import { ArrowDownLeft, ArrowUpRight, ExternalLink, GitFork, Network, ShieldOff, X } from 'lucide-react';
import type { ReactNode } from 'react';
import { useExplorer, useFileOf } from '../app/context';
import { CodeFrame, InlineCode } from './CodeFrame';
import { FileLink, RuleLink, TypeLink } from './links';
import { Badge, Button, ContractChip, FileStatusBadge, PassBar, RuleStatusIcon, SectionTitle, SeverityIcon } from './ui';
import { LAYERS_CONTRACT, cleanMessage, editorLink, pathInProject } from '../graph/explore';
import type { Exemption, SourceFile, Violation } from '../graph/types';
import { cn, namespaceOf, plural } from '../lib/cn';
import { langOf } from '../lib/highlight';
import type { Selection } from '../lib/route';

const TIER_LABEL = ['symbol', 'reference', 'flow'];

export function DetailPanel(props: { selection: Selection }): JSX.Element {
  const { select } = useExplorer();
  const { selection } = props;

  return (
    <aside
      // Below xl the panel floats over the view instead of squeezing it into an unreadable column.
      className="absolute inset-y-0 right-0 z-30 flex w-[min(520px,100vw)] shrink-0 flex-col border-l border-line bg-surface shadow-2xl xl:static xl:z-auto xl:w-[min(560px,40vw)] xl:shadow-none"
      aria-label="Details"
    >
      <div className="flex h-11 shrink-0 items-center justify-between border-b border-line px-4">
        <span className="text-[11px] font-semibold tracking-wider text-faint uppercase">{selection.kind}</span>
        <button
          type="button"
          onClick={() => select(null)}
          className="rounded p-1 text-faint hover:bg-surface-2 hover:text-fg"
          aria-label="Close details"
        >
          <X className="size-4" />
        </button>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
        {selection.kind === 'type' && <TypeDetail id={selection.id} />}
        {selection.kind === 'issue' && <IssueDetail ruleId={selection.id} />}
        {selection.kind === 'rule' && <RuleDetail ruleId={selection.id} />}
        {selection.kind === 'file' && <FileDetail id={Number(selection.id)} />}
        {selection.kind === 'contract' && <ContractDetail id={selection.id} />}
        {selection.kind === 'layer' && <LayerDetail id={selection.id} />}
      </div>
    </aside>
  );
}

function Section(props: { title: ReactNode; right?: ReactNode; children: ReactNode }): JSX.Element {
  return (
    <section className="mt-6 first:mt-0">
      <SectionTitle right={props.right}>{props.title}</SectionTitle>
      {props.children}
    </section>
  );
}

function Missing(props: { what: string }): JSX.Element {
  return <div className="text-muted">This {props.what} is not in the loaded graph.</div>;
}

/** The code around a violation, or its location when the export has no source. */
function ViolationFrame(props: { violation: Violation; showType?: boolean }): JSX.Element {
  const { index } = useExplorer();
  const { violation } = props;
  const href = editorLink(index.graph.root, violation.file, violation.line, violation.col);

  return (
    <div className="rounded-lg border border-line bg-surface-2/60 p-3">
      <div className="flex items-start gap-2">
        <SeverityIcon severity={violation.severity} className="mt-0.5" />
        <div className="min-w-0 flex-1">
          <div className="text-fg">{cleanMessage(violation.message)}</div>
          <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
            {props.showType && <TypeLink id={violation.node} />}
            {violation.member && <Badge mono>{violation.member}</Badge>}
            <Badge mono>{violation.code}</Badge>
            <FileLink path={violation.file} line={violation.line} />
          </div>
        </div>
      </div>
      {violation.snippet ? (
        <CodeFrame
          className="mt-3"
          lines={violation.snippet.lines}
          startLine={violation.snippet.startLine}
          highlight={[violation.line]}
          window={13}
          lang={langOf(violation.file)}
          title={`${violation.file}:${violation.line}:${violation.col}`}
          href={href}
        />
      ) : (
        <div className="mt-2 text-xs text-faint">
          Export with <code className="font-mono">--include-source</code> to see the code here.
        </div>
      )}
    </div>
  );
}

