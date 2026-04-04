import { useRef, useCallback, useEffect, useState } from 'react';
import type { DiagEvent } from '../lib/types';
import { EventColors } from '../lib/types';

interface TimelineBarProps {
  events: DiagEvent[];
  duration: number;
  currentTime: number;
  onSeek: (time: number) => void;
}

export function TimelineBar({ events, duration, currentTime, onSeek }: TimelineBarProps) {
  const barRef = useRef<HTMLDivElement>(null);
  const [dragging, setDragging] = useState(false);

  const getTimeFromX = useCallback((clientX: number) => {
    if (!barRef.current || duration <= 0) return null;
    const rect = barRef.current.getBoundingClientRect();
    const x = clientX - rect.left;
    const t = (x / rect.width) * duration;
    return Math.max(0, Math.min(t, duration));
  }, [duration]);

  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    const t = getTimeFromX(e.clientX);
    if (t !== null) {
      onSeek(t);
      setDragging(true);
    }
  }, [getTimeFromX, onSeek]);

  useEffect(() => {
    if (!dragging) return;

    const handleMouseMove = (e: MouseEvent) => {
      const t = getTimeFromX(e.clientX);
      if (t !== null) onSeek(t);
    };

    const handleMouseUp = () => setDragging(false);

    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);
    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };
  }, [dragging, getTimeFromX, onSeek]);

  const playheadPos = duration > 0 ? (currentTime / duration) * 100 : 0;

  return (
    <div
      ref={barRef}
      className="relative cursor-pointer select-none"
      style={{ height: 28, background: '#1f1f1f', borderTop: '1px solid var(--border-color)', borderBottom: '1px solid var(--border-color)' }}
      onMouseDown={handleMouseDown}
    >
      {/* Event ticks */}
      {events.map((evt, i) => {
        if (duration <= 0) return null;
        const pos = (evt.timestamp / duration) * 100;
        const color = EventColors[evt.type] || EventColors.default;
        return (
          <div
            key={i}
            className="absolute top-0 bottom-0"
            style={{ left: `${pos}%`, width: 2, background: color, opacity: 0.8 }}
            title={`${evt.type}: ${evt.label} (${evt.timestamp.toFixed(2)}s)`}
          />
        );
      })}

      {/* Playhead */}
      <div
        className="absolute top-0 bottom-0"
        style={{ left: `${playheadPos}%`, width: 2, background: '#fff', zIndex: 10 }}
      >
        <div
          style={{ width: 0, height: 0, borderLeft: '4px solid transparent', borderRight: '4px solid transparent', borderTop: '6px solid #fff', position: 'absolute', top: 0, left: -3 }}
        />
      </div>
    </div>
  );
}
