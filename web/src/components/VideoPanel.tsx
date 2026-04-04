import { useRef, useEffect, useCallback, useState } from 'react';

export interface HighlightBounds {
  x: number;      // screen-space left
  y: number;      // screen-space bottom (Unity Y-up)
  w: number;      // width in screen pixels
  h: number;      // height in screen pixels
  screenWidth: number;   // total game screen width
  screenHeight: number;  // total game screen height
}

interface VideoPanelProps {
  videoUrl: string | null;
  screenshotUrl: string | null;
  duration: number;
  currentTime: number;
  onTimeChange: (time: number) => void;
  paused?: number; // increments to trigger pause
  fps?: number;
  highlightBounds?: HighlightBounds | null;
  screenSize?: { w: number; h: number } | null; // Unity screen dimensions for click mapping
  onVideoClick?: (unityX: number, unityY: number) => void;
  onVideoContextMenu?: (unityX: number, unityY: number, clientX: number, clientY: number) => void;
  videoStartOffset?: number;          // seconds between session start and recording start
  videoTimestamps?: Float32Array | null; // per-frame session times from sidecar CSV
}

/** Compute the rendered media area within a container (object-fit: contain logic). */
function computeMediaLayout(containerW: number, containerH: number, screenWidth: number, screenHeight: number) {
  const containerAR = containerW / containerH;
  const mediaAR = screenWidth / screenHeight;

  let mediaW: number, mediaH: number, offsetX: number, offsetY: number;
  if (mediaAR > containerAR) {
    mediaW = containerW;
    mediaH = containerW / mediaAR;
    offsetX = 0;
    offsetY = (containerH - mediaH) / 2;
  } else {
    mediaH = containerH;
    mediaW = containerH * mediaAR;
    offsetX = (containerW - mediaW) / 2;
    offsetY = 0;
  }
  return { mediaW, mediaH, offsetX, offsetY };
}