/** The `.tan` source around a line, when the export embedded the contract files. */
function TanFrame(props: { file: string; line: number; window?: number }): JSX.Element | null {
  const { index, filesByPath } = useExplorer();
  const path = props.file.includes('/') ? props.file : `.tangent/${props.file}`;
  const file = filesByPath.get(path);
  if (!file?.text) return null;
  return (
    <CodeFrame
      lines={file.text.replace(/\r\n/g, '\n').split('\n')}
      startLine={1}
      highlight={[props.line]}
      window={props.window ?? 11}
      lang="tangent"
      title={`${path}:${props.line}`}
      href={editorLink(index.graph.root, path, props.line)}
    />
  );
}

// ---------------------------------------------------------------------------

function TypeDetail(props: { id: string }): JSX.Element {
  const { index, contracts, exemptionsByNode, navigate, select } = useExplorer();
  const fileOf = useFileOf();
  const node = index.nodesById.get(props.id);
  if (!node) return <Missing what="type" />;

  const violations = index.violationsByNode.get(node.id) ?? [];
  const exemptions = exemptionsByNode.get(node.id) ?? [];
  const outgoing = index.outgoing.get(node.id) ?? [];
  const incoming = index.incoming.get(node.id) ?? [];
  const file = fileOf(node);
  const href = editorLink(index.graph.root, node.file, node.line);
  const applied = contracts.filter((c) => c.types.some((t) => t.id === node.id));

  return (
    <div>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="truncate text-lg font-semibold text-fg">{node.name}</h2>
          <div className="truncate font-mono text-[11px] text-faint">{namespaceOf(node.id)}</div>
        </div>
        {violations.length > 0 ? <Badge tone="danger">{plural(violations.length, 'violation')}</Badge> : <Badge tone="success">conforms</Badge>}
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-1.5">
        <Badge>{node.kind}</Badge>
        {node.layer && (
          <button type="button" onClick={() => select({ kind: 'layer', id: node.layer ?? '' })}>
            <Badge tone="accent">layer {node.layer}</Badge>
          </button>
        )}
        {node.contracts.length === 0 && <Badge>no contract</Badge>}
        {node.contracts.map((id) => (
          <ContractChip key={id} id={id} color={index.contractColors.get(id) ?? 'var(--faint)'} onClick={() => select({ kind: 'contract', id })} />
        ))}
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <Button size="sm" onClick={() => navigate({ view: 'graph', root: `type:${node.id}` })}>
          <Network className="size-3.5" /> Show in graph
        </Button>
        {href && (
          <a href={href} className="inline-flex h-7 items-center gap-1.5 rounded-md border border-line bg-surface-2 px-2 text-xs font-medium text-fg hover:bg-surface-3">
            <ExternalLink className="size-3.5" /> Open in VS Code
          </a>
        )}
        {file && <FileLink path={file.path} line={node.line} />}
      </div>

      {violations.length > 0 && (
        <Section title="Violations">
          <div className="flex flex-col gap-3">
            {violations.map((violation, i) => (
              <div key={i}>
                <RuleLink id={violation.ruleId} className="mb-1.5" />
                <ViolationFrame violation={violation} />
              </div>
            ))}
          </div>
        </Section>
      )}

      {exemptions.length > 0 && (
        <Section title="Exemptions">
          <ExemptionList exemptions={exemptions} />
        </Section>
      )}

      {applied.length > 0 && (
        <Section title="Rules applied">
          <div className="flex flex-col gap-3">
            {applied.map((contract) => (
              <div key={contract.id} className="rounded-lg border border-line">
                <div className="flex items-center justify-between border-b border-line bg-surface-2 px-3 py-1.5">
                  <ContractChip id={contract.id} color={contract.color} onClick={() => select({ kind: 'contract', id: contract.id })} />
                  <span className="text-[11px] text-faint">{plural(contract.rules.length, 'rule')}</span>
                </div>
                <ul>
                  {contract.rules.map((summary) => {
                    const application = summary.applications.find((a) => a.node.id === node.id);
                    return (
                      <li key={summary.rule.id} className="flex items-center gap-2 border-b border-line/60 px-3 py-1.5 last:border-b-0">
                        {application && <RuleStatusIcon status={application.status} />}
                        <button
                          type="button"
                          className="min-w-0 flex-1 truncate text-left hover:text-accent-strong"
                          onClick={() => select({ kind: 'rule', id: summary.rule.id })}
                        >
                          <InlineCode code={summary.rule.text} lang="tangent" />
                        </button>
                      </li>
                    );
                  })}
                  {contract.rules.length === 0 && <li className="px-3 py-1.5 text-muted">Only inherited rules.</li>}
                </ul>
              </div>
            ))}
          </div>
        </Section>
      )}

      <Section title={<span className="inline-flex items-center gap-1"><ArrowUpRight className="size-3" /> Depends on</span>} right={<span className="text-[11px] text-faint">{outgoing.length}</span>}>
        <EdgeList ids={outgoing.map((e) => ({ id: e.to, count: e.count, illegal: e.violatesLayering }))} empty="No references to other types in the graph." />
      </Section>

      <Section title={<span className="inline-flex items-center gap-1"><ArrowDownLeft className="size-3" /> Used by</span>} right={<span className="text-[11px] text-faint">{incoming.length}</span>}>
        <EdgeList ids={incoming.map((e) => ({ id: e.from, count: e.count, illegal: e.violatesLayering }))} empty="Nothing in the graph references this type." />
      </Section>

      {node.members.length > 0 && (
        <Section title="Public members" right={<span className="text-[11px] text-faint">{node.members.length}</span>}>
          <ul className="overflow-hidden rounded-lg border border-line bg-code">
            {node.members.map((member, i) => (
              <li key={i} className="flex items-center gap-2 border-b border-line/60 px-3 py-1 last:border-b-0">
                <span className="w-16 shrink-0 text-[10px] tracking-wide text-faint uppercase">{member.kind}</span>
                <InlineCode code={member.signature} lang="csharp" className="truncate" />
              </li>
            ))}
          </ul>
        </Section>
      )}
    </div>
  );
}

