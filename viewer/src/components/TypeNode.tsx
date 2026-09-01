import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import type { TangentNode } from '../graph/types';

export type TypeNodeData = {
  node: TangentNode;
  color: string;
  /** Faded out because a search is active and this node does not match. */
  dimmed: boolean;
  /** Outlined because it matches the current search. */
  matched: boolean;
  isSelected: boolean;
};

export type TypeFlowNode = Node<TypeNodeData, 'type'>;

export function TypeNode({ data }: NodeProps<TypeFlowNode>): JSX.Element {
  const { node, color, dimmed, matched, isSelected } = data;
  const className = [
    'type-node',
    dimmed ? 'dim' : '',
    matched ? 'match' : '',
    isSelected ? 'selected' : '',
  ]
    .filter(Boolean)
    .join(' ');

  const contracts = node.contracts.length > 0 ? node.contracts.join(', ') : 'unbound';

  return (
    <div className={className} style={{ ['--contract-color' as string]: color }} title={node.id}>
      <Handle type="target" position={Position.Top} isConnectable={false} />
      <div className="name">{node.name}</div>
      <div className="sub">
        {node.kind} &middot; {contracts}
      </div>
      <div className="badges">
        {node.violations > 0 && (
          <span className="badge violations" title={`${node.violations} violation(s)`}>
            {node.violations}
          </span>
        )}
        {node.exemptions > 0 && (
          <span className="badge exemptions" title={`${node.exemptions} exemption(s)`}>
            ex {node.exemptions}
          </span>
        )}
      </div>
      <Handle type="source" position={Position.Bottom} isConnectable={false} />
    </div>
  );
}
