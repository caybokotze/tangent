import {
  Background,
  BackgroundVariant,
  Controls,
  Handle,
  MarkerType,
  MiniMap,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Edge as FlowEdge,
  type Node as FlowNode,
  type NodeProps,
  type NodeTypes,
} from '@xyflow/react';
import { useEffect, useMemo, useRef } from 'react';
import type { GraphIndex } from '../../graph/derive';
import { nodeColor } from '../../graph/derive';
import type { GraphLayout } from '../../graph/layout';
import type { Edge, TangentNode } from '../../graph/types';
import { UNLAYERED } from '../../graph/types';
import { cn } from '../../lib/cn';

type TypeData = {
  node: TangentNode;
  color: string;
  isRoot: boolean;
  isSelected: boolean;
  dimmed: boolean;
};

type LaneData = { label: string; namespaces: string[]; count: number };

function TypeNodeView({ data }: NodeProps<FlowNode<TypeData, 'type'>>): JSX.Element {
  const { node, color, isRoot, isSelected, dimmed } = data;
  const unbound = node.contracts.length === 0;
  return (
    <div
      title={node.id}
      className={cn(
        'relative flex h-full w-full flex-col justify-center overflow-hidden rounded-lg border bg-surface pr-2 pl-3.5 shadow-sm transition-all',
        isSelected ? 'border-accent ring-2 ring-accent/40' : isRoot ? 'border-line-strong' : 'border-line',
        unbound && 'border-dashed bg-surface-2',
        dimmed && 'opacity-35',
      )}
    >
      <span className="absolute inset-y-0 left-0 w-1" style={{ background: color }} />
      <Handle type="target" position={Position.Top} isConnectable={false} />
      <div className="truncate pr-9 text-[13px] font-semibold">
        <span className={unbound ? 'text-muted' : 'text-fg'}>{node.name}</span>
      </div>
      <div className="flex items-center gap-1.5 truncate text-[11px] text-faint">
        {isRoot && <span className="rounded bg-accent-soft px-1 text-[9px] font-bold tracking-wide text-accent-strong uppercase">root</span>}
        <span className="truncate">{node.kind} · {unbound ? 'no contract' : node.contracts.join(', ')}</span>
      </div>
      <div className="absolute top-1.5 right-1.5 flex gap-1">
        {node.violations > 0 && (
          <span className="rounded-full bg-danger px-1.5 text-[10px] leading-4 font-bold text-white">{node.violations}</span>
        )}
        {node.exemptions > 0 && (
          <span className="rounded-full bg-surface-3 px-1.5 text-[10px] leading-4 font-semibold text-muted">ex {node.exemptions}</span>
        )}
      </div>
      <Handle type="source" position={Position.Bottom} isConnectable={false} />
    </div>
  );
}

function LaneNodeView({ data }: NodeProps<FlowNode<LaneData, 'lane'>>): JSX.Element {
  return (
    <div className="h-full w-full rounded-xl border border-line bg-surface/40">
      <div className="flex items-center justify-between px-4 pt-3">
        <span className="text-[11px] font-bold tracking-wider text-muted uppercase">
          {data.label} <span className="font-medium text-faint">· {data.count}</span>
        </span>
        <span className="font-mono text-[10px] text-faint">{data.namespaces.join('  ')}</span>
      </div>
    </div>
  );
}

const nodeTypes: NodeTypes = { type: TypeNodeView, lane: LaneNodeView };

export type GraphCanvasProps = {
  index: GraphIndex;
  layout: GraphLayout;
  nodes: TangentNode[];
  edges: Edge[];
  roots: Set<string>;
  selectedId: string | null;
  search: string;
  onSelect: (id: string | null) => void;
  onReroot: (id: string) => void;
};

