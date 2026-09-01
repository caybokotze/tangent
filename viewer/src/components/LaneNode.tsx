import type { Node, NodeProps } from '@xyflow/react';

export type LaneNodeData = {
  label: string;
  /** Namespace globs of the layer, shown on the right of the lane header. */
  namespaces: string[];
  nodeCount: number;
};

export type LaneFlowNode = Node<LaneNodeData, 'lane'>;

export function LaneNode({ data }: NodeProps<LaneFlowNode>): JSX.Element {
  return (
    <div className="rf-lane">
      <div className="lane-label">
        {data.label} <span style={{ opacity: 0.7 }}>({data.nodeCount})</span>
      </div>
      <div className="lane-meta">{data.namespaces.join('  ')}</div>
    </div>
  );
}
