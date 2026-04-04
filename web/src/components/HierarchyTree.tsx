import { useState, useCallback, useMemo, useEffect, useRef } from 'react';
import type { HierarchyNode } from '../lib/types';

interface HierarchyTreeProps {
  roots: HierarchyNode[];
  selectedPath: string | null;
  onSelectNode: (node: HierarchyNode) => void;
}

type SearchMode = 'name' | 'text';

/** Check if a node or any descendant matches the search filter */
function nodeMatchesFilter(node: HierarchyNode, query: string, mode: SearchMode): boolean {
  const q = query.toLowerCase();
  if (mode === 'name') {
    if (node.name.toLowerCase().includes(q)) return true;
  } else {
    if (node.text && node.text.toLowerCase().includes(q)) return true;
  }
  if (node.children) {
    for (const child of node.children) {
      if (nodeMatchesFilter(child, query, mode)) return true;
    }
  }
  return false;
}

/** Check if this specific node (not descendants) matches */
function nodeDirectMatch(node: HierarchyNode, query: string, mode: SearchMode): boolean {
  const q = query.toLowerCase();
  if (mode === 'name') return node.name.toLowerCase().includes(q);
  return !!(node.text && node.text.toLowerCase().includes(q));
}

/** Pick an icon character based on component annotations or text content. */
export function nodeIcon(node: HierarchyNode): { icon: string; color: string } {
  // Use text field first (lightweight snapshots may not have annotations)
  if (node.text) return { icon: 'T', color: '#CCC' };

  const ann = node.annotations;
  if (!ann || ann.length === 0) return { icon: '\u25A6', color: '#8899AA' }; // dotted box = plain GameObject

  for (const a of ann) {
    if (a.startsWith('[Camera'))   return { icon: '\uD83C\uDFA5', color: '#8BC5FF' }; // camera
    if (a.startsWith('[Light'))    return { icon: '\u2600', color: '#FFE066' };       // sun
    if (a.startsWith('[Canvas'))   return { icon: '\u25A3', color: '#80B3FF' };       // canvas
    if (a.startsWith('[Button'))   return { icon: '\u25CF', color: '#4DCC4D' };       // button
    if (a.startsWith('[Toggle'))   return { icon: '\u2611', color: '#4DCC4D' };       // toggle
    if (a.startsWith('[TMP]') || a.startsWith('[Text]'))
                                   return { icon: 'T', color: '#CCC' };              // text
    if (a.startsWith('[Image'))    return { icon: '\u25A0', color: '#9999FF' };       // image
    if (a.startsWith('[RawImage')) return { icon: '\u25A0', color: '#9999FF' };       // raw image
    if (a.startsWith('[InputField'))return { icon: '\u2328', color: '#80B3FF' };      // input
    if (a.startsWith('[Slider'))   return { icon: '\u2501', color: '#FF9933' };       // slider
    if (a.startsWith('[Dropdown')) return { icon: '\u25BE', color: '#80B3FF' };       // dropdown
    if (a.startsWith('[ScrollRect'))return { icon: '\u21F3', color: '#80B3FF' };      // scroll
    if (a.startsWith('[MeshRenderer') || a.startsWith('[SkinnedMesh'))
                                   return { icon: '\u25B2', color: '#CC80FF' };       // mesh
    if (a.startsWith('[Animator')) return { icon: '\u266B', color: '#FF80CC' };       // anim
    if (a.startsWith('[AudioSource'))return { icon: '\u266A', color: '#FF80CC' };     // audio
    if (a.startsWith('[ParticleSystem'))return { icon: '\u2728', color: '#FFE066' };  // particles
  }
  return { icon: '\u25A0', color: 'var(--accent-blue)' }; // has components but no special icon
}

/** Extract a short label from text field or annotations. */
function annotationLabel(node: HierarchyNode): string | null {
  // Prefer the text field (lightweight snapshots)
  if (node.text) {
    return node.text.length > 24 ? node.text.slice(0, 24) + '...' : node.text;
  }

  const ann = node.annotations;
  if (!ann) return null;
  for (const a of ann) {
    // Text content: [TMP] "Hello World" or [Text] "Hello World"
    const textMatch = a.match(/\[(?:TMP|Text)\]\s*"(.+?)"/);
    if (textMatch) return textMatch[1].length > 20 ? textMatch[1].slice(0, 20) + '...' : textMatch[1];
    // Dropdown value: [Dropdown:Graphics]
    const ddMatch = a.match(/\[Dropdown:(.+?)\]/);
    if (ddMatch) return ddMatch[1];
  }
  return null;
}

/** Check if selectedPath starts with this node's path (meaning this node is an ancestor). */
function isAncestorOfSelected(nodePath: string, selectedPath: string | null): boolean {
  if (!selectedPath || !nodePath) return false;
  return selectedPath.startsWith(nodePath + '/');
}

