using System.IO;
using System.Text;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Minimal ELF parser for extracting the GNU build-ID from a 64-bit or
    /// 32-bit little-endian Android .so on disk. Supports ELF64 (arm64-v8a,
    /// x86_64) and ELF32 (armeabi-v7a, x86) — the four ABIs Unity targets.
    ///
    /// Reads only the ELF header, program header table, and PT_NOTE segments
    /// (usually &lt;1KB total). The rest of the .so is never touched.
    /// </summary>
    static class ElfBuildId
    {
        const int NT_GNU_BUILD_ID = 3;

        public static string ReadFromFile(string path)
        {
            using var fs = File.OpenRead(path);

            var ehdr = new byte[64];
            if (ReadFull(fs, ehdr, 0, 64) < 64) return null;
            if (ehdr[0] != 0x7F || ehdr[1] != 'E' || ehdr[2] != 'L' || ehdr[3] != 'F') return null;
            bool is64 = ehdr[4] == 2;
            bool isLE = ehdr[5] == 1;
            if (!isLE) return null;

            long phoff = is64 ? (long)ReadU64(ehdr, 32) : ReadU32(ehdr, 28);
            int phentsize = is64 ? ReadU16(ehdr, 54) : ReadU16(ehdr, 42);
            int phnum     = is64 ? ReadU16(ehdr, 56) : ReadU16(ehdr, 44);

            if (phoff <= 0 || phentsize <= 0 || phnum <= 0) return null;
            if (phentsize * (long)phnum > 64 * 1024) return null; // sanity

            fs.Seek(phoff, SeekOrigin.Begin);
            var ph = new byte[phentsize * phnum];
            if (ReadFull(fs, ph, 0, ph.Length) < ph.Length) return null;

            for (int i = 0; i < phnum; i++)
            {
                int off = i * phentsize;
                uint pType = ReadU32(ph, off);
                if (pType != 4 /* PT_NOTE */) continue;

                long pOffset = is64 ? (long)ReadU64(ph, off + 8)  : ReadU32(ph, off + 4);
                long pFilesz = is64 ? (long)ReadU64(ph, off + 32) : ReadU32(ph, off + 16);
                if (pOffset <= 0 || pFilesz <= 0 || pFilesz > 1024 * 1024) continue;

                fs.Seek(pOffset, SeekOrigin.Begin);
                var notes = new byte[pFilesz];
                if (ReadFull(fs, notes, 0, notes.Length) < notes.Length) continue;

                var id = ScanNotesForBuildId(notes);
                if (!string.IsNullOrEmpty(id)) return id;
            }
            return null;
        }

        static int ReadFull(Stream s, byte[] buf, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, offset + read, count - read);
                if (n <= 0) break;
                read += n;
            }
            return read;
        }

        static string ScanNotesForBuildId(byte[] notes)
        {
            int p = 0;
            int end = notes.Length;
            while (p + 12 <= end)
            {
                int namesz = (int)ReadU32(notes, p);
                int descsz = (int)ReadU32(notes, p + 4);
                int type   = (int)ReadU32(notes, p + 8);
                int namePad = (namesz + 3) & ~3;
                int descPad = (descsz + 3) & ~3;
                int descStart = p + 12 + namePad;
                if (type == NT_GNU_BUILD_ID && descsz > 0 && descStart + descsz <= end)
                {
                    var sb = new StringBuilder(descsz * 2);
                    for (int i = 0; i < descsz; i++)
                        sb.Append(notes[descStart + i].ToString("x2"));
                    return sb.ToString();
                }
                p = descStart + descPad;
            }
            return null;
        }

        static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
        static uint ReadU32(byte[] b, int o) =>
            (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        static ulong ReadU64(byte[] b, int o) =>
            ReadU32(b, o) | ((ulong)ReadU32(b, o + 4) << 32);
    }
}
