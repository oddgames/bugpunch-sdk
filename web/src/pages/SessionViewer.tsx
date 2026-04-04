import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { getSession, getSessionJson, getHierarchy, getVideoTimestamps, sessionFileUrl, sessionVideoUrl } from '../lib/api';
import type { TestSession, DiagSession, HierarchyNode, HierarchySnapshot } from '../lib/types';
import { VideoPanel } from '../components/VideoPanel';
import type { HighlightBounds } from '../components/VideoPanel';
import { TimelineBar } from '../components/TimelineBar';
import { ConsolePanel } from '../components/ConsolePanel';
import { HierarchyTree } from '../components/HierarchyTree';
import { nodeIcon } from '../components/HierarchyTree';
import { InspectorPanel } from '../components/InspectorPanel';

/** Recursively build paths from the tree structure (C# side doesn't set them).
 *  Uses instanceId to disambiguate same-named siblings. */
function buildPaths(nodes: HierarchyNode[], parentPath: string) {
  const nameCounts = new Map<string, number>();
  for (const node of nodes) {
    const count = (nameCounts.get(node.name) ?? 0) + 1;
    nameCounts.set(node.name, count);
  }
  const nameIndex = new Map<string, number>();
  for (const node of nodes) {
    const total = nameCounts.get(node.name) ?? 1;
    if (total > 1) {
      // Disambiguate with instanceId
      const idx = (nameIndex.get(node.name) ?? 0);
      nameIndex.set(node.name, idx + 1);
      const suffix = node.instanceId != null ? `[${node.instanceId}]` : `[${idx}]`;
      node.path = parentPath ? `${parentPath}/${node.name}${suffix}` : `${node.name}${suffix}`;
    } else {
      node.path = parentPath ? `${parentPath}/${node.name}` : node.name;
    }
    if (node.children?.length) buildPaths(node.children, node.path);
  }
}

/** Project a single world-space AABB to screen-space bounds using a VP matrix. */
function projectNodeBounds(
  wb: number[], m: number[], sw: number, sh: number
): { x: number; y: number; w: number; h: number; depth: number } | null {
  if (wb.length !== 6 || m.length !== 16) return null;
  const [cx, cy, cz, ex, ey, ez] = wb;
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
  let minZ = Infinity;
  let any = false;
  for (let ix = 0; ix < 2; ix++)
  for (let iy = 0; iy < 2; iy++)
  for (let iz = 0; iz < 2; iz++) {
    const wx = cx + (ix === 0 ? -ex : ex);
    const wy = cy + (iy === 0 ? -ey : ey);
    const wz = cz + (iz === 0 ? -ez : ez);
    const clipX = m[0]*wx + m[1]*wy + m[2]*wz + m[3];
    const clipY = m[4]*wx + m[5]*wy + m[6]*wz + m[7];
    const clipZ = m[8]*wx + m[9]*wy + m[10]*wz + m[11];
    const clipW = m[12]*wx + m[13]*wy + m[14]*wz + m[15];
    if (clipW <= 0) continue;
    const sx = (clipX / clipW + 1) * 0.5 * sw;
    const sy = (clipY / clipW + 1) * 0.5 * sh;
    const z = clipZ / clipW;
    any = true;
    if (sx < minX) minX = sx; if (sx > maxX) maxX = sx;
    if (sy < minY) minY = sy; if (sy > maxY) maxY = sy;
    if (z < minZ) minZ = z;
  }
  if (!any || maxX <= minX || maxY <= minY) return null;
  return { x: minX, y: minY, w: maxX - minX, h: maxY - minY, depth: minZ };
}

/** Flatten all active nodes that have bounds (screen-space or world-space) for hit-testing. */
function flattenHittableNodes(roots: HierarchyNode[]): HierarchyNode[] {
  const list: HierarchyNode[] = [];
  function walk(node: HierarchyNode) {
    if (node.active && !node.isScene && ((node.w > 0 && node.h > 0) || node.worldBounds)) list.push(node);
    if (node.children) for (const child of node.children) walk(child);
  }
  for (const root of roots) walk(root);
  return list;
}

/** Check if a Unity screen-space point is inside a node's bounds. */
function pointInNode(ux: number, uy: number, node: HierarchyNode): boolean {
  return ux >= node.x && ux <= node.x + node.w && uy >= node.y && uy <= node.y + node.h;
}