function EdgeList(props: { ids: { id: string; count: number; illegal: boolean }[]; empty: string }): JSX.Element {
  if (props.ids.length === 0) return <div className="text-muted">{props.empty}</div>;
  const sorted = [...props.ids].sort((a, b) => Number(b.illegal) - Number(a.illegal) || b.count - a.count);
  return (
    <ul className="flex flex-col gap-1">
      {sorted.map((edge) => (
        <li key={edge.id} className="flex items-center gap-2">
          <TypeLink id={edge.id} className="min-w-0 flex-1" />
          {edge.illegal && <Badge tone="danger">breaks layering</Badge>}
          <span className="text-[11px] text-faint tabular-nums">×{edge.count}</span>
        </li>
      ))}
    </ul>
  );
}

function ExemptionList(props: { exemptions: Exemption[]; showType?: boolean }): JSX.Element {
  return (
    <ul className="flex flex-col gap-2">
      {props.exemptions.map((exemption, i) => (
        <li key={i} className="flex items-start gap-2 rounded-lg border border-dashed border-line-strong px-3 py-2">
          <ShieldOff className="mt-0.5 size-4 shrink-0 text-faint" />
          <div className="min-w-0 flex-1">
            <div className="text-fg italic">“{exemption.reason}”</div>
            <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
              {props.showType && exemption.node && <TypeLink id={exemption.node} />}
              {exemption.member && <Badge mono>{exemption.member}</Badge>}
              {exemption.ruleId ? <RuleLink id={exemption.ruleId} /> : <Badge>whole contract</Badge>}
              <FileLink path={exemption.file} line={exemption.line} />
            </div>
          </div>
        </li>
      ))}
    </ul>
  );
}

// ---------------------------------------------------------------------------

