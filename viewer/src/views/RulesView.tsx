import { ArrowRight, ChevronRight, Layers3 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useExplorer } from '../app/context';
import { InlineCode } from '../components/CodeFrame';
import { FileLink, TypeLink } from '../components/links';
import { Badge, ContractChip, EmptyState, PassBar, RuleStatusIcon, SectionTitle } from '../components/ui';
import { LAYERS_CONTRACT, cleanMessage, type ContractSummary, type RuleSummary } from '../graph/explore';
import { baseName, cn, plural } from '../lib/cn';

const TIER_LABEL = ['symbol', 'reference', 'flow'];

export function RulesView(): JSX.Element {
  const { contracts, route } = useExplorer();

  const initial =
    route.selection?.kind === 'contract'
      ? route.selection.id
      : contracts.find((c) => c.violations > 0)?.id ?? contracts[0]?.id ?? LAYERS_CONTRACT;
  const [active, setActive] = useState(initial);

  // Follow the panel: selecting a contract or a rule elsewhere opens it here.
  useEffect(() => {
    const sel = route.selection;
    if (sel?.kind === 'contract') setActive(sel.id);
    if (sel?.kind === 'rule' || sel?.kind === 'issue') {
      const owner = contracts.find((c) => c.rules.some((r) => r.rule.id === sel.id));
      if (owner) setActive(owner.id);
      else if (sel.id.startsWith('layer:')) setActive(LAYERS_CONTRACT);
    }
  }, [route.selection, contracts]);

  const contract = contracts.find((c) => c.id === active);

  return (
    <div className="flex h-full min-h-0">
      <nav className="w-52 shrink-0 overflow-y-auto border-r border-line bg-surface/40 p-3" aria-label="Contracts">
        <SectionTitle>Contracts</SectionTitle>
        <ul className="flex flex-col gap-0.5">
          {contracts.map((c) => (
            <li key={c.id}>
              <button
                type="button"
                onClick={() => setActive(c.id)}
                className={cn(
                  'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left transition-colors',
                  active === c.id ? 'bg-surface-3 text-fg' : 'text-muted hover:bg-surface-2 hover:text-fg',
                )}
              >
                <span className="size-2.5 shrink-0 rounded-full" style={{ background: c.color }} />
                <span className="min-w-0 flex-1 truncate font-medium">{c.id}</span>
                {c.violations > 0 ? (
                  <Badge tone="danger">{c.violations}</Badge>
                ) : (
                  <span className="text-[11px] text-faint tabular-nums">{c.types.length}</span>
                )}
              </button>
            </li>
          ))}
          <li className="mt-2 border-t border-line pt-2">
            <button
              type="button"
              onClick={() => setActive(LAYERS_CONTRACT)}
              className={cn(
                'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left transition-colors',
                active === LAYERS_CONTRACT ? 'bg-surface-3 text-fg' : 'text-muted hover:bg-surface-2 hover:text-fg',
              )}
            >
              <Layers3 className="size-3.5 shrink-0" />
              <span className="min-w-0 flex-1 truncate font-medium">Layers</span>
            </button>
          </li>
        </ul>
      </nav>

      <div className="min-w-0 flex-1 overflow-y-auto">
        {active === LAYERS_CONTRACT ? <LayerRules /> : contract ? <ContractRules contract={contract} /> : null}
      </div>
    </div>
  );
}

function ContractRules(props: { contract: ContractSummary }): JSX.Element {
  const { contract } = props;
  const { select } = useExplorer();

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5 px-6 py-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="size-3 rounded-full" style={{ background: contract.color }} />
            <button
              type="button"
              className="text-xl font-semibold text-fg hover:text-accent-strong"
              onClick={() => select({ kind: 'contract', id: contract.id })}
            >
              {contract.id}
            </button>
            {contract.extends && (
              <span className="text-muted">
                extends <ContractChip id={contract.extends} color="var(--faint)" onClick={() => select({ kind: 'contract', id: contract.extends ?? '' })} />
              </span>
            )}
          </div>
          <div className="mt-1 flex items-center gap-3 text-muted">
            <FileLink path={contract.file} line={contract.line} />
            <span>{plural(contract.rules.length, 'rule')}</span>
            <span>applies to {plural(contract.types.length, 'type')}</span>
          </div>
        </div>
        <div className="flex gap-2">
          <Badge tone={contract.failingTypes > 0 ? 'danger' : 'success'}>
            {contract.failingTypes > 0 ? `${contract.failingTypes} failing` : 'all passing'}
          </Badge>
        </div>
      </header>

      <section>
        <SectionTitle>Rules</SectionTitle>
        {contract.rules.length === 0 ? (
          <div className="rounded-lg border border-line bg-surface">
            <EmptyState title="No rules of its own">
              {contract.extends ? `Every rule is inherited from ${contract.extends}.` : 'This contract declares no rules.'}
            </EmptyState>
          </div>
        ) : (
          <ul className="overflow-hidden rounded-lg border border-line bg-surface">
            {contract.rules.map((rule) => (
              <RuleRow key={rule.rule.id} summary={rule} />
            ))}
          </ul>
        )}
      </section>

      <section>
        <SectionTitle>Bound types</SectionTitle>
        <div className="flex flex-wrap gap-2 rounded-lg border border-line bg-surface p-3">
          {contract.types.length === 0 && <span className="text-muted">No type is bound to this contract.</span>}
          {contract.types.map((node) => (
            <span key={node.id} className="inline-flex items-center gap-1.5 rounded-md border border-line bg-surface-2 px-2 py-1">
              <TypeLink id={node.id} />
              {node.violations > 0 && <Badge tone="danger">{node.violations}</Badge>}
            </span>
          ))}
        </div>
      </section>
    </div>
  );
}