export function SessionViewer() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [meta, setMeta] = useState<TestSession | null>(null);
  const [session, setSession] = useState<DiagSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Viewer state
  const [currentTime, setCurrentTime] = useState(0);
  const [hierarchy, setHierarchy] = useState<HierarchySnapshot | null>(null);
  const [selectedNode, setSelectedNode] = useState<HierarchyNode | null>(null);
  const [screenshotUrl, setScreenshotUrl] = useState<string | null>(null);
  const [highlightedLogIndex, setHighlightedLogIndex] = useState<number | null>(null);
  const [seekCount, setSeekCount] = useState(0); // increments on each seek to trigger pause
  const loadedHierarchyFile = useRef<string | null>(null);

  // Video timestamp sidecar for accurate video-to-session sync
  const [videoTimestamps, setVideoTimestamps] = useState<Float32Array | null>(null);

  // Context menu state
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; nodes: HierarchyNode[] } | null>(null);

  // Load data
  useEffect(() => {
    if (!id) return;
    setLoading(true);
    loadedHierarchyFile.current = null;
    setVideoTimestamps(null);
    Promise.all([getSession(id), getSessionJson(id)])
      .then(([m, s]) => {
        setMeta(m);
        setSession(s);
        // Load video timestamp sidecar if available
        if (s.videoTimestampsFile) {
          getVideoTimestamps(id, s.videoTimestampsFile).then(setVideoTimestamps);
        }
      })
      .catch(e => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  // Sync hierarchy to current time — show the most recent snapshot at or before currentTime
  useEffect(() => {
    if (!id || !session?.events.length) return;

    // Find the most recent event with a hierarchyFile at or before currentTime
    let bestFile: string | null = null;
    for (const evt of session.events) {
      if (evt.timestamp <= currentTime && evt.hierarchyFile) {
        bestFile = evt.hierarchyFile;
      }
    }

    if (bestFile === loadedHierarchyFile.current) return; // already loaded
    loadedHierarchyFile.current = bestFile;

    if (bestFile) {
      getHierarchy(id, bestFile)
        .then(h => { buildPaths(h.roots, ''); setHierarchy(h); })
        .catch(() => setHierarchy(null));
    } else {
      setHierarchy(null);
    }
  }, [id, currentTime, session?.events]);

  // Find nearest log entry to current time
  useEffect(() => {
    if (!session?.logs.length) return;
    let nearest = 0;
    let minDiff = Infinity;
    for (let i = 0; i < session.logs.length; i++) {
      const diff = Math.abs(session.logs[i].timestamp - currentTime);
      if (diff < minDiff) {
        minDiff = diff;
        nearest = i;
      }
    }
    setHighlightedLogIndex(nearest);
  }, [currentTime, session?.logs]);

  const handleSeek = useCallback((time: number) => {
    setCurrentTime(time);
    setSeekCount(c => c + 1); // trigger pause

    // If the event at this timestamp has a screenshotFile, show it automatically
    // (e.g. failure events where the video may already show teardown state)
    if (session?.events.length) {
      let bestScreenshot: string | null = null;
      for (const evt of session.events) {
        if (Math.abs(evt.timestamp - time) < 0.05 && evt.screenshotFile) {
          bestScreenshot = evt.screenshotFile;
        }
      }
      if (bestScreenshot && id) {
        setScreenshotUrl(sessionFileUrl(id, bestScreenshot));
        return;
      }
    }
    setScreenshotUrl(null);
  }, [session?.events, id]);

  const handleScreenshotClick = useCallback((fileName: string) => {
    if (!id) return;
    setScreenshotUrl(sessionFileUrl(id, fileName));
  }, [id]);

  const handleSnapshotClick = useCallback((fileName: string) => {
    if (!id) return;
    getHierarchy(id, fileName)
      .then(h => { buildPaths(h.roots, ''); setHierarchy(h); })
      .catch(() => {});
  }, [id]);

  const handleNodeSelect = useCallback((node: HierarchyNode) => {
    setSelectedNode(node);
    setContextMenu(null);
  }, []);

  // Compute highlight bounds for selected node overlay on video
  // For 3D objects with worldBounds, project on-demand using camera matrix
  const highlightBounds: HighlightBounds | null = useMemo(() => {
    if (!selectedNode || !hierarchy) return null;
    // 3D object: project worldBounds → screen on demand
    if (selectedNode.worldBounds && hierarchy.cameraMatrix) {
      const proj = projectNodeBounds(selectedNode.worldBounds, hierarchy.cameraMatrix, hierarchy.screenWidth, hierarchy.screenHeight);
      if (!proj) return null;
      return { x: proj.x, y: proj.y, w: proj.w, h: proj.h, screenWidth: hierarchy.screenWidth, screenHeight: hierarchy.screenHeight };
    }
    // UI element: already has screen-space bounds
    if (selectedNode.w <= 0 || selectedNode.h <= 0) return null;
    return {
      x: selectedNode.x,
      y: selectedNode.y,
      w: selectedNode.w,
      h: selectedNode.h,
      screenWidth: hierarchy.screenWidth,
      screenHeight: hierarchy.screenHeight,
    };
  }, [selectedNode, hierarchy]);

  // Screen size for coordinate transforms
  const screenSize = useMemo(() => {
    if (!hierarchy) return null;
    return { w: hierarchy.screenWidth, h: hierarchy.screenHeight };
  }, [hierarchy]);

  // Flat list of hittable nodes (UI with screen bounds + 3D with worldBounds)
  const hittableNodes = useMemo(() => {
    if (!hierarchy?.roots) return [];
    return flattenHittableNodes(hierarchy.roots);
  }, [hierarchy]);

  // Hit-test a point against a node, projecting 3D bounds on demand
  const hitTestNode = useCallback((ux: number, uy: number, node: HierarchyNode): { hit: boolean; depth: number } => {
    if (node.worldBounds && hierarchy?.cameraMatrix) {
      const proj = projectNodeBounds(node.worldBounds, hierarchy.cameraMatrix, hierarchy.screenWidth, hierarchy.screenHeight);
      if (!proj) return { hit: false, depth: Infinity };
      const hit = ux >= proj.x && ux <= proj.x + proj.w && uy >= proj.y && uy <= proj.y + proj.h;
      return { hit, depth: proj.depth };
    }
    return { hit: pointInNode(ux, uy, node), depth: node.depth ?? Infinity };
  }, [hierarchy]);

  // Left-click on video: select frontmost (lowest depth) node at point
  const handleVideoClick = useCallback((ux: number, uy: number) => {
    setContextMenu(null);
    let best: HierarchyNode | null = null;
    let bestDepth = Infinity;
    for (const node of hittableNodes) {
      const { hit, depth } = hitTestNode(ux, uy, node);
      if (hit && depth < bestDepth) { best = node; bestDepth = depth; }
    }
    if (best) setSelectedNode(best);
  }, [hittableNodes, hitTestNode]);

  // Right-click on video: show context menu with all nodes at point
  const handleVideoContextMenu = useCallback((ux: number, uy: number, clientX: number, clientY: number) => {
    const hits: { node: HierarchyNode; depth: number }[] = [];
    for (const node of hittableNodes) {
      const { hit, depth } = hitTestNode(ux, uy, node);
      if (hit) hits.push({ node, depth });
    }
    hits.sort((a, b) => a.depth - b.depth);
    if (hits.length > 0) {
      setContextMenu({ x: clientX, y: clientY, nodes: hits.map(h => h.node) });
    } else {
      setContextMenu(null);
    }
  }, [hittableNodes, hitTestNode]);

  // Close context menu on click outside or Escape
  useEffect(() => {
    if (!contextMenu) return;
    const handleClick = () => setContextMenu(null);
    const handleKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setContextMenu(null); };
    window.addEventListener('click', handleClick);
    window.addEventListener('keydown', handleKey);
    return () => {
      window.removeEventListener('click', handleClick);
      window.removeEventListener('keydown', handleKey);
    };
  }, [contextMenu]);

  // Find current source location from events
  const currentSource = useMemo(() => {
    if (!session?.events.length) return null;
    let best: typeof session.events[0] | null = null;
    for (const evt of session.events) {
      if (evt.timestamp <= currentTime && evt.callerFile) {
        best = evt;
      }
    }
    return best;
  }, [session?.events, currentTime]);

  // Find nearest hierarchy snapshot event to current time
  const handleGoToNearestSnapshot = useCallback(() => {
    if (!session?.events.length) return;
    let nearest: typeof session.events[0] | null = null;
    let minDiff = Infinity;
    for (const evt of session.events) {
      if (!evt.hierarchyFile) continue;
      const diff = Math.abs(evt.timestamp - currentTime);
      if (diff < minDiff) {
        minDiff = diff;
        nearest = evt;
      }
    }
    if (nearest) handleSeek(nearest.timestamp);
  }, [session?.events, currentTime, handleSeek]);

  // Navigate to previous/next snapshot relative to current time
  const handlePrevSnapshot = useCallback(() => {
    if (!session?.events.length) return;
    let best: typeof session.events[0] | null = null;
    for (const evt of session.events) {
      if (!evt.hierarchyFile) continue;
      if (evt.timestamp < currentTime - 0.01) best = evt;
    }
    if (best) handleSeek(best.timestamp);
  }, [session?.events, currentTime, handleSeek]);

  const handleNextSnapshot = useCallback(() => {
    if (!session?.events.length) return;
    for (const evt of session.events) {
      if (!evt.hierarchyFile) continue;
      if (evt.timestamp > currentTime + 0.01) {
        handleSeek(evt.timestamp);
        return;
      }
    }
  }, [session?.events, currentTime, handleSeek]);

  const hasAnySnapshot = useMemo(
    () => session?.events.some(e => e.hierarchyFile) ?? false,
    [session?.events]
  );

  const videoUrl = id && meta?.hasVideo ? sessionVideoUrl(id) : null;

  // Timeline duration = max of video duration, last log timestamp, last event timestamp
  const timelineDuration = useMemo(() => {
    let d = session?.videoDuration || 0;
    if (session?.logs.length) {
      const lastLog = session.logs[session.logs.length - 1].timestamp;
      if (lastLog > d) d = lastLog;
    }
    if (session?.events.length) {
      const lastEvt = session.events[session.events.length - 1].timestamp;
      if (lastEvt > d) d = lastEvt;
    }
    return d;
  }, [session]);

  const resultColor = meta?.result === 'pass' ? 'var(--accent-green)' : meta?.result === 'fail' ? 'var(--accent-red)' : 'var(--accent-orange)';

  if (loading) {
    return <div className="flex items-center justify-center h-screen" style={{ color: 'var(--text-muted)' }}>Loading session...</div>;
  }

  if (error) {
    return <div className="flex items-center justify-center h-screen" style={{ color: 'var(--accent-red)' }}>Error: {error}</div>;
  }

  if (!session || !meta) return null;

  return (
    <div className="flex flex-col h-screen" style={{ background: 'var(--bg-primary)' }}>
      {/* Header */}
      <div className="flex items-center gap-3 px-4 py-2 shrink-0" style={{ background: 'var(--bg-secondary)', borderBottom: '1px solid var(--border-color)' }}>
        <button
          onClick={() => navigate('/')}
          className="text-xs opacity-60 hover:opacity-100"
          style={{ color: 'var(--text-secondary)' }}
        >
          &larr; Back
        </button>
        <span
          className="px-2 py-0.5 rounded text-xs font-bold border"
          style={{ borderColor: resultColor, color: resultColor }}
        >
          {meta.result.toUpperCase()}
        </span>
        <span className="font-medium font-mono" style={{ color: 'var(--text-primary)' }}>
          {meta.testName}
        </span>
        <span className="text-xs" style={{ color: 'var(--text-muted)' }}>
          {meta.project && `${meta.project}`}
          {meta.branch && ` / ${meta.branch}`}
          {meta.commit && ` @ ${meta.commit}`}
        </span>
        {meta.appVersion && (
          <span className="text-xs font-mono px-1.5 py-0.5 rounded" style={{ color: 'var(--text-secondary)', background: 'var(--bg-tertiary)' }}>
            v{meta.appVersion}
          </span>
        )}
        <span className="ml-auto text-xs" style={{ color: 'var(--text-muted)' }}>
          {meta.startTime} &middot; {meta.screenWidth}x{meta.screenHeight}
        </span>
      </div>

      {/* 3-panel layout */}
      <div className="flex flex-1 min-h-0">
        {/* Left: Hierarchy */}
        <div className="flex flex-col shrink-0" style={{ width: 260, borderRight: '1px solid var(--border-color)', background: 'var(--bg-secondary)' }}>
          <div className="flex items-center justify-between px-3 py-1" style={{ borderBottom: '1px solid var(--border-color)' }}>
            <span className="text-xs font-medium" style={{ color: 'var(--text-muted)' }}>Hierarchy</span>
            {hasAnySnapshot && (
              <div className="flex items-center gap-0.5">
                <button
                  onClick={handlePrevSnapshot}
                  className="px-1 opacity-50 hover:opacity-100"
                  style={{ color: 'var(--text-secondary)', fontSize: 10 }}
                  title="Previous snapshot"
                >&lsaquo;</button>
                <button
                  onClick={handleGoToNearestSnapshot}
                  className="px-1.5 py-0.5 rounded opacity-60 hover:opacity-100"
                  style={{ color: 'var(--accent-blue)', fontSize: 10, background: 'var(--bg-tertiary)' }}
                  title="Go to nearest hierarchy snapshot"
                >Nearest</button>
                <button
                  onClick={handleNextSnapshot}
                  className="px-1 opacity-50 hover:opacity-100"
                  style={{ color: 'var(--text-secondary)', fontSize: 10 }}
                  title="Next snapshot"
                >&rsaquo;</button>
              </div>
            )}
          </div>
          <HierarchyTree
            roots={hierarchy?.roots || []}
            selectedPath={selectedNode?.path || null}
            onSelectNode={handleNodeSelect}
          />
        </div>

        {/* Center: Video + Timeline + Console */}
        <div className="flex flex-col flex-1 min-w-0 min-h-0">
          {/* Source location */}
          {currentSource?.callerFile && (
            <div className="px-3 py-0.5 text-xs" style={{ background: 'var(--bg-tertiary)', borderBottom: '1px solid var(--border-color)' }}>
              <span style={{ color: 'var(--text-muted)' }}>Source: </span>
              <span style={{ color: 'var(--accent-blue)' }}>
                {currentSource.callerFile.split(/[/\\]/).pop()}:{currentSource.callerLine}
              </span>
              <span style={{ color: 'var(--text-muted)' }}> ({currentSource.callerMethod})</span>
            </div>
          )}

          {/* Video */}
          <VideoPanel
            videoUrl={videoUrl}
            screenshotUrl={screenshotUrl}
            duration={timelineDuration}
            currentTime={currentTime}
            onTimeChange={setCurrentTime}
            paused={seekCount > 0 ? seekCount : undefined}
            highlightBounds={highlightBounds}
            screenSize={screenSize}
            onVideoClick={handleVideoClick}
            onVideoContextMenu={handleVideoContextMenu}
            videoStartOffset={session?.videoStartOffset ?? 0}
            videoTimestamps={videoTimestamps}
          />

          {/* Timeline */}
          <TimelineBar
            events={session.events}
            duration={timelineDuration}
            currentTime={currentTime}
            onSeek={handleSeek}
          />

          {/* Console */}
          <ConsolePanel
            logs={session.logs}
            events={session.events}
            currentTime={currentTime}
            onSeek={handleSeek}
            onScreenshotClick={handleScreenshotClick}
            onSnapshotClick={handleSnapshotClick}
            highlightedIndex={highlightedLogIndex}
            activeHierarchyFile={loadedHierarchyFile.current}
          />
        </div>

        {/* Right: Inspector (shown when node selected) */}
        {selectedNode && (
          <div className="flex flex-col shrink-0" style={{ width: 240, borderLeft: '1px solid var(--border-color)', background: 'var(--bg-secondary)' }}>
            <div className="flex items-center justify-between px-3 py-1" style={{ borderBottom: '1px solid var(--border-color)' }}>
              <span className="text-xs font-medium" style={{ color: 'var(--text-muted)' }}>Inspector</span>
              <button
                onClick={() => setSelectedNode(null)}
                className="text-xs opacity-50 hover:opacity-100"
                style={{ color: 'var(--text-secondary)' }}
              >
                x
              </button>
            </div>
            <InspectorPanel node={selectedNode} />
          </div>
        )}
      </div>

      {/* Context menu for right-click on video */}
      {contextMenu && (
        <div
          onClick={e => e.stopPropagation()}
          style={{
            position: 'fixed',
            left: contextMenu.x,
            top: contextMenu.y,
            zIndex: 1000,
            background: 'var(--bg-secondary)',
            border: '1px solid var(--border-color)',
            borderRadius: 4,
            boxShadow: '0 4px 12px rgba(0,0,0,0.4)',
            minWidth: 180,
            maxHeight: 300,
            overflowY: 'auto',
            padding: '4px 0',
          }}
        >
          <div className="px-3 py-1 text-[10px]" style={{ color: 'var(--text-muted)', borderBottom: '1px solid var(--border-color)' }}>
            {contextMenu.nodes.length} object{contextMenu.nodes.length !== 1 ? 's' : ''} at point
          </div>
          {contextMenu.nodes.map((node, i) => {
            const { icon, color } = nodeIcon(node);
            return (
              <div
                key={node.instanceId ?? i}
                onClick={() => { setSelectedNode(node); setContextMenu(null); }}
                className="flex items-center gap-2 px-3 py-1 cursor-pointer"
                style={{ color: 'var(--text-primary)' }}
                onMouseEnter={e => { e.currentTarget.style.background = 'var(--bg-hover)'; }}
                onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; }}
              >
                <span style={{ width: 14, fontSize: 11, textAlign: 'center', color, flexShrink: 0 }}>{icon}</span>
                <span className="text-xs truncate">{node.name}</span>
                {node.depth != null && (
                  <span className="ml-auto text-[10px] font-mono" style={{ color: 'var(--text-muted)', flexShrink: 0 }}>
                    z:{node.depth.toFixed(1)}
                  </span>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