function IssueDetail(props: { ruleId: string }): JSX.Element {
  const { issues, contracts, select } = useExplorer();
  const issue = issues.find((i) => i.ruleId === props.ruleId);
  if (!issue) return <RuleDetail ruleId={props.ruleId} />;

  const color = issue.contract === LAYERS_CONTRACT ? 'var(--danger)' : contracts.find((c) => c.id === issue.contract)?.color ?? 'var(--faint)';

  return (
    <div>
      <div className="flex items-start gap-2">
        <SeverityIcon severity={issue.severity} className="mt-1" />
        <h2 className="min-w-0 flex-1 text-lg font-semibold text-fg">
          {issue.contract === LAYERS_CONTRACT ? issue.message : cleanMessage(issue.message)}
        </h2>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-1.5">
        <ContractChip id={issue.contract} color={color} onClick={issue.contract === LAYERS_CONTRACT ? undefined : () => select({ kind: 'contract', id: issue.contract })} />
        {issue.codes.map((code) => (
          <Badge key={code} mono>{code}</Badge>
        ))}
        <Badge tone="danger">{plural(issue.occurrences.length, 'event')}</Badge>
        <Badge>{plural(issue.types.length, 'type')}</Badge>
      </div>

      <Section title="The rule" right={<RuleLink id={issue.ruleId}>view rule</RuleLink>}>
        {issue.contract !== LAYERS_CONTRACT && (
          <div className="rounded-lg border border-line bg-code px-3 py-2">
            <InlineCode code={issue.title} lang="tangent" className="whitespace-pre-wrap" />
          </div>
        )}
        {issue.source && (
          <div className="mt-2">
            <TanFrame file={issue.source.file} line={issue.source.line} />
          </div>
        )}
      </Section>

      <Section title="Events">
        <div className="flex flex-col gap-3">
          {issue.occurrences.map((violation, i) => (
            <ViolationFrame key={i} violation={violation} showType />
          ))}
        </div>
      </Section>
    </div>
  );
}

function RuleDetail(props: { ruleId: string }): JSX.Element {
  const { index, contracts, issues, select } = useExplorer();
  const summary = contracts.flatMap((c) => c.rules).find((r) => r.rule.id === props.ruleId);
  const issue = issues.find((i) => i.ruleId === props.ruleId);

  if (!summary) {
    if (issue) return <IssueDetail ruleId={props.ruleId} />;
    return <Missing what="rule" />;
  }

  const contract = contracts.find((c) => c.id === summary.rule.contract);
  const source = summary.source ?? (contract ? { file: contract.file, line: contract.line } : null);

  return (
    <div>
      <div className="rounded-lg border border-line bg-code px-3 py-2.5">
        <InlineCode code={summary.rule.text} lang="tangent" className="text-[13px] whitespace-pre-wrap" />
      </div>
      <div className="mt-2 font-mono text-[11px] text-faint">{summary.rule.id}</div>

      <div className="mt-3 flex flex-wrap items-center gap-1.5">
        {contract && <ContractChip id={contract.id} color={contract.color} onClick={() => select({ kind: 'contract', id: contract.id })} />}
        <Badge>tier {summary.rule.tier} · {TIER_LABEL[summary.rule.tier] ?? '?'}</Badge>
        <PassBar passing={summary.passing} failing={summary.failing} exempt={summary.exempt} />
        <span className="text-xs text-muted">
          {summary.passing} pass · <span className={cn(summary.failing > 0 && 'font-semibold text-danger')}>{summary.failing} fail</span> · {summary.exempt} exempt
        </span>
      </div>

      {source && (
        <Section title="Declared in">
          <TanFrame file={source.file} line={source.line} />
          {!index.graph.files?.some((f) => f.text) && <FileLink path={source.file.includes('/') ? source.file : `.tangent/${source.file}`} line={source.line} />}
        </Section>
      )}

      <Section title="Applies to" right={<span className="text-[11px] text-faint">{plural(summary.applications.length, 'type')}</span>}>
        <ul className="flex flex-col gap-1.5">
          {summary.applications.map((application) => (
            <li key={application.node.id} className="flex items-start gap-2">
              <RuleStatusIcon status={application.status} className="mt-0.5" />
              <div className="min-w-0 flex-1">
                <TypeLink id={application.node.id} />
                {application.status === 'fail' && (
                  <div className="truncate text-xs text-muted">{cleanMessage(application.violations[0]?.message ?? '')}</div>
                )}
                {application.status === 'exempt' && (
                  <div className="truncate text-xs text-faint italic">exempt: {application.exemptions[0]?.reason}</div>
                )}
              </div>
            </li>
          ))}
        </ul>
      </Section>

      {issue && (
        <Section title="Events">
          <div className="flex flex-col gap-3">
            {issue.occurrences.map((violation, i) => (
              <ViolationFrame key={i} violation={violation} showType />
            ))}
          </div>
        </Section>
      )}
    </div>
  );
}

