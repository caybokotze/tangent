import type { GraphIndex } from '../graph/derive';
import { UNBOUND_COLOR } from '../graph/derive';
import type { TangentNode, Violation } from '../graph/types';

export type SidePanelProps = {
  index: GraphIndex;
  node: TangentNode;
  onClose: () => void;
  onSelect: (nodeId: string) => void;
};

function ViolationCard({ index, violation }: { index: GraphIndex; violation: Violation }): JSX.Element {
  const rule = index.rulesById.get(violation.ruleId);
  const isError = violation.severity === 'error';
  return (
    <div className={`violation ${isError ? 'error' : ''}`}>
      <div className="vhead">
        <span className="code">{violation.code}</span>
        <span className={`severity ${isError ? 'error' : ''}`}>{violation.severity}</span>
        {violation.member !== null && <span className="where">{violation.member}</span>}
      </div>
      <div className="msg">{violation.message}</div>
      <div className="rule-id">{violation.ruleId}</div>
      <div className="where">
        {violation.file}:{violation.line}:{violation.col}
      </div>
      {rule && (
        <pre className="rule-text" title={`${rule.contract} rule, tier ${rule.tier}`}>
          {rule.text}
        </pre>
      )}
    </div>
  );
}

/** Details for one selected type: where it lives, what it is bound to, what failed. */
export function SidePanel({ index, node, onClose, onSelect }: SidePanelProps): JSX.Element {
  const violations = index.violationsByNode.get(node.id) ?? [];
  const outgoing = index.outgoing.get(node.id) ?? [];
  const incoming = index.incoming.get(node.id) ?? [];

  return (
    <aside className="side-panel" aria-label={`Details for ${node.name}`}>
      <header>
        <div style={{ flex: 1 }}>
          <h2>{node.name}</h2>
          <div className="kind">
            {node.kind} &middot; {node.id}
          </div>
        </div>
        <button type="button" onClick={onClose} title="Close panel" aria-label="Close panel">
          &times;
        </button>
      </header>

      <section>
        <dl className="kv">
          <dt>Source</dt>
          <dd className="mono">
            {node.file}:{node.line}
          </dd>
          <dt>Layer</dt>
          <dd>{node.layer ?? <span className="empty">none</span>}</dd>
          <dt>Contracts</dt>
          <dd>
            {node.contracts.length === 0 ? (
              <span className="empty">unbound</span>
            ) : (
              <div className="chips">
                {node.contracts.map((contract) => (
                  <span className="chip" key={contract}>
                    <span
                      className="dot"
                      style={{ background: index.contractColors.get(contract) ?? UNBOUND_COLOR }}
                    />
                    {contract}
                  </span>
                ))}
              </div>
            )}
          </dd>
          <dt>Exemptions</dt>
          <dd>{node.exemptions}</dd>
          <dt>References</dt>
          <dd>
            {outgoing.length} out, {incoming.length} in
          </dd>
        </dl>
      </section>

      <section>
        <h3>
          Violations ({violations.length}
          {node.violations !== violations.length ? ` of ${node.violations} reported` : ''})
        </h3>
        {violations.length === 0 ? (
          <p className="empty">No violations on this type.</p>
        ) : (
          violations.map((violation, i) => (
            <ViolationCard key={`${violation.ruleId}:${violation.line}:${i}`} index={index} violation={violation} />
          ))
        )}
      </section>

      <section>
        <h3>Members ({node.members.length})</h3>
        {node.members.length === 0 ? (
          <p className="empty">No members were exported for this type.</p>
        ) : (
          <ul className="member-list">
            {node.members.map((member) => (
              <li key={`${member.kind}:${member.name}:${member.signature}`}>
                <span className="mkind">{member.kind}</span>
                <span className="msig">{member.signature}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h3>Dependencies ({outgoing.length})</h3>
        {outgoing.length === 0 ? (
          <p className="empty">This type references nothing else in the graph.</p>
        ) : (
          <ul className="member-list">
            {outgoing.map((edge) => (
              <li key={`${edge.to}:${edge.kind}`}>
                <span className="mkind">{edge.violatesLayering ? 'illegal' : edge.kind}</span>
                <button type="button" className="link-button msig" onClick={() => onSelect(edge.to)}>
                  {index.nodesById.get(edge.to)?.name ?? edge.to}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h3>Referenced by ({incoming.length})</h3>
        {incoming.length === 0 ? (
          <p className="empty">Nothing in the graph references this type.</p>
        ) : (
          <ul className="member-list">
            {incoming.map((edge) => (
              <li key={`${edge.from}:${edge.kind}`}>
                <span className="mkind">{edge.violatesLayering ? 'illegal' : edge.kind}</span>
                <button type="button" className="link-button msig" onClick={() => onSelect(edge.from)}>
                  {index.nodesById.get(edge.from)?.name ?? edge.from}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </aside>
  );
}
