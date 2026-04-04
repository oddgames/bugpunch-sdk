/** Server-side session record (from /api/sessions) */
export interface TestSession {
  id: string;
  testName: string;
  result: 'pass' | 'fail' | 'warn';
  scene?: string;
  startTime?: string;
  duration: number;
  project?: string;
  branch?: string;
  commit?: string;
  appVersion?: string;
  platform?: string;
  machineName?: string;
  screenWidth: number;
  screenHeight: number;
  eventCount: number;
  logCount: number;
  hasVideo: boolean;
  zipPath: string;
  zipSize: number;
  createdAt: string;
  failureMessage?: string;
  customMetadata?: string;
  runId?: string;
}

/** Paginated response */
export interface PaginatedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/** DiagSession from session.json inside the zip */
export interface DiagSession {
  testName: string;
  scene: string;
  screenWidth: number;
  screenHeight: number;
  startTime: string;
  videoFile?: string;
  videoDuration: number;
  videoStartOffset?: number;       // seconds between session start and recording start
  videoTimestampsFile?: string;    // filename of frame timestamp CSV sidecar
  metadata?: SessionMetadata;
  events: DiagEvent[];
  logs: LogEntry[];
}

export interface SessionMetadata {
  project?: string;
  branch?: string;
  commit?: string;
  appVersion?: string;
  platform?: string;
  machineName?: string;
  customKeys?: string[];
  customValues?: string[];
}

export interface DiagEvent {
  type: 'start' | 'success' | 'warn' | 'failure' | 'search_snapshot' | 'snapshot';
  label: string;
  timestamp: number;
  frame: number;
  screenshotFile?: string;
  hierarchyFile?: string;
  callerFile?: string;
  callerLine: number;
  callerMethod?: string;
}

export interface LogEntry {
  logType: number;
  message: string;
  stackTrace?: string;
  timestamp: number;
  frame: number;
}

export interface HierarchySnapshot {
  screenWidth: number;
  screenHeight: number;
  cameraMatrix?: number[];  // 4x4 view-projection matrix (row-major) for 3D→screen projection
  roots: HierarchyNode[];
}

export interface HierarchyNode {
  name: string;
  path: string;
  text?: string;        // text content from TMP_Text or legacy Text
  active: boolean;
  isScene?: boolean;
  instanceId?: number;  // GameObject.GetInstanceID() for unique identification
  x: number;
  y: number;
  w: number;
  h: number;
  depth?: number;       // distance from camera (lower = closer/frontmost)
  worldBounds?: number[];  // [cx, cy, cz, ex, ey, ez] world-space AABB (3D objects only)
  annotations?: string[];
  childCount: number;
  siblingIndex: number;
  properties?: string[];
  children: HierarchyNode[];
}

/** Log type constants */
export const LogType = {
  Error: 0,
  Assert: 1,
  Warning: 2,
  Log: 3,
  Exception: 4,
  Screenshot: 5,
  Snapshot: 6,
  ActionStart: 7,
  ActionSuccess: 8,
  ActionWarn: 9,
  ActionFailure: 10,
} as const;

/** Action grouping for console display */
export interface ActionGroup {
  startIndex: number;
  endIndex: number;
  innerCount: number;
  duration: number;
  resultType: number; // logType of the end entry
}

/** Test run summary (grouped sessions) */
export interface RunSummary {
  runId: string;
  project?: string;
  branch?: string;
  commit?: string;
  appVersion?: string;
  platform?: string;
  machineName?: string;
  startedAt: string;
  finishedAt?: string;
  isComplete: boolean;
  totalTests: number;
  passed: number;
  failed: number;
  warned: number;
  totalDuration: number;
  result: 'pass' | 'fail' | 'warn' | 'running';
}

/** Event color scheme matching editor */
export const EventColors: Record<string, string> = {
  success: '#4DCC4D',
  failure: '#FF4D4D',
  warn: '#FF9933',
  start: '#80B3FF',
  search_snapshot: '#FFCC33',
  snapshot: '#9999FF',
  default: '#999999',
};

/** Row tint colors for console entries */
export const RowTints: Record<number, string> = {
  [LogType.Screenshot]: 'rgba(21, 64, 21, 0.3)',
  [LogType.Snapshot]: 'rgba(21, 21, 64, 0.3)',
  [LogType.ActionStart]: 'rgba(51, 64, 89, 0.25)',
  [LogType.ActionSuccess]: 'rgba(21, 77, 21, 0.25)',
  [LogType.ActionWarn]: 'rgba(89, 77, 26, 0.25)',
  [LogType.ActionFailure]: 'rgba(89, 26, 26, 0.25)',
};