function FileDetail(props: { id: number }): JSX.Element {
  const { index, filesById, select } = useExplorer();
  const file = filesById.get(props.id);
  if (!file) return <Missing what="file" />;

  const violations = index.graph.violations.filter((v) => v.fileId === file.id || (v.fileId === undefined && v.file === file.path));
  const exemptions = (index.graph.exemptions ?? []).filter((e) => e.fileId === file.id);
  const declared = index.graph.contracts.filter((c) => c.file === file.path);
  const href = editorLink(index.graph.root, file.path, 1);

  return (
    <div>
      <h2 className="font-mono text-[15px] font-semibold break-all text-fg">{file.path.slice(file.path.lastIndexOf('/') + 1)}</h2>
      <div className="mt-0.5 font-mono text-[11px] break-all text-faint">{file.path}</div>
      <div className="mt-3 flex flex-wrap items-center gap-1.5">
        <FileStatusBadge status={file.status} />
        {file.project && <Badge>{file.project}</Badge>}
        <Badge>{plural(file.lines, 'line')}</Badge>
        {violations.length > 0 && <Badge tone="danger">{plural(violations.length, 'violation')}</Badge>}
        {href && (
          <a href={href} className="ml-auto inline-flex items-center gap-1 text-xs font-medium text-accent-strong hover:underline">
            Open in VS Code <ExternalLink className="size-3" />
          </a>
        )}
      </div>

      {file.kind === 'contract' ? (
        <>
          {declared.length > 0 && (
            <Section title="Contracts declared here">
              <div className="flex flex-wrap gap-1.5">
                {declared.map((c) => (
                  <ContractChip key={c.id} id={c.id} color={index.contractColors.get(c.id) ?? 'var(--faint)'} onClick={() => select({ kind: 'contract', id: c.id })} />
                ))}
              </div>
            </Section>
          )}
          <Section title="Source">
            {file.text ? (
              <CodeFrame lines={file.text.replace(/\r\n/g, '\n').split('\n')} startLine={1} lang="tangent" title={file.path} href={href} />
            ) : (
              <div className="text-muted">Export with <code className="font-mono">--include-source</code> to see the contract text.</div>
            )}
          </Section>
        </>
      ) : (
        <>
          <Section title="Types declared" right={<span className="text-[11px] text-faint">{file.types.length}</span>}>
            {file.types.length === 0 ? (
              <div className="text-muted">This file declares no type (top-level statements, global usings, or assembly attributes).</div>
            ) : (
              <ul className="flex flex-col gap-1.5">
                {file.types.map((id) => {
                  const node = index.nodesById.get(id);
                  return (
                    <li key={id} className="flex items-center gap-2">
                      {node ? <TypeLink id={id} /> : <span className="font-medium text-muted">{id}</span>}
                      <span className="flex flex-wrap gap-1">
                        {node && node.contracts.length > 0 ? (
                          node.contracts.map((c) => <ContractChip key={c} id={c} color={index.contractColors.get(c) ?? 'var(--faint)'} />)
                        ) : (
                          <Badge>no contract</Badge>
                        )}
                      </span>
                    </li>
                  );
                })}
              </ul>
            )}
          </Section>
          <UntrackedHint file={file} />
        </>
      )}

      {violations.length > 0 && (
        <Section title="Violations in this file">
          <div className="flex flex-col gap-3">
            {violations.map((violation, i) => (
              <ViolationFrame key={i} violation={violation} showType />
            ))}
          </div>
        </Section>
      )}

      {exemptions.length > 0 && (
        <Section title="Exemptions in this file">
          <ExemptionList exemptions={exemptions} showType />
        </Section>
      )}
    </div>
  );
}

function UntrackedHint(props: { file: SourceFile }): JSX.Element | null {
  if (props.file.status === 'tracked' || props.file.types.length === 0) return null;
  return (
    <div className="mt-4 flex items-start gap-2 rounded-lg border border-line bg-surface-2/60 px-3 py-2.5 text-muted">
      <GitFork className="mt-0.5 size-4 shrink-0 text-faint" />
      <div>
        {props.file.status === 'untracked' ? 'No contract describes the types in ' : 'Some types in '}
        <span className="font-mono text-fg">{pathInProject(props.file)}</span>
        {props.file.status === 'untracked' ? '.' : ' have no contract.'} Bind them with a <code className="font-mono">match</code> or{' '}
        <code className="font-mono">bind</code> in <code className="font-mono">.tangent/</code>, or leave them to the layer rules.
      </div>
    </div>
  );
}

