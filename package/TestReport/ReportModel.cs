using System;
using System.Collections.Generic;

namespace ODDGames.Bugpunch
{
    // Data classes for TestReport. Pure DTOs — no logic.
    // Lifted out of TestReport for separation, but kept internal to preserve
    // the original visibility semantics (TestReport is internal static).

    [Serializable]
    internal class SessionMetadata
    {
        public string runId;         // server-issued run ID for grouping test batches
        public string project;       // e.g. "MonsterTruckDestruction"
        public string branch;        // e.g. "main", "feature/new-ui"
        public string commit;        // short hash
        public string appVersion;    // Application.version
        public string platform;      // RuntimePlatform string
        public string machineName;   // Environment.MachineName
        public List<string> customKeys = new();   // parallel lists (JsonUtility doesn't support Dictionary)
        public List<string> customValues = new();
    }

    [Serializable]
    internal class DiagSession
    {
        public string testName;
        public string result;          // "pass", "fail", or "warn" — set from NUnit TestContext
        public string scene;
        public int screenWidth;
        public int screenHeight;
        public string startTime;
        public string videoFile;      // relative path to recording MP4
        public float videoDuration;   // video duration in seconds
        public float videoStartOffset; // seconds between session start and recording start
        public string videoTimestampsFile; // relative path to frame timestamp CSV sidecar
        public SessionMetadata metadata;
        public List<DiagEvent> events = new();
        public List<LogEntry> logs = new();
    }

    [Serializable]
    internal class LogEntry
    {
        public int logType;     // Unity LogType: 0=Error, 1=Assert, 2=Warning, 3=Log, 4=Exception
                                // Custom: 5=Screenshot, 6=Snapshot, 7=ActionStart, 8=ActionSuccess, 9=ActionWarn, 10=ActionFailure
        public string message;
        public string stackTrace; // for screenshots/snapshots: contains the filename
        public float timestamp;
        public int frame;
    }

    [Serializable]
    internal class DiagEvent
    {
        public string type; // "start", "success", "warn", "failure", "search_snapshot"
        public string label;
        public float timestamp;
        public int frame;
        public string screenshotFile; // null if no screenshot
        public string hierarchyFile; // null if no hierarchy snapshot
        public string callerFile;   // source file path of test code
        public int callerLine;      // line number in test code
        public string callerMethod; // method name in test code
    }

    [Serializable]
    internal class HierarchySnapshot
    {
        public int screenWidth;
        public int screenHeight;
        public float[] cameraMatrix;    // 4x4 view-projection matrix (column-major) for 3D→screen projection
        public List<HierarchyNode> roots = new(); // nested tree — matches web viewer format
    }

    [Serializable]
    internal class HierarchyNode
    {
        public string name;
        public string path;
        public string text;             // text content from TMP_Text or legacy Text (null if no text)
        public bool active;
        public bool isScene;            // true for scene header nodes (top-level grouping)
        public int instanceId;          // GameObject.GetInstanceID() for unique identification
        public float x, y, w, h;       // screen-space bounds (for UI elements in screen space)
        public float depth;             // rendering order (lower = closer/frontmost for 3D, negative for UI)
        public float[] worldBounds;     // [cx, cy, cz, ex, ey, ez] — world-space AABB center+extents (3D objects only)
        public string[] annotations;
        public int childCount;          // total direct children
        public int siblingIndex;        // sibling index in parent — for Unity Hierarchy ordering
        public List<string> properties;  // detailed component properties (null when not detailed)
        public List<HierarchyNode> children = new();
    }
}
