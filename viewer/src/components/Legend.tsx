import type { GraphIndex } from '../graph/derive';
import { UNBOUND_COLOR } from '../graph/derive';

/** Contract colour key for the swimlane view. */
export function Legend({ index }: { index: GraphIndex }): JSX.Element {
  return (
    <div className="legend">
      <h4>Contracts</h4>
      <ul>
        {index.graph.contracts.map((contract) => (
          <li key={contract.id}>
            <span
              className="swatch"
              style={{ background: index.contractColors.get(contract.id) ?? UNBOUND_COLOR }}
            />
            {contract.id}
            {contract.extends !== null && (
              <span style={{ color: 'var(--text-faint)' }}>: {contract.extends}</span>
            )}
          </li>
        ))}
        <li>
          <span className="swatch" style={{ background: UNBOUND_COLOR }} />
          unbound
        </li>
        <li>
          <span className="line" />
          layering violation
        </li>
        <li>
          <span className="line muted" />
          allowed reference
        </li>
      </ul>
    </div>
  );
}