function ContractDetail(props: { id: string }): JSX.Element {
  const { contracts, select } = useExplorer();
  const contract = contracts.find((c) => c.id === props.id);
  if (!contract) return <Missing what="contract" />;

  return (
    <div>
      <div className="flex items-center gap-2">
        <span className="size-3 rounded-full" style={{ background: contract.color }} />
        <h2 className="text-lg font-semibold text-fg">{contract.id}</h2>
        {contract.violations > 0 ? <Badge tone="danger">{plural(contract.violations, 'violation')}</Badge> : <Badge tone="success">all passing</Badge>}
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-2 text-muted">
        {contract.extends && (
          <span>
            extends <ContractChip id={contract.extends} color="var(--faint)" onClick={() => select({ kind: 'contract', id: contract.extends ?? '' })} />
          </span>
        )}
        <FileLink path={contract.file} line={contract.line} />
      </div>

      <Section title="Declared in">
        <TanFrame file={contract.file} line={contract.line} window={15} />
      </Section>

      <Section title="Rules" right={<span className="text-[11px] text-faint">{contract.rules.length}</span>}>
        <ul className="flex flex-col gap-1.5">
          {contract.rules.map((summary) => (
            <li key={summary.rule.id}>
              <button
                type="button"
                className="flex w-full items-center gap-2 rounded-md px-2 py-1 text-left hover:bg-surface-2"
                onClick={() => select({ kind: 'rule', id: summary.rule.id })}
              >
                <RuleStatusIcon status={summary.failing > 0 ? 'fail' : 'pass'} />
                <InlineCode code={summary.rule.text} lang="tangent" className="min-w-0 flex-1 truncate" />
                <PassBar passing={summary.passing} failing={summary.failing} exempt={summary.exempt} />
              </button>
            </li>
          ))}
        </ul>
      </Section>

      <Section title="Bound types" right={<span className="text-[11px] text-faint">{contract.types.length}</span>}>
        <ul className="flex flex-col gap-1">
          {contract.types.map((node) => (
            <li key={node.id} className="flex items-center gap-2">
              <TypeLink id={node.id} className="min-w-0 flex-1" />
              {node.violations > 0 && <Badge tone="danger">{node.violations}</Badge>}
            </li>
          ))}
        </ul>
      </Section>
    </div>
  );
}

function LayerDetail(props: { id: string }): JSX.Element {
  const { layers, navigate } = useExplorer();
  const layer = layers.find((l) => l.from === props.id);
  if (!layer) return <Missing what="layer" />;

  return (
    <div>
      <h2 className="text-lg font-semibold text-fg">Layer {layer.from}</h2>
      <div className="mt-2 flex flex-wrap items-center gap-1.5 text-muted">
        may depend on
        {layer.allowed.length === 0 ? <Badge>nothing</Badge> : layer.allowed.map((a) => <Badge key={a} tone="accent">{a}</Badge>)}
      </div>
      <div className="mt-3">
        <Button size="sm" onClick={() => navigate({ view: 'graph', root: `layer:${layer.from}` })}>
          <Network className="size-3.5" /> Show in graph
        </Button>
      </div>

      {layer.violations.length > 0 && (
        <Section title="Violations">
          <div className="flex flex-col gap-3">
            {layer.violations.map((violation, i) => (
              <ViolationFrame key={i} violation={violation} showType />
            ))}
          </div>
        </Section>
      )}

      <Section title="Types" right={<span className="text-[11px] text-faint">{layer.types.length}</span>}>
        <ul className="flex flex-col gap-1">
          {layer.types.map((node) => (
            <li key={node.id} className="flex items-center gap-2">
              <TypeLink id={node.id} className="min-w-0 flex-1" />
              {node.contracts.length === 0 && <span className="text-[11px] text-faint">no contract</span>}
              {node.violations > 0 && <Badge tone="danger">{node.violations}</Badge>}
            </li>
          ))}
        </ul>
      </Section>
    </div>
  );
}
