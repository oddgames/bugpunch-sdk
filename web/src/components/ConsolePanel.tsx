import { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import type { LogEntry, DiagEvent } from '../lib/types';
import { LogType, RowTints } from '../lib/types';
import { buildActionGroups, stripRichText, formatActionDuration } from '../lib/actionGroups';

interface ConsolePanelProps {
  logs: LogEntry[];
  events: DiagEvent[];
  currentTime: number;
  onSeek: (time: number) => void;
  onScreenshotClick: (fileName: string) => void;
  onSnapshotClick: (fileName: string) => void;
  highlightedIndex: number | null;
  activeHierarchyFile: string | null;
}

const LOG_ICONS: Record<number, string> = {
  [LogType.Error]: '\u274C',      // red X
  [LogType.Assert]: '\u274C',
  [LogType.Warning]: '\u26A0\uFE0F',  // warning
  [LogType.Log]: '\u2139\uFE0F',       // info
  [LogType.Exception]: '\u274C',
  [LogType.Screenshot]: '\uD83D\uDDBC\uFE0F',  // framed picture
  [LogType.Snapshot]: '\uD83C\uDFE0',   // house (hierarchy)
  [LogType.ActionStart]: '\u25B6',       // play
  [LogType.ActionSuccess]: '\u2705',     // check
  [LogType.ActionWarn]: '\u26A0\uFE0F',
  [LogType.ActionFailure]: '\u274C',
};

type FilterKey = 'actions' | 'logs' | 'warnings' | 'errors' | 'screenshots' | 'snapshots';

const FILTER_TYPES: Record<FilterKey, number[]> = {
  actions: [LogType.ActionStart, LogType.ActionSuccess, LogType.ActionWarn, LogType.ActionFailure],
  logs: [LogType.Log],
  warnings: [LogType.Warning],
  errors: [LogType.Error, LogType.Assert, LogType.Exception],
  screenshots: [LogType.Screenshot],
  snapshots: [LogType.Snapshot],
};

export function ConsolePanel({ logs, events, onSeek, onScreenshotClick, onSnapshotClick, highlightedIndex, activeHierarchyFile }: ConsolePanelProps) {
  const [filters, setFilters] = useState<Record<FilterKey, boolean>>({
    actions: true, logs: true, warnings: true, errors: true, screenshots: true, snapshots: true,
  });
  const [search, setSearch] = useState('');
  const listRef = useRef<HTMLDivElement>(null);
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const prevHighlightRef = useRef<number | null>(null);
  const scrollTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Resizable stack trace panel
  const [stackHeight, setStackHeight] = useState(80);
  const dragging = useRef(false);
  const dragStartY = useRef(0);
  const dragStartHeight = useRef(0);

  const handleDragStart = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    dragging.current = true;
    dragStartY.current = e.clientY;
    dragStartHeight.current = stackHeight;

    const onMove = (ev: MouseEvent) => {
      if (!dragging.current) return;
      const delta = dragStartY.current - ev.clientY; // dragging up = bigger
      setStackHeight(Math.max(24, Math.min(400, dragStartHeight.current + delta)));
    };
    const onUp = () => {
      dragging.current = false;
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
    };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  }, [stackHeight]);

  const actionGroups = useMemo(() => buildActionGroups(logs), [logs]);

  // Map log entries to their nearest hierarchy file by matching timestamps to events
  const logHierarchyMap = useMemo(() => {
    const map = new Map<number, string>(); // logIndex -> hierarchyFile
    if (!events.length) return map;

    // Build sorted list of events with hierarchyFile
    const hierarchyEvents = events
      .filter(e => e.hierarchyFile)
      .sort((a, b) => a.timestamp - b.timestamp);
    if (!hierarchyEvents.length) return map;

    for (let i = 0; i < logs.length; i++) {
      const log = logs[i];
      // Find the most recent hierarchy event at or before this log's timestamp (with small tolerance for async)
      let best: string | null = null;
      for (const evt of hierarchyEvents) {
        if (evt.timestamp <= log.timestamp + 0.05) {
          best = evt.hierarchyFile!;
        }
      }
      if (best) map.set(i, best);
    }
    return map;
  }, [logs, events]);

  const toggleFilter = (key: FilterKey) => {
    setFilters(f => ({ ...f, [key]: !f[key] }));
  };

  // Build visible entry indices
  const visibleIndices = useMemo(() => {
    const activeTypes = new Set<number>();
    (Object.entries(filters) as [FilterKey, boolean][]).forEach(([key, on]) => {
      if (on) FILTER_TYPES[key].forEach(t => activeTypes.add(t));
    });

    const result: number[] = [];
    const searchLower = search.toLowerCase();

    for (let i = 0; i < logs.length; i++) {
      const entry = logs[i];
      if (!activeTypes.has(entry.logType)) continue;
      if (searchLower && !stripRichText(entry.message).toLowerCase().includes(searchLower)) continue;
      result.push(i);
    }

    return result;
  }, [logs, filters, search]);

  // Count by type
  const counts = useMemo(() => {
    const c: Record<FilterKey, number> = { actions: 0, logs: 0, warnings: 0, errors: 0, screenshots: 0, snapshots: 0 };
    for (const entry of logs) {
      for (const [key, types] of Object.entries(FILTER_TYPES)) {
        if (types.includes(entry.logType)) c[key as FilterKey]++;
      }
    }
    return c;
  }, [logs]);

  // Resolve highlighted index to nearest visible entry
  const effectiveHighlight = useMemo(() => {
    if (highlightedIndex === null) return null;
    if (visibleIndices.includes(highlightedIndex)) return highlightedIndex;
    // Find nearest visible entry by index distance
    let best: number | null = null;
    let bestDist = Infinity;
    for (const vi of visibleIndices) {
      const dist = Math.abs(vi - highlightedIndex);
      if (dist < bestDist) {
        bestDist = dist;
        best = vi;
      }
    }
    return best;
  }, [highlightedIndex, visibleIndices]);

  // Auto-scroll to highlighted entry — debounced to prevent jitter during rapid updates
  useEffect(() => {
    if (effectiveHighlight === null || !listRef.current) return;
    if (prevHighlightRef.current === effectiveHighlight) return;
    prevHighlightRef.current = effectiveHighlight;

    // Clear any pending scroll
    if (scrollTimeoutRef.current) clearTimeout(scrollTimeoutRef.current);

    // Debounce: wait for highlight to settle before scrolling
    scrollTimeoutRef.current = setTimeout(() => {
      if (!listRef.current) return;
      const row = listRef.current.querySelector(`[data-idx="${effectiveHighlight}"]`);
      if (row) {
        // Check if already in view — skip scroll if visible to avoid jitter
        const container = listRef.current;
        const rowRect = row.getBoundingClientRect();
        const containerRect = container.getBoundingClientRect();
        const isVisible = rowRect.top >= containerRect.top && rowRect.bottom <= containerRect.bottom;
        if (!isVisible) {
          row.scrollIntoView({ block: 'nearest', behavior: 'auto' });
        }
      }
    }, 80);

    return () => {
      if (scrollTimeoutRef.current) clearTimeout(scrollTimeoutRef.current);
    };
  }, [effectiveHighlight]);

  const handleEntryClick = useCallback((idx: number) => {
    const entry = logs[idx];
    setSelectedIndex(idx);

    if (entry.logType === LogType.Screenshot && entry.stackTrace) {
      onScreenshotClick(entry.stackTrace);
    } else if (entry.logType === LogType.Snapshot && entry.stackTrace) {
      onSnapshotClick(entry.stackTrace);
    } else {
      onSeek(entry.timestamp);
    }
  }, [logs, onSeek, onScreenshotClick, onSnapshotClick]);

  const selectedEntry = selectedIndex !== null ? logs[selectedIndex] : null;

  return (
    <div className="flex flex-col flex-1 min-h-0">
      {/* Toolbar */}
      <div className="flex items-center gap-1 px-2 py-1 flex-wrap" style={{ background: 'var(--bg-secondary)', borderTop: '1px solid var(--border-color)', borderBottom: '1px solid var(--border-color)' }}>
        {(Object.entries(FILTER_TYPES) as [FilterKey, number[]][]).map(([key]) => (
          <button
            key={key}
            onClick={() => toggleFilter(key)}
            className={`px-1.5 py-0.5 rounded text-xs flex items-center gap-1 ${filters[key] ? 'opacity-100' : 'opacity-30'}`}
            style={{ color: 'var(--text-secondary)' }}
          >
            {key.charAt(0).toUpperCase() + key.slice(1)}
            <span className="text-[10px] opacity-60">{counts[key]}</span>
          </button>
        ))}
        <div className="flex-1" />
        <input
          type="text"
          placeholder="Search..."
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="px-1.5 py-0.5 rounded text-xs border outline-none"
          style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)', width: 120 }}
        />
      </div>

      {/* Log entries */}
      <div ref={listRef} className="flex-1 overflow-y-auto" style={{ background: 'var(--bg-primary)', minHeight: 80 }}>
        {visibleIndices.map((idx, visIdx) => {
          const entry = logs[idx];
          const isHighlighted = idx === effectiveHighlight;
          const isSelected = idx === selectedIndex;
          const tint = RowTints[entry.logType];
          const altBg = visIdx % 2 === 0 ? 'var(--bg-primary)' : 'var(--bg-secondary)';
          const logHierFile = logHierarchyMap.get(idx);
          const isActiveHierarchy = logHierFile != null && logHierFile === activeHierarchyFile;

          // Check if this entry is inside an action group (for indentation)
          let isInnerEntry = false;
          for (const [startIdx, g] of actionGroups) {
            if (idx > startIdx && idx <= g.endIndex) {
              isInnerEntry = true;
              break;
            }
          }

          // Find duration for COMPLETE entries (end of a group)
          let groupDuration: number | null = null;
          for (const [, g] of actionGroups) {
            if (g.endIndex === idx) {
              groupDuration = g.duration;
              break;
            }
          }

          return (
            <div
              key={idx}
              data-idx={idx}
              onClick={() => handleEntryClick(idx)}
              className="flex items-center px-2 cursor-pointer hover:brightness-110"
              style={{
                height: 22,
                background: isHighlighted ? 'rgba(128, 179, 255, 0.15)' : isSelected ? 'rgba(128, 179, 255, 0.1)' : tint || altBg,
                borderLeft: isHighlighted ? '2px solid var(--accent-blue)' : '2px solid transparent',
              }}
            >
              {/* Current playback indicator */}
              <span className="text-[9px] mr-0.5" style={{ width: 8, color: 'var(--accent-blue)', opacity: isHighlighted ? 1 : 0 }}>
                {'\u25B8'}
              </span>

              {/* Indent for inner group entries */}
              {isInnerEntry && <span style={{ width: 16 }} />}

              {/* Icon */}
              <span className="mr-1.5 text-xs" style={{ width: 16, textAlign: 'center' }}>
                {LOG_ICONS[entry.logType] || '\u2022'}
              </span>

              {/* Message */}
              <span
                className="flex-1 truncate text-xs"
                style={{ color: 'var(--text-primary)' }}
                dangerouslySetInnerHTML={{ __html: richTextToHtml(entry.message) }}
              />

              {/* Duration on COMPLETE entries */}
              {groupDuration !== null && (
                <span className="ml-2 text-[10px] font-mono" style={{ color: 'var(--text-muted)' }}>
                  {formatActionDuration(groupDuration)}
                </span>
              )}

              {/* Hierarchy pin indicator */}
              {logHierFile && (
                <span
                  className="ml-1 text-[9px]"
                  style={{ color: isActiveHierarchy ? 'var(--accent-green)' : 'var(--text-muted)', opacity: isActiveHierarchy ? 1 : 0.3 }}
                  title={isActiveHierarchy ? `Hierarchy: ${logHierFile} (active)` : `Hierarchy: ${logHierFile}`}
                >
                  {'\uD83C\uDF33'}
                </span>
              )}

              {/* Timestamp */}
              <span className="ml-2 text-[10px] font-mono whitespace-nowrap" style={{ color: 'var(--text-muted)' }}>
                {entry.timestamp.toFixed(2)}s
              </span>
            </div>
          );
        })}
      </div>

      {/* Resize handle */}
      <div
        onMouseDown={handleDragStart}
        style={{ height: 4, cursor: 'ns-resize', background: 'var(--border-color)', borderTop: '1px solid var(--border-color)' }}
        className="shrink-0 hover:brightness-150"
      />

      {/* Stack trace panel — resizable */}
      <div style={{ background: 'var(--bg-secondary)', height: stackHeight }} className="overflow-y-auto shrink-0">
        <div className="px-2 py-1">
          {selectedEntry?.stackTrace ? (
            <pre className="text-[11px] font-mono whitespace-pre-wrap break-all" style={{ color: 'var(--text-secondary)', margin: 0 }}>
              {selectedEntry.stackTrace}
            </pre>
          ) : (
            <span className="text-[11px]" style={{ color: 'var(--text-muted)' }}>
              {selectedEntry ? 'No stack trace' : 'Select a log entry to view details'}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}

/** Convert Unity rich text color tags to HTML spans */
function richTextToHtml(text: string): string {
  return text
    .replace(/<color=#([A-Fa-f0-9]+)>/g, '<span style="color:#$1">')
    .replace(/<\/color>/g, '</span>')
    .replace(/<b>/g, '<strong>')
    .replace(/<\/b>/g, '</strong>')
    .replace(/<i>/g, '<em>')
    .replace(/<\/i>/g, '</em>')
    .replace(/</g, (m, offset, str) => {
      // Only escape < that aren't part of our converted tags
      if (str.substring(offset).match(/^<(span|\/span|strong|\/strong|em|\/em)/)) return m;
      return '&lt;';
    });
}
