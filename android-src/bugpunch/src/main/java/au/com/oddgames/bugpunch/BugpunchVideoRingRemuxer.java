package au.com.oddgames.bugpunch;

import android.media.MediaCodec;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.util.Log;

import java.io.File;
import java.io.RandomAccessFile;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.MappedByteBuffer;
import java.nio.channels.FileChannel;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Reads the on-disk video ring written by bp_video.c during a previous
 * session and remuxes the recovered samples into a plain {@code .mp4} via
 * Android's {@link MediaMuxer}. Runs on next launch from
 * {@link BugpunchCrashDrain#drain} — never in the hot crash path.
 *
 * <p>The ring format is intentionally simple (single header page + index
 * ring + payload ring, all in one file) so the writer can be async-signal-
 * safe. All the work to turn it into a playable mp4 happens here, where we
 * have a live process and can use any API we like.
 */
final class BugpunchVideoRingRemuxer {
    private static final String TAG = "[Bugpunch.VideoRing]";

    private BugpunchVideoRingRemuxer() {}

    /** Result of a remux attempt. Exactly one of {@code path} / {@code reason}
     *  is non-null. {@code reason} is a short stable token (snake_case) so
     *  dashboards can switch on it. */
    static final class Result {
        final String path;
        final String reason;
        private Result(String path, String reason) { this.path = path; this.reason = reason; }
        static Result ok(String path)        { return new Result(path, null); }
        static Result fail(String reason)    { return new Result(null, reason); }
    }

    // Layout constants — must match bp_video.c.
    private static final int    MAGIC          = 0x52565042; // 'BPVR'
    private static final int    VERSION        = 1;
    private static final int    HEADER_SIZE    = 4096;
    private static final int    MAX_CSD        = 256;
    private static final int    IDX_ENTRY_SIZE = 24;
    private static final int    FLAG_KEYFRAME  = 0x1;
    private static final String MIME_AVC       = "video/avc";

    /**
     * Try to remux {@code path} (the bp_video.dat ring) into an mp4 written
     * next to it. Returns the mp4 path on success, or {@code null} if the
     * ring couldn't be opened, contained no usable samples, or the muxer
     * failed.
     *
     * <p>Caller is responsible for snapshotting / cleaning up the returned
     * path; this method does not touch it after writing.
     */
    static Result remux(String ringPath, File outputDir) {
        File ringFile = new File(ringPath);
        if (!ringFile.exists()) return Result.fail("ring_missing");
        if (ringFile.length() < HEADER_SIZE) return Result.fail("ring_truncated");

        RandomAccessFile raf = null;
        FileChannel ch = null;
        MediaMuxer muxer = null;
        File outFile = null;
        try {
            raf = new RandomAccessFile(ringFile, "r");
            ch = raf.getChannel();
            // Map the whole ring read-only. On a 30-90s ring this is well
            // under 64 MB, comfortably within the per-process address space
            // even on 32-bit ABIs we no longer ship.
            MappedByteBuffer map = ch.map(FileChannel.MapMode.READ_ONLY,
                0, ringFile.length());
            map.order(ByteOrder.nativeOrder());

            Header h = readHeader(map);
            if (h == null) return Result.fail("ring_bad_header");
            if (!h.formatSet || h.spsLen == 0 || h.ppsLen == 0) {
                Log.i(TAG, "ring has no SPS/PPS — recorder didn't run long enough");
                return Result.fail("no_csd_yet");
            }
            if (h.payloadHead == 0 || h.idxHead == 0) {
                Log.i(TAG, "ring has no samples");
                return Result.fail("no_samples");
            }

            // Recover the index entries that haven't been overwritten by
            // payload-ring wrap. An entry is valid iff its payload range
            // [offset, offset+len) is still within (payload_head -
            // payload_cap, payload_head]. Walk in chronological order so PTS
            // stays monotonic.
            List<IdxEntry> valid = collectValidEntries(map, h);
            if (valid.isEmpty()) {
                Log.i(TAG, "ring has entries but all are overwritten");
                return Result.fail("ring_overwritten");
            }

            // Trim to start at the oldest keyframe — H.264 isn't decodable
            // before an IDR. If no keyframe survives, the whole window is
            // unplayable; return null and the drain skips video for this
            // crash.
            int firstKf = -1;
            for (int i = 0; i < valid.size(); i++) {
                if ((valid.get(i).flags & FLAG_KEYFRAME) != 0) { firstKf = i; break; }
            }
            if (firstKf < 0) {
                Log.i(TAG, "ring has no surviving keyframe");
                return Result.fail("no_keyframe");
            }
            List<IdxEntry> playable = valid.subList(firstKf, valid.size());

            // Build the MediaFormat the muxer needs. SPS goes in csd-0, PPS
            // in csd-1 — the same convention MediaCodec emits when it fires
            // INFO_OUTPUT_FORMAT_CHANGED.
            MediaFormat fmt = MediaFormat.createVideoFormat(MIME_AVC, h.width, h.height);
            fmt.setByteBuffer("csd-0", ByteBuffer.wrap(h.sps));
            fmt.setByteBuffer("csd-1", ByteBuffer.wrap(h.pps));

            outFile = new File(outputDir, "bp_video_" + System.currentTimeMillis() + ".mp4");
            muxer = new MediaMuxer(outFile.getAbsolutePath(),
                MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            int track = muxer.addTrack(fmt);
            muxer.start();

            long basePtsUs = playable.get(0).ptsUs;
            byte[] sampleBuf = new byte[64 * 1024];
            MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
            int written = 0;
            for (IdxEntry e : playable) {
                if (e.len > sampleBuf.length) sampleBuf = new byte[e.len];
                readPayloadRange(map, h, e.payloadOffset, e.len, sampleBuf);
                ByteBuffer bb = ByteBuffer.wrap(sampleBuf, 0, e.len);
                info.offset = 0;
                info.size = e.len;
                info.presentationTimeUs = e.ptsUs - basePtsUs;
                info.flags = (e.flags & FLAG_KEYFRAME) != 0
                    ? MediaCodec.BUFFER_FLAG_KEY_FRAME : 0;
                muxer.writeSampleData(track, bb, info);
                written++;
            }
            muxer.stop();
            muxer.release();
            muxer = null;
            Log.i(TAG, "remuxed " + written + " samples → " + outFile.getName()
                + " (" + outFile.length() + " bytes)");
            return Result.ok(outFile.getAbsolutePath());
        } catch (Throwable t) {
            Log.w(TAG, "remux failed", t);
            if (outFile != null) try { outFile.delete(); } catch (Exception ignored) {}
            return Result.fail("remux_threw");
        } finally {
            if (muxer != null) try { muxer.release(); } catch (Exception ignored) {}
            if (ch != null)    try { ch.close();    } catch (Exception ignored) {}
            if (raf != null)   try { raf.close();   } catch (Exception ignored) {}
        }
    }

    // ── Header / index helpers ──

    private static class Header {
        int magic, version;
        long payloadOff, payloadCap, payloadHead;
        long idxOff, idxCap, idxHead;
        int width, height, fps;
        boolean formatSet;
        int spsLen, ppsLen;
        byte[] sps, pps;
    }

    private static class IdxEntry {
        long ptsUs;
        long payloadOffset;
        int  len;
        int  flags;
    }

    private static Header readHeader(MappedByteBuffer m) {
        m.position(0);
        Header h = new Header();
        h.magic       = m.getInt();
        h.version     = m.getInt();
        if (h.magic != MAGIC || h.version != VERSION) {
            Log.w(TAG, "bad ring magic/version: " + Integer.toHexString(h.magic)
                + " v=" + h.version);
            return null;
        }
        h.payloadOff  = m.getLong();
        h.payloadCap  = m.getLong();
        h.payloadHead = m.getLong();
        h.idxOff      = m.getLong();
        h.idxCap      = m.getLong();
        h.idxHead     = m.getLong();
        h.width       = m.getInt();
        h.height      = m.getInt();
        h.fps         = m.getInt();
        h.formatSet   = m.getInt() != 0;
        h.spsLen      = m.getInt();
        h.sps = new byte[Math.min(h.spsLen, MAX_CSD)];
        m.get(h.sps);
        // Skip remaining bytes of the fixed-size sps[MAX_CSD] field.
        if (h.spsLen < MAX_CSD) m.position(m.position() + (MAX_CSD - h.spsLen));
        h.ppsLen      = m.getInt();
        h.pps = new byte[Math.min(h.ppsLen, MAX_CSD)];
        m.get(h.pps);
        return h;
    }

    private static List<IdxEntry> collectValidEntries(MappedByteBuffer m, Header h) {
        List<IdxEntry> out = new ArrayList<>();
        long entries = Math.min(h.idxHead, h.idxCap);
        // Window of payload offsets that haven't been overwritten yet:
        //   (payloadHead - payloadCap, payloadHead]
        long payloadFloor = h.payloadHead > h.payloadCap
            ? h.payloadHead - h.payloadCap : 0;
        long oldestEntryAbs = h.idxHead - entries;
        for (long i = 0; i < entries; i++) {
            long entryAbs = oldestEntryAbs + i;
            int phys = (int) (entryAbs % h.idxCap);
            int base = (int) h.idxOff + phys * IDX_ENTRY_SIZE;
            m.position(base);
            IdxEntry e = new IdxEntry();
            e.ptsUs         = m.getLong();
            e.payloadOffset = m.getLong();
            e.len           = m.getInt();
            e.flags         = m.getInt();
            // Drop entries whose payload bytes have wrapped under us, AND
            // any garbage left over from a torn write (we publish the head
            // last, so a too-recent entry without backing payload is impossible
            // in steady state — but guard anyway against future writer bugs).
            if (e.len <= 0) continue;
            if (e.payloadOffset < payloadFloor) continue;
            if (e.payloadOffset + e.len > h.payloadHead) continue;
            out.add(e);
        }
        Collections.sort(out, (a, b) -> Long.compare(a.ptsUs, b.ptsUs));
        return out;
    }

    private static void readPayloadRange(MappedByteBuffer m, Header h,
            long absOffset, int len, byte[] dst) {
        long phys = absOffset % h.payloadCap;
        int  base = (int) h.payloadOff + (int) phys;
        long until = phys + len;
        if (until <= h.payloadCap) {
            m.position(base);
            m.get(dst, 0, len);
        } else {
            int first = (int) (h.payloadCap - phys);
            m.position(base);
            m.get(dst, 0, first);
            m.position((int) h.payloadOff);
            m.get(dst, first, len - first);
        }
    }
}
