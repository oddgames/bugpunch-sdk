import { useMemo } from 'react';
import type { HierarchyNode } from '../lib/types';

interface InspectorPanelProps {
  node: HierarchyNode | null;
}

interface ComponentData {
  typeName: string;
  props: Record<string, unknown>;
}

function parseProperties(properties: string[]): ComponentData[] {
  const components: ComponentData[] = [];
  for (const prop of properties) {
    // Format: "ComponentType: { json }"
    const colonIdx = prop.indexOf(':');
    if (colonIdx <= 0) continue;
    const typeName = prop.substring(0, colonIdx).trim();
    const jsonStr = prop.substring(colonIdx + 1).trim();
    try {
      const parsed = JSON.parse(jsonStr);
      components.push({ typeName, props: parsed });
    } catch {
      components.push({ typeName, props: {} });
    }
  }
  return components;
}

function formatValue(val: unknown): string {
  if (val === null || val === undefined) return 'null';
  if (typeof val === 'boolean') return val ? 'true' : 'false';
  if (typeof val === 'number') return Number.isInteger(val) ? String(val) : val.toFixed(3);
  if (typeof val === 'string') return val;
  if (typeof val === 'object') {
    const obj = val as Record<string, unknown>;
    const keys = Object.keys(obj);
    if (keys.length <= 4 && keys.every(k => typeof obj[k] === 'number')) {
      return `(${keys.map(k => (obj[k] as number).toFixed(2)).join(', ')})`;
    }
    return JSON.stringify(val);
  }
  return String(val);
}

function valueColor(val: unknown): string {
  if (typeof val === 'boolean') return val ? 'var(--accent-green)' : 'var(--accent-red)';
  if (typeof val === 'number') return '#80B3FF';
  if (typeof val === 'string') return '#CE9178';
  return 'var(--text-primary)';
}

/** Color for annotation type badges. */
function annotationColor(ann: string): string {
  if (ann.startsWith('[Button')) return '#4DCC4D';
  if (ann.startsWith('[Toggle')) return '#4DCC4D';
  if (ann.startsWith('[TMP]') || ann.startsWith('[Text]')) return '#CCC';
  if (ann.startsWith('[Image') || ann.startsWith('[RawImage')) return '#9999FF';
  if (ann.startsWith('[Canvas')) return '#80B3FF';
  if (ann.startsWith('[Camera')) return '#8BC5FF';
  if (ann.startsWith('[Light')) return '#FFE066';
  if (ann.includes('disabled')) return 'var(--accent-red)';
  return 'var(--text-secondary)';
}

export function InspectorPanel({ node }: InspectorPanelProps) {
  const components = useMemo(
    () => (node?.properties ? parseProperties(node.properties) : []),
    [node?.properties]
  );

  if (!node) {
    return (
      <div className="p-4 text-center text-xs" style={{ color: 'var(--text-muted)' }}>
        Select a node to inspect
      </div>
    );
  }

  return (
    <div className="overflow-y-auto flex-1 text-xs">
      {/* Header */}
      <div className="px-3 py-2" style={{ background: 'var(--bg-tertiary)', borderBottom: '1px solid var(--border-color)' }}>
        <div className="flex items-center gap-2">
          <span className="font-medium" style={{ color: 'var(--text-primary)' }}>{node.name}</span>
          <span
            className="ml-auto text-[10px] px-1.5 py-0.5 rounded"
            style={{
              color: node.active ? 'var(--accent-green)' : 'var(--accent-red)',
              background: node.active ? 'rgba(77, 204, 77, 0.1)' : 'rgba(255, 77, 77, 0.1)',
            }}
          >
            {node.active ? 'Active' : 'Inactive'}
          </span>
        </div>
        <div className="font-mono text-[10px] mt-0.5" style={{ color: 'var(--text-muted)', wordBreak: 'break-all' }}>
          {node.path}
        </div>
        {(node.instanceId != null || node.depth != null) && (
          <div className="flex gap-3 mt-0.5 text-[10px] font-mono" style={{ color: 'var(--text-muted)' }}>
            {node.instanceId != null && <span>ID: {node.instanceId}</span>}
            {node.depth != null && <span>Depth: {node.depth.toFixed(2)}</span>}
          </div>
        )}
      </div>

      {/* Text content */}
      {node.text && (
        <Section title="Text">
          <div className="px-3 py-1 font-mono text-[11px] select-all" style={{ color: '#CE9178', wordBreak: 'break-all' }}>
            {node.text}
          </div>
        </Section>
      )}

      {/* Transform / Position */}
      {(node.w > 0 || node.h > 0) && (
        <Section title="Transform">
          <PropRow label="Position" value={`${node.x.toFixed(0)}, ${node.y.toFixed(0)}`} />
          <PropRow label="Size" value={`${node.w.toFixed(0)} x ${node.h.toFixed(0)}`} />
        </Section>
      )}

      {/* Components (annotations) */}
      {node.annotations && node.annotations.length > 0 && (
        <Section title={`Components (${node.annotations.length})`}>
          {node.annotations.map((ann, i) => (
            <div key={i} className="px-3 py-0.5 font-mono" style={{ color: annotationColor(ann) }}>
              {ann}
            </div>
          ))}
        </Section>
      )}

      {/* Children count */}
      {node.childCount > 0 && (
        <div className="px-3 py-1" style={{ borderBottom: '1px solid var(--border-color)' }}>
          <span style={{ color: 'var(--text-muted)' }}>Children: </span>
          <span style={{ color: 'var(--text-primary)' }}>{node.childCount}</span>
        </div>
      )}

      {/* Detailed component properties (from Snapshot captures) */}
      {components.map((comp, i) => (
        <Section key={i} title={comp.typeName}>
          {Object.entries(comp.props).map(([key, val]) => (
            <PropRow key={key} label={key} value={formatValue(val)} color={valueColor(val)} />
          ))}
          {Object.keys(comp.props).length === 0 && (
            <div className="px-3 py-0.5" style={{ color: 'var(--text-muted)' }}>(no properties)</div>
          )}
        </Section>
      ))}
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="px-3 py-1 font-medium" style={{ background: 'var(--bg-tertiary)', borderBottom: '1px solid var(--border-color)', color: 'var(--text-secondary)' }}>
        {title}
      </div>
      {children}
    </div>
  );
}

function PropRow({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div className="flex px-3 py-0.5">
      <span className="w-20 shrink-0" style={{ color: 'var(--text-muted)' }}>{label}</span>
      <span className="font-mono select-all" style={{ color: color || 'var(--text-primary)', wordBreak: 'break-all' }}>{value}</span>
    </div>
  );
}