export function VideoPanel({ videoUrl, screenshotUrl, duration, currentTime, onTimeChange, paused, fps = 15, highlightBounds, screenSize, onVideoClick, onVideoContextMenu, videoStartOffset = 0, videoTimestamps }: VideoPanelProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [playing, setPlaying] = useState(false);
  const [videoDuration, setVideoDuration] = useState<number | null>(null);
  const [videoSize, setVideoSize] = useState<{ w: number; h: number } | null>(null);
  const [containerSize, setContainerSize] = useState<{ w: number; h: number } | null>(null);
  // Tracks wall-clock time for advancing past video end
  const pastVideoStartRef = useRef<{ wallTime: number; sessionTime: number } | null>(null);

  // Map session time → video time (accounting for recording start offset)
  const sessionToVideo = useCallback((sessionTime: number) => {
    return Math.max(0, sessionTime - videoStartOffset);
  }, [videoStartOffset]);

  // Map video time → session time
  const videoToSession = useCallback((videoTime: number) => {
    return videoTime + videoStartOffset;
  }, [videoStartOffset]);

  useEffect(() => {
    if (!videoRef.current) return;
    const video = videoRef.current;
    video.onloadedmetadata = () => {
      setVideoDuration(video.duration);
      setVideoSize({ w: video.videoWidth, h: video.videoHeight });
    };
    return () => {
      video.onloadedmetadata = null;
    };
  }, []);

  // Track container size for overlay scaling
  useEffect(() => {
    if (!containerRef.current) return;
    const observer = new ResizeObserver(entries => {
      for (const entry of entries) {
        setContainerSize({ w: entry.contentRect.width, h: entry.contentRect.height });
      }
    });
    observer.observe(containerRef.current);
    return () => observer.disconnect();
  }, []);

  // Use requestAnimationFrame for smooth time updates during playback
  useEffect(() => {
    if (!playing) return;
    let raf: number;
    const tick = () => {
      if (videoDuration !== null && videoRef.current && videoRef.current.currentTime < videoDuration - 0.05) {
        // Video is still playing — convert video time to session time
        onTimeChange(videoToSession(videoRef.current.currentTime));
        pastVideoStartRef.current = null;
      } else {
        // Past video end — advance using wall clock
        if (!pastVideoStartRef.current) {
          pastVideoStartRef.current = {
            wallTime: performance.now(),
            sessionTime: videoDuration != null ? videoToSession(videoDuration) : currentTime,
          };
        }
        const elapsed = (performance.now() - pastVideoStartRef.current.wallTime) / 1000;
        const t = pastVideoStartRef.current.sessionTime + elapsed;
        if (t >= duration) {
          onTimeChange(duration);
          setPlaying(false);
          pastVideoStartRef.current = null;
          return;
        }
        onTimeChange(t);
      }
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [playing, onTimeChange, videoDuration, duration, videoToSession]);

  // Reset video duration when URL changes
  useEffect(() => { setVideoDuration(null); }, [videoUrl]);

  // Pause + seek when parent requests it (timeline/console click)
  useEffect(() => {
    if (!paused || !videoRef.current) return;
    videoRef.current.pause();
    setPlaying(false);
    pastVideoStartRef.current = null;
    // Convert session time to video time, clamped to video duration
    const videoTime = sessionToVideo(currentTime);
    const target = videoDuration !== null && videoTime > videoDuration ? videoDuration : videoTime;
    videoRef.current.currentTime = Math.max(0, target);
  }, [paused]); // only react to paused changes — currentTime is read but not a dependency

  // Session time is past the video's coverage
  const videoEnd = videoDuration != null ? videoToSession(videoDuration) : null;
  const pastEnd = videoEnd !== null && currentTime > videoEnd + 0.1;

  const togglePlay = useCallback(() => {
    if (!videoRef.current) return;
    if (playing) {
      videoRef.current.pause();
      setPlaying(false);
      pastVideoStartRef.current = null;
    } else {
      // If before video end, start video playback
      if (videoDuration === null || sessionToVideo(currentTime) < videoDuration) {
        videoRef.current.play();
      }
      // pastVideoStartRef will be set by the RAF tick when needed
      setPlaying(true);
    }
  }, [playing, videoDuration, currentTime, sessionToVideo]);

  const stepFrame = useCallback((direction: number) => {
    if (!videoRef.current) return;
    videoRef.current.pause();
    setPlaying(false);
    pastVideoStartRef.current = null;
    const newTime = Math.max(0, Math.min(currentTime + direction / fps, duration));
    onTimeChange(newTime);
    // Seek video if within video range
    const newVideoTime = sessionToVideo(newTime);
    if (videoDuration !== null && newVideoTime <= videoDuration) {
      videoRef.current.currentTime = newVideoTime;
    }
  }, [fps, duration, currentTime, onTimeChange, videoDuration, sessionToVideo]);

  // Sync external time changes when paused (scrubbing without explicit seek)
  useEffect(() => {
    if (!videoRef.current || playing) return;
    const videoTime = sessionToVideo(currentTime);
    const diff = Math.abs(videoRef.current.currentTime - videoTime);
    if (diff > 0.1) {
      const target = videoDuration !== null && videoTime > videoDuration ? videoDuration : videoTime;
      videoRef.current.currentTime = Math.max(0, target);
    }
  }, [currentTime, playing, videoDuration, sessionToVideo]);

  const formatTime = (t: number) => {
    const m = Math.floor(t / 60);
    const s = Math.floor(t % 60);
    const ms = Math.floor((t % 1) * 100);
    return `${m}:${String(s).padStart(2, '0')}.${String(ms).padStart(2, '0')}`;
  };

  // Convert CSS mouse position to Unity screen-space coordinates
  const cssToUnity = useCallback((clientX: number, clientY: number): { x: number; y: number } | null => {
    const container = containerRef.current;
    const sz = screenSize ?? (highlightBounds ? { w: highlightBounds.screenWidth, h: highlightBounds.screenHeight } : null);
    if (!container || !containerSize || !sz || sz.w <= 0 || sz.h <= 0) return null;

    const rect = container.getBoundingClientRect();
    const relX = clientX - rect.left;
    const relY = clientY - rect.top;

    const { mediaW, mediaH, offsetX, offsetY } = computeMediaLayout(containerSize.w, containerSize.h, sz.w, sz.h);

    const unityX = (relX - offsetX) * (sz.w / mediaW);
    const unityY = sz.h - (relY - offsetY) * (sz.h / mediaH); // flip Y
    return { x: unityX, y: unityY };
  }, [containerSize, screenSize, highlightBounds]);

  const handleClick = useCallback((e: React.MouseEvent) => {
    if (!onVideoClick) return;
    const pt = cssToUnity(e.clientX, e.clientY);
    if (pt) onVideoClick(pt.x, pt.y);
  }, [onVideoClick, cssToUnity]);

  const handleContextMenu = useCallback((e: React.MouseEvent) => {
    if (!onVideoContextMenu) return;
    e.preventDefault();
    const pt = cssToUnity(e.clientX, e.clientY);
    if (pt) onVideoContextMenu(pt.x, pt.y, e.clientX, e.clientY);
  }, [onVideoContextMenu, cssToUnity]);

  // Compute overlay rectangle CSS from Unity screen-space bounds
  const overlayStyle = (() => {
    if (!highlightBounds || !containerSize) return null;
    const { x, y, w, h, screenWidth, screenHeight } = highlightBounds;
    if (w <= 0 || h <= 0 || screenWidth <= 0 || screenHeight <= 0) return null;

    const { mediaW, mediaH, offsetX, offsetY } = computeMediaLayout(containerSize.w, containerSize.h, screenWidth, screenHeight);
    const scaleX = mediaW / screenWidth;
    const scaleY = mediaH / screenHeight;

    // Unity Y is bottom-up, CSS Y is top-down
    const cssLeft = offsetX + x * scaleX;
    const cssTop = offsetY + (screenHeight - y - h) * scaleY;
    const cssWidth = w * scaleX;
    const cssHeight = h * scaleY;

    return {
      position: 'absolute' as const,
      left: cssLeft,
      top: cssTop,
      width: cssWidth,
      height: cssHeight,
      border: '2px solid #4D9FFF',
      background: 'rgba(77, 159, 255, 0.12)',
      pointerEvents: 'none' as const,
      borderRadius: 2,
      zIndex: 10,
    };
  })();

  return (
    <div className="flex flex-col">
      {/* Video / Screenshot display */}
      <div ref={containerRef} className="relative flex items-center justify-center" style={{
        background: '#111',
        minHeight: 200,
        aspectRatio: videoSize ? `${videoSize.w} / ${videoSize.h}` : undefined,
        maxHeight: 480,
      }}>
        {screenshotUrl ? (
          <img
            src={screenshotUrl}
            alt="Screenshot"
            className="max-w-full max-h-[480px] object-contain"
          />
        ) : videoUrl ? (
          <>
            <video
              ref={videoRef}
              src={videoUrl}
              className="max-w-full max-h-[480px] object-contain"
              style={{ display: pastEnd ? 'none' : undefined }}
              preload="auto"
              playsInline
            />
            {pastEnd && (
              <div className="text-center" style={{ color: 'var(--text-muted)' }}>
                Recording ended — test teardown in progress
              </div>
            )}
          </>
        ) : (
          <div className="text-center" style={{ color: 'var(--text-muted)' }}>
            {videoUrl === null ? 'No video available' : 'Loading video...'}
          </div>
        )}

        {/* Highlight overlay for selected hierarchy node */}
        {overlayStyle && <div style={overlayStyle} />}

        {/* Transparent interactive overlay for click-to-select */}
        {(onVideoClick || onVideoContextMenu) && (
          <div
            onClick={handleClick}
            onContextMenu={handleContextMenu}
            style={{
              position: 'absolute',
              inset: 0,
              zIndex: 20,
              cursor: 'crosshair',
            }}
          />
        )}
      </div>

      {/* Controls */}
      {videoUrl && (
        <div className="flex items-center gap-2 px-2 py-0.5" style={{ background: 'var(--bg-secondary)', borderTop: '1px solid var(--border-color)' }}>
          <button onClick={() => stepFrame(-1)} className="px-1 opacity-60 hover:opacity-100 cursor-pointer" style={{ color: 'var(--text-primary)', fontSize: 11 }} title="Previous frame">
            |&lt;
          </button>
          <button onClick={togglePlay} className="px-1 opacity-80 hover:opacity-100 cursor-pointer" style={{ color: 'var(--text-primary)', fontSize: 11 }} title={playing ? 'Pause' : 'Play'}>
            {playing ? '||' : '\u25B6'}
          </button>
          <button onClick={() => stepFrame(1)} className="px-1 opacity-60 hover:opacity-100 cursor-pointer" style={{ color: 'var(--text-primary)', fontSize: 11 }} title="Next frame">
            &gt;|
          </button>
          <span className="text-xs font-mono whitespace-nowrap" style={{ color: 'var(--text-muted)' }}>
            {formatTime(currentTime)} / {formatTime(duration)}
          </span>
        </div>
      )}
    </div>
  );
}