export function HierarchyTree({ roots, selectedPath, onSelectNode }: HierarchyTreeProps) {
  const [search, setSearch] = useState('');
  const [searchMode, setSearchMode] = useState<SearchMode>('name');
  const scrollRef = useRef<HTMLDivElement>(null);

  // Filter roots when searching
  const filteredRoots = useMemo(() => {
    if (!search.trim()) return roots;
    return roots.filter(node => nodeMatchesFilter(node, search.trim(), searchMode));
  }, [roots, search, searchMode]);

  // Scroll selected node into view when selectedPath changes
  useEffect(() => {
    if (!selectedPath || !scrollRef.current) return;
    // Small delay to let React render the expanded tree first
    const timer = setTimeout(() => {
      const el = scrollRef.current?.querySelector(`[data-path="${CSS.escape(selectedPath)}"]`);
      if (el) el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }, 50);
    return () => clearTimeout(timer);
  }, [selectedPath]);

  if (roots.length === 0) {
    return (
      <div className="flex flex-col flex-1 min-h-0">
        <div className="p-4 text-center text-xs" style={{ color: 'var(--text-muted)' }}>
          No hierarchy data
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col flex-1 min-h-0">
      {/* Search bar */}
      <div className="flex items-center gap-1 px-1.5 py-1 shrink-0" style={{ borderBottom: '1px solid var(--border-color)', background: 'var(--bg-secondary)' }}>
        <select
          value={searchMode}
          onChange={e => setSearchMode(e.target.value as SearchMode)}
          className="text-[11px] rounded border outline-none px-0.5 py-0.5"
          style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-secondary)', width: 52 }}
        >
          <option value="name">Name</option>
          <option value="text">Text</option>
        </select>
        <input
          type="text"
          placeholder={searchMode === 'name' ? 'Filter by name...' : 'Filter by text...'}
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="flex-1 px-1.5 py-0.5 rounded text-[11px] border outline-none min-w-0"
          style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
        />
        {search && (
          <button
            onClick={() => setSearch('')}
            className="text-xs px-1"
            style={{ color: 'var(--text-muted)' }}
          >
            {'\u2715'}
          </button>
        )}
      </div>

      {/* Tree */}
      <div ref={scrollRef} className="overflow-y-auto overflow-x-hidden flex-1">
        {filteredRoots.map((node, i) => (
          node.isScene ? (
            <SceneNode
              key={i}
              node={node}
              selectedPath={selectedPath}
              onSelectNode={onSelectNode}
              searchQuery={search.trim()}
              searchMode={searchMode}
            />
          ) : (
            <TreeNode
              key={i}
              node={node}
              depth={0}
              selectedPath={selectedPath}
              onSelectNode={onSelectNode}
              searchQuery={search.trim()}
              searchMode={searchMode}
            />
          )
        ))}
        {search.trim() && filteredRoots.length === 0 && (
          <div className="p-3 text-center text-[11px]" style={{ color: 'var(--text-muted)' }}>
            No matches
          </div>
        )}
      </div>
    </div>
  );
}

/** Scene header node — dark bar like Unity's Hierarchy scene headers */
function SceneNode({ node, selectedPath, onSelectNode, searchQuery, searchMode }: {
  node: HierarchyNode;
  selectedPath: string | null;
  onSelectNode: (node: HierarchyNode) => void;
  searchQuery: string;
  searchMode: SearchMode;
}) {
  const [expanded, setExpanded] = useState(true);
  const hasChildren = node.children && node.children.length > 0;

  // Force expand if selected node is a descendant
  const forceExpand = isAncestorOfSelected(node.path, selectedPath);

  // When searching, filter children
  const visibleChildren = useMemo(() => {
    if (!searchQuery || !hasChildren) return node.children;
    return node.children.filter(child => nodeMatchesFilter(child, searchQuery, searchMode));
  }, [node.children, searchQuery, searchMode, hasChildren]);

  if (searchQuery && visibleChildren.length === 0) return null;

  const effectiveExpanded = expanded || forceExpand;

  return (
    <div>
      <div
        onClick={() => setExpanded(prev => !prev)}
        className="flex items-center cursor-pointer select-none"
        style={{
          height: 24,
          paddingLeft: 4,
          background: 'var(--bg-tertiary)',
          borderBottom: '1px solid var(--border-color)',
          borderTop: '1px solid var(--border-color)',
        }}
      >
        {hasChildren && (
          <span style={{ width: 14, fontSize: 9, color: 'var(--text-secondary)', textAlign: 'center', flexShrink: 0 }}>
            {effectiveExpanded ? '\u25BC' : '\u25B6'}
          </span>
        )}
        <span className="text-xs font-semibold ml-0.5" style={{ color: 'var(--text-secondary)' }}>
          {node.name}
        </span>
        {!effectiveExpanded && hasChildren && (
          <span className="ml-auto pr-2 text-[10px]" style={{ color: 'var(--text-muted)' }}>
            {visibleChildren.length}
          </span>
        )}
      </div>
      {effectiveExpanded && visibleChildren.map((child, i) => (
        <TreeNode
          key={i}
          node={child}
          depth={0}
          selectedPath={selectedPath}
          onSelectNode={onSelectNode}
          searchQuery={searchQuery}
          searchMode={searchMode}
        />
      ))}
    </div>
  );
}