function RuleRow(props: { summary: RuleSummary }): JSX.Element {
  const { summary } = props;
  const { route, select } = useExplorer();
  const [open, setOpen] = useState(summary.failing > 0);
  const selected = route.selection?.kind === 'rule' && route.selection.id === summary.rule.id;

  return (
    <li className="border-b border-line last:border-b-0">
      <div
        className={cn('flex cursor-pointer items-start gap-3 px-3 py-2.5 hover:bg-surface-2', selected && 'bg-accent-soft hover:bg-accent-soft')}
        onClick={() => {
          setOpen((value) => !value);
          select({ kind: 'rule', id: summary.rule.id });
        }}
      >
        <ChevronRight className={cn('mt-0.5 size-4 shrink-0 text-faint transition-transform', open && 'rotate-90')} />
        <div className="min-w-0 flex-1">
          <InlineCode code={summary.rule.text} lang="tangent" className="block text-[13px] break-words whitespace-pre-wrap" />
          <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1">
            <span className="font-mono text-[11px] text-faint">{summary.rule.id}</span>
            <Badge>{TIER_LABEL[summary.rule.tier] ?? summary.rule.tier}</Badge>
            <PassBar passing={summary.passing} failing={summary.failing} exempt={summary.exempt} />
            <span className="text-xs text-muted tabular-nums">
              {summary.failing > 0 ? <span className="font-semibold text-danger">{summary.failing} fail</span> : null}
              {summary.failing > 0 ? ' · ' : ''}
              {summary.passing} pass
              {summary.exempt > 0 && <span className="text-faint"> · {summary.exempt} exempt</span>}
            </span>
          </div>
        </div>
      </div>
      {open && (
        <ul className="border-t border-line bg-bg/60 py-1 pr-3 pl-10">
          {summary.applications.length === 0 && <li className="py-2 text-muted">No bound type.</li>}
          {summary.applications.map((application) => (
            <li key={application.node.id} className="flex items-start gap-3 py-1.5">
              <RuleStatusIcon status={application.status} className="mt-0.5" />
              <div className="min-w-0 flex-1">
                <TypeLink id={application.node.id} />
                {application.status === 'fail' && (
                  <div className="text-xs text-muted">{cleanMessage(application.violations[0]?.message ?? '')}</div>
                )}
                {application.status === 'exempt' && (
                  <div className="text-xs text-faint italic">exempt: {application.exemptions[0]?.reason}</div>
                )}
                {application.violations[0] && (
                  <FileLink path={application.violations[0].file} line={application.violations[0].line} className="mt-0.5 max-w-full">
                    {baseName(application.violations[0].file)}:{application.violations[0].line}
                  </FileLink>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}

function LayerRules(): JSX.Element {
  const { layers, select } = useExplorer();
  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5 px-6 py-6">
      <header>
        <h1 className="text-xl font-semibold text-fg">Layers</h1>
        <p className="mt-1 text-muted">
          What each layer may depend on. Every other dependency between layers is a violation.
        </p>
      </header>
      {layers.length === 0 && (
        <div className="rounded-lg border border-line bg-surface">
          <EmptyState title="No layers declared">Add a project block with layers to your .tangent folder.</EmptyState>
        </div>
      )}
      <ul className="overflow-hidden rounded-lg border border-line bg-surface">
        {layers.map((layer) => (
          <li key={layer.from} className="border-b border-line last:border-b-0">
            <button
              type="button"
              className="flex w-full items-center gap-3 px-4 py-3 text-left hover:bg-surface-2"
              onClick={() => select({ kind: 'layer', id: layer.from })}
            >
              <span className="w-36 shrink-0 font-semibold text-fg">{layer.from}</span>
              <ArrowRight className="size-4 shrink-0 text-faint" />
              <span className="flex min-w-0 flex-1 flex-wrap gap-1.5">
                {layer.allowed.length === 0 ? (
                  <Badge>nothing</Badge>
                ) : (
                  layer.allowed.map((target) => <Badge key={target} tone="accent">{target}</Badge>)
                )}
              </span>
              <span className="text-xs text-muted tabular-nums">{plural(layer.types.length, 'type')}</span>
              {layer.violations.length > 0 ? (
                <Badge tone="danger">{plural(layer.violations.length, 'violation')}</Badge>
              ) : (
                <Badge tone="success">clean</Badge>
              )}
            </button>
            {layer.violations.length > 0 && (
              <ul className="border-t border-line bg-bg/60 py-1 pr-4 pl-12">
                {layer.violations.map((violation, i) => (
                  <li key={i} className="flex items-start gap-3 py-1.5">
                    <RuleStatusIcon status="fail" className="mt-0.5" />
                    <div className="min-w-0 flex-1">
                      <TypeLink id={violation.node} />
                      <div className="text-xs text-muted">{cleanMessage(violation.message)}</div>
                      <FileLink path={violation.file} line={violation.line} className="mt-0.5 max-w-full">
                        {baseName(violation.file)}:{violation.line}
                      </FileLink>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
