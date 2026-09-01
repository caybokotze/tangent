import { useMemo } from 'react';
import { contractMatrix } from '../graph/derive';
import type { GraphIndex } from '../graph/derive';

export type ContractViewProps = {
  index: GraphIndex;
  contractId: string;
  onContractChange: (contractId: string) => void;
  onSelect: (nodeId: string) => void;
};

/** Bound types x rules for one contract: a cell fails if a violation cites that rule. */
export function ContractView({
  index,
  contractId,
  onContractChange,
  onSelect,
}: ContractViewProps): JSX.Element {
  const matrix = useMemo(() => contractMatrix(index, contractId), [index, contractId]);
  const contract = index.graph.contracts.find((c) => c.id === contractId);
  const rules = index.rulesById;

  return (
    <div className="contract-view">
      <div className="contract-head">
        <label htmlFor="contract-picker">Contract</label>
        <select
          id="contract-picker"
          value={contractId}
          onChange={(event) => onContractChange(event.target.value)}
        >
          {index.graph.contracts.map((c) => (
            <option key={c.id} value={c.id}>
              {c.id}
            </option>
          ))}
        </select>
        {contract && (
          <span className="meta">
            {contract.file}:{contract.line} &middot; {contract.ruleCount} rule
            {contract.ruleCount === 1 ? '' : 's'}
            {contract.extends !== null ? ` · extends ${contract.extends}` : ''}
          </span>
        )}
        <span className="meta">
          {matrix.rows.length} bound type{matrix.rows.length === 1 ? '' : 's'} &middot;{' '}
          {matrix.ruleIds.length} rule column{matrix.ruleIds.length === 1 ? '' : 's'}
        </span>
      </div>

      {matrix.rows.length === 0 ? (
        <p className="empty">No type in this graph is bound to {contractId}.</p>
      ) : matrix.ruleIds.length === 0 ? (
        <p className="empty">
          The export has no rule ids for {contractId}: no violations cite it and the graph has no
          rules array.
        </p>
      ) : (
        <table className="matrix">
          <thead>
            <tr>
              <th className="type">Type</th>
              {matrix.ruleIds.map((ruleId) => (
                <th className="rule" key={ruleId} title={rules.get(ruleId)?.text ?? ruleId}>
                  {ruleId}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {matrix.rows.map((row) => (
              <tr key={row.node.id}>
                <th className="type">
                  <button type="button" className="link-button" onClick={() => onSelect(row.node.id)}>
                    {row.node.name}
                  </button>
                </th>
                {row.cells.map((cell) => (
                  <td
                    key={cell.ruleId}
                    className={`cell ${cell.failing ? 'fail' : 'pass'}`}
                    title={
                      cell.failing
                        ? cell.violations.map((v) => `${v.code}: ${v.message}`).join('\n')
                        : `${row.node.name} satisfies ${cell.ruleId}`
                    }
                    onClick={cell.failing ? () => onSelect(row.node.id) : undefined}
                  >
                    {cell.failing ? '✗' : '✓'}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