function TreeNode({ node, depth, selectedPath, onSelectNode, searchQuery, searchMode }: {
  node: HierarchyNode;
  depth: number;
  selectedPath: string | null;
  onSelectNode: (node: HierarchyNode) => void;
  searchQuery: string;
  searchMode: SearchMode;
}) {
  // Auto-expand when searching, default collapse depth > 1 otherwise
  const [expanded, setExpanded] = useState(searchQuery ? true : depth < 1);
  const hasChildren = node.children && node.children.length > 0;
  const isSelected = node.path === selectedPath;
  const { icon, color: iconColor } = nodeIcon(node);
  const label = annotationLabel(node);
  const isDirectMatch = searchQuery ? nodeDirectMatch(node, searchQuery, searchMode) : false;

  // Force expand if selected node is a descendant of this node
  const forceExpand = hasChildren && isAncestorOfSelected(node.path, selectedPath);

  // When searching, filter children to only show matching branches
  const visibleChildren = useMemo(() => {
    if (!searchQuery || !hasChildren) return node.children;
    return node.children.filter(child => nodeMatchesFilter(child, searchQuery, searchMode));
  }, [node.children, searchQuery, searchMode, hasChildren]);

  // Auto-expand when search changes
  const effectiveExpanded = searchQuery ? true : (expanded || forceExpand);

  const handleToggle = useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
    setExpanded(prev => !prev);
  }, []);

  const handleSelect = useCallback(() => {
    onSelectNode(node);
  }, [node, onSelectNode]);

  return (
    <div>
      <div
        data-path={node.path}
        onClick={handleSelect}
        className="flex items-center cursor-pointer"
        style={{
          height: 22,
          paddingLeft: depth * 14 + 4,
          background: isSelected ? 'rgba(128, 179, 255, 0.2)' : isDirectMatch ? 'rgba(255, 204, 51, 0.12)' : 'transparent',
          opacity: node.active ? 1 : 0.4,
          borderLeft: isSelected ? '2px solid var(--accent-blue)' : '2px solid transparent',
        }}
        onMouseEnter={e => { if (!isSelected) e.currentTarget.style.background = isDirectMatch ? 'rgba(255, 204, 51, 0.18)' : 'var(--bg-hover)'; }}
        onMouseLeave={e => { if (!isSelected) e.currentTarget.style.background = isDirectMatch ? 'rgba(255, 204, 51, 0.12)' : 'transparent'; }}
      >
        {/* Foldout arrow */}
        {hasChildren ? (
          <span
            onClick={handleToggle}
            className="cursor-pointer select-none"
            style={{ width: 14, fontSize: 9, color: 'var(--text-muted)', textAlign: 'center', flexShrink: 0 }}
          >
            {effectiveExpanded ? '\u25BC' : '\u25B6'}
          </span>
        ) : (
          <span style={{ width: 14, flexShrink: 0 }} />
        )}

        {/* Component-based icon */}
        <span style={{ width: 16, fontSize: 11, textAlign: 'center', color: node.active ? iconColor : 'var(--text-muted)', flexShrink: 0 }}>
          {icon}
        </span>

        {/* Name */}
        <span className="truncate text-xs" style={{
          color: node.active ? 'var(--text-primary)' : 'var(--text-muted)',
          fontWeight: depth === 0 ? 500 : 400,
        }}>
          {node.name}
        </span>

        {/* Annotation label (text content, dropdown value, etc.) */}
        {label && (
          <span className="truncate ml-1 text-[10px]" style={{ color: 'var(--text-muted)', fontStyle: 'italic', maxWidth: 80 }}>
            {label}
          </span>
        )}

        {/* Child count when collapsed */}
        {hasChildren && !effectiveExpanded && (
          <span className="ml-auto pr-1 text-[10px]" style={{ color: 'var(--text-muted)', flexShrink: 0 }}>
            {node.childCount || node.children.length}
          </span>
        )}
      </div>

      {/* Children */}
      {effectiveExpanded && hasChildren && visibleChildren.map((child, i) => (
        <TreeNode
          key={i}
          node={child}
          depth={depth + 1}
          selectedPath={selectedPath}
          onSelectNode={onSelectNode}
          searchQuery={searchQuery}
          searchMode={searchMode}
        />
      ))}
    </div>
  );
}