function Canvas(props: GraphCanvasProps): JSX.Element {
  const { index, layout, nodes, edges, roots, selectedId, search, onSelect, onReroot } = props;
  const { fitView, setCenter, getZoom } = useReactFlow();
  const container = useRef<HTMLDivElement>(null);

  const flowNodes = useMemo<FlowNode[]>(() => {
    const byId = new Map(nodes.map((n) => [n.id, n] as const));
    const layers = new Map(index.graph.layers.map((l) => [l.id, l] as const));
    const needle = search.trim().toLowerCase();

    const lanes: FlowNode[] = layout.lanes.map((lane) => ({
      id: `lane:${lane.id}`,
      type: 'lane',
      position: { x: lane.x, y: lane.y },
      data: {
        label: lane.label,
        namespaces: lane.id === UNLAYERED ? ['no layer'] : layers.get(lane.id)?.namespaces ?? [],
        count: lane.nodeIds.length,
      } satisfies LaneData,
      width: lane.width,
      height: lane.height,
      draggable: false,
      selectable: false,
      focusable: false,
      zIndex: 0,
    }));

    const types: FlowNode[] = [];
    for (const positioned of layout.nodes) {
      const node = byId.get(positioned.id);
      if (!node) continue;
      const matches = needle === '' || node.name.toLowerCase().includes(needle) || node.id.toLowerCase().includes(needle);
      types.push({
        id: node.id,
        type: 'type',
        position: { x: positioned.x, y: positioned.y },
        data: {
          node,
          color: nodeColor(index, node),
          isRoot: roots.has(node.id),
          isSelected: selectedId === node.id,
          dimmed: !matches,
        } satisfies TypeData,
        width: positioned.width,
        height: positioned.height,
        draggable: false,
        connectable: false,
        zIndex: 1,
      });
    }
    return [...lanes, ...types];
  }, [index, layout, nodes, roots, selectedId, search]);

  const flowEdges = useMemo<FlowEdge[]>(
    () =>
      edges.map((edge) => {
        const incident = selectedId !== null && (edge.from === selectedId || edge.to === selectedId);
        const faded = selectedId !== null && !incident;
        return {
          id: `${edge.from}->${edge.to}`,
          source: edge.from,
          target: edge.to,
          type: 'default',
          className: cn(edge.violatesLayering ? 'violating' : incident ? 'hot' : 'muted', faded && !edge.violatesLayering && 'faded'),
          zIndex: edge.violatesLayering ? 20 : incident ? 10 : 1,
          markerEnd: {
            type: MarkerType.ArrowClosed,
            width: 14,
            height: 14,
            color: edge.violatesLayering ? 'var(--edge-violation)' : incident ? 'var(--accent)' : 'var(--edge)',
          },
          label: edge.count > 1 ? String(edge.count) : undefined,
          labelShowBg: false,
        } satisfies FlowEdge;
      }),
    [edges, selectedId],
  );

  const signature = layout.nodes.map((n) => n.id).join('|');
  const selected = selectedId === null ? undefined : layout.nodes.find((n) => n.id === selectedId);

  // Frame the selected type, or the whole neighbourhood when nothing is selected.
  const frame = useRef<() => void>(() => undefined);
  frame.current = () => {
    if (selected) {
      void setCenter(selected.x + selected.width / 2, selected.y + selected.height / 2, {
        zoom: Math.max(getZoom(), 1),
        duration: 300,
      });
    } else {
      void fitView({ padding: 0.12, duration: 250 });
    }
  };

  useEffect(() => {
    const handle = window.setTimeout(() => frame.current(), 0);
    return () => window.clearTimeout(handle);
  }, [signature, selected?.id]);

  // The detail panel opening or closing changes the canvas width: re-frame.
  useEffect(() => {
    const element = container.current;
    if (!element || typeof ResizeObserver === 'undefined') return;
    let handle = 0;
    const observer = new ResizeObserver(() => {
      window.clearTimeout(handle);
      handle = window.setTimeout(() => frame.current(), 120);
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
      window.clearTimeout(handle);
    };
  }, []);

  return (
    <div ref={container} className="h-full w-full">
    <ReactFlow
      nodes={flowNodes}
      edges={flowEdges}
      nodeTypes={nodeTypes}
      nodesDraggable={false}
      nodesConnectable={false}
      edgesFocusable={false}
      minZoom={0.05}
      maxZoom={2.5}
      onNodeClick={(_, node) => {
        if (node.type === 'type') onSelect(node.id);
      }}
      onNodeDoubleClick={(_, node) => {
        if (node.type === 'type') onReroot(node.id);
      }}
      onPaneClick={() => onSelect(null)}
      fitView
    >
      <Background variant={BackgroundVariant.Dots} gap={22} size={1} color="var(--border)" />
      <MiniMap
        pannable
        zoomable
        position="bottom-right"
        style={{ width: 150, height: 100 }}
        nodeBorderRadius={4}
        maskColor="rgb(0 0 0 / 0.25)"
        nodeColor={(node) => (node.type === 'lane' ? 'transparent' : String((node.data as TypeData).color))}
        nodeStrokeColor={(node) => (node.type === 'lane' ? 'var(--border)' : 'transparent')}
      />
      <Controls position="bottom-left" showInteractive={false} />
    </ReactFlow>
    </div>
  );
}

export function GraphCanvas(props: GraphCanvasProps): JSX.Element {
  return (
    <ReactFlowProvider>
      <Canvas {...props} />
    </ReactFlowProvider>
  );
}
