import type { LogEntry, ActionGroup } from './types';
import { LogType } from './types';

/**
 * Builds action groups by pairing ActionStart with ActionSuccess/Warn/Failure entries.
 * Supports nested actions via stack-based pairing.
 */
export function buildActionGroups(logs: LogEntry[]): Map<number, ActionGroup> {
  const groups = new Map<number, ActionGroup>();
  const stack: number[] = [];

  for (let i = 0; i < logs.length; i++) {
    const entry = logs[i];
    if (entry.logType === LogType.ActionStart) {
      stack.push(i);
    } else if (
      entry.logType === LogType.ActionSuccess ||
      entry.logType === LogType.ActionWarn ||
      entry.logType === LogType.ActionFailure
    ) {
      if (stack.length > 0) {
        const startIdx = stack.pop()!;
        let innerCount = 0;
        for (let j = startIdx + 1; j < i; j++) {
          if (!groups.has(j)) innerCount++;
        }
        groups.set(startIdx, {
          startIndex: startIdx,
          endIndex: i,
          innerCount,
          duration: entry.timestamp - logs[startIdx].timestamp,
          resultType: entry.logType,
        });
      }
    }
  }

  return groups;
}

/** Strips Unity rich text tags from a string */
export function stripRichText(text: string): string {
  return text.replace(/<\/?[^>]+>/g, '');
}

/** Formats duration for display */
export function formatActionDuration(seconds: number): string {
  if (seconds < 0.001) return '<1ms';
  if (seconds < 1) return `${Math.round(seconds * 1000)}ms`;
  return `${seconds.toFixed(1)}s`;
}
