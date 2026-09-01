import { useEffect, useMemo } from 'react';
import {
  Background,
  BackgroundVariant,
  Controls,
  MarkerType,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Edge as FlowEdge,
  type Node as FlowNode,
  type NodeTypes,
} from '@xyflow/react';
import type { GraphLayout } from '../graph/layout';
import type { GraphIndex } from '../graph/derive';
import { matchesSearch, nodeColor } from '../graph/derive';
import type { Edge, TangentNode } from '../graph/types';
import { UNLAYERED } from '../graph/types';
import { LaneNode } from './LaneNode';
import { TypeNode } from './TypeNode';

const nodeTypes: NodeTypes = { type: TypeNode, lane: LaneNode };

export type GraphCanvasProps = {
  index: GraphIndex;
  layout: GraphLayout;
  nodes: TangentNode[];
  edges: Edge[];
  search: string;
  selectedNodeId: string | null;
  onSelect: (nodeId: string | null) => void;
};

function buildFlowNodes(props: GraphCanvasProps): FlowNode[] {
  const { index, layout, nodes, search, selectedNodeId } = props;
  const byId = new Map(nodes.map((node) => [node.id, node] as const));
  const searching = search.trim() !== '';
  const layerById = new Map(index.graph.layers.map((layer) => [layer.id, layer] as const));

  const lanes: FlowNode[] = layout.lanes.map((lane) => ({
    id: `lane:${lane.id}`,
    type: 'lane',
    position: { x: lane.x, y: lane.y },
    data: {
      label: lane.label,
      namespaces: lane.id === UNLAYERED ? ['no layer'] : layerById.get(lane.id)?.namespaces ?? [],
      nodeCount: lane.nodeIds.length,
    },
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
    const matched = searching && matchesSearch(node, search);
    types.push({
      id: node.id,
      type: 'type',
      position: { x: positioned.x, y: positioned.y },
      data: {
        node,
        color: nodeColor(index, node),
        dimmed: searching && !matched,
        matched,
        isSelected: selectedNodeId === node.id,
      },
      width: positioned.width,
      height: positioned.height,
      draggable: false,
      connectable: false,
      zIndex: 1,
    });
  }

  return [...lanes, ...types];
}

function buildFlowEdges(props: GraphCanvasProps): FlowEdge[] {
  const { edges, selectedNodeId } = props;
  return edges.map((edge) => {
    const incident = selectedNodeId !== null && (edge.from === selectedNodeId || edge.to === selectedNodeId);
    const faded = selectedNodeId !== null && !incident;
    const className = [edge.violatesLayering ? 'violating' : 'muted', faded ? 'faded' : '']
      .filter(Boolean)
      .join(' ');
    return {
      id: `${edge.from}->${edge.to}`,
      source: edge.from,
      target: edge.to,
      type: 'default',
      className,
      // Layering violations are drawn on top of every other edge.
      zIndex: edge.violatesLayering ? 20 : 1,
      markerEnd: {
        type: MarkerType.ArrowClosed,
        width: 14,
        height: 14,
        color: edge.violatesLayering ? 'var(--edge-violation)' : 'var(--edge)',
      },
      label: edge.count > 1 ? String(edge.count) : undefined,
      labelShowBg: false,
      data: { kind: edge.kind, count: edge.count },
    } satisfies FlowEdge;
  });
}

function Canvas(props: GraphCanvasProps): JSX.Element {
  const { index, layout, nodes, edges, search, selectedNodeId, onSelect } = props;
  const flowNodes = useMemo(
    () => buildFlowNodes({ index, layout, nodes, edges, search, selectedNodeId, onSelect }),
    [index, layout, nodes, edges, search, selectedNodeId, onSelect],
  );
  const flowEdges = useMemo(
    () => buildFlowEdges({ index, layout, nodes, edges, search, selectedNodeId, onSelect }),
    [index, layout, nodes, edges, search, selectedNodeId, onSelect],
  );
  const { fitView } = useReactFlow();

  // Re-frame whenever the set of laid-out nodes changes.
  const signature = layout.nodes.map((n) => n.id).join('|');
  useEffect(() => {
    const handle = window.setTimeout(() => void fitView({ padding: 0.12, duration: 200 }), 0);
    return () => window.clearTimeout(handle);
  }, [signature, fitView]);

  return (
    <ReactFlow
      nodes={flowNodes}
      edges={flowEdges}
      nodeTypes={nodeTypes}
      nodesDraggable={false}
      nodesConnectable={false}
      edgesFocusable={false}
      elevateEdgesOnSelect={false}
      minZoom={0.05}
      maxZoom={2.5}
      proOptions={{ hideAttribution: false }}
      onNodeClick={(_, node) => onSelect(node.id)}
      onPaneClick={() => onSelect(null)}
      fitView
    >
      <Background variant={BackgroundVariant.Dots} gap={22} size={1} color="var(--border)" />
      <MiniMap
        pannable
        zoomable
        position="bottom-right"
        nodeStrokeWidth={2}
        nodeBorderRadius={3}
        maskColor="var(--lane-bg)"
        style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}
        nodeColor={(node) =>
          node.type === 'lane' ? 'var(--surface-2)' : String(node.data['color'] ?? 'var(--edge)')
        }
        nodeStrokeColor="var(--border-strong)"
      />
      <Controls position="top-right" showInteractive={false} />
    </ReactFlow>
  );
}

export function GraphCanvas(props: GraphCanvasProps): JSX.Element {
  return (
    <ReactFlowProvider>
      <Canvas {...props} />
    </ReactFlowProvider>
  );
}
