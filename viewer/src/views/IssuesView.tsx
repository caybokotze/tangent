import {
  createColumnHelper,
  flexRender,
  getCoreRowModel,
  getExpandedRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
  type ExpandedState,
  type SortingState,
} from '@tanstack/react-table';
import { ArrowDown, ArrowUp, ChevronRight, PartyPopper, Search } from 'lucide-react';
import { Fragment, useMemo, useState } from 'react';
import { useExplorer } from '../app/context';
import { InlineCode } from '../components/CodeFrame';
import { FileLink, TypeLink } from '../components/links';
import { Badge, ContractChip, EmptyState, Segmented, SeverityBar, SeverityIcon, Stat, Tooltip } from '../components/ui';
import { LAYERS_CONTRACT, cleanMessage, type IssueGroup } from '../graph/explore';
import { cn, plural } from '../lib/cn';

const column = createColumnHelper<IssueGroup>();
const TIER_LABEL = ['symbol', 'reference', 'flow'];

export function IssuesView(): JSX.Element {
  const { index, issues, contracts, projects, route, select } = useExplorer();
  const [sorting, setSorting] = useState<SortingState>([]);
  const [expanded, setExpanded] = useState<ExpandedState>({});
  const [search, setSearch] = useState('');
  const [contract, setContract] = useState<string>('all');

  const colorOf = (id: string): string =>
    id === LAYERS_CONTRACT ? 'var(--danger)' : contracts.find((c) => c.id === id)?.color ?? 'var(--faint)';

  const rows = useMemo(
    () => (contract === 'all' ? issues : issues.filter((issue) => issue.contract === contract)),
    [issues, contract],
  );

  const columns = useMemo(
    () => [
      column.display({
        id: 'issue',
        header: 'Issue',
        cell: ({ row }) => {
          const issue = row.original;
          return (
            <div className="flex min-w-0 items-start gap-2.5">
              <ChevronRight
                className={cn('mt-0.5 size-4 shrink-0 text-faint transition-transform', row.getIsExpanded() && 'rotate-90')}
              />
              <SeverityIcon severity={issue.severity} className="mt-0.5" />
              <div className="min-w-0 flex-1">
                <div className="flex min-w-0 items-center gap-2">
                  <span className="text-[13px] font-semibold break-words text-fg">
                    {issue.contract === LAYERS_CONTRACT ? issue.message : <InlineCode code={issue.title} lang="tangent" />}
                  </span>
                </div>
                <div className="mt-0.5 truncate text-muted">
                  {issue.contract === LAYERS_CONTRACT ? 'Layer dependency rule' : cleanMessage(issue.message)}
                </div>
                <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
                  <ContractChip id={issue.contract} color={colorOf(issue.contract)} />
                  {issue.codes.map((code) => (
                    <Badge key={code} mono>
                      {code}
                    </Badge>
                  ))}
                  <span className="font-mono text-[11px] text-faint">{issue.ruleId}</span>
                  {issue.source && <span className="font-mono text-[11px] text-faint">· {issue.source.file}:{issue.source.line}</span>}
                </div>
              </div>
            </div>
          );
        },
      }),
      column.accessor((issue) => (issue.tier === null ? -1 : issue.tier), {
        id: 'tier',
        header: 'Tier',
        cell: ({ getValue }) => {
          const tier = getValue();
          return tier < 0 ? <span className="text-faint">–</span> : <Badge>{TIER_LABEL[tier] ?? tier}</Badge>;
        },
      }),
      column.accessor((issue) => issue.types.length, {
        id: 'types',
        header: 'Types',
        cell: ({ getValue }) => <span className="tabular-nums">{getValue()}</span>,
      }),
      column.accessor((issue) => issue.occurrences.length, {
        id: 'events',
        header: 'Events',
        cell: ({ getValue }) => <span className="font-semibold tabular-nums">{getValue()}</span>,
      }),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [contracts],
  );

  const table = useReactTable({
    data: rows,
    columns,
    state: { sorting, expanded, globalFilter: search },
    onSortingChange: setSorting,
    onExpandedChange: setExpanded,
    onGlobalFilterChange: setSearch,
    globalFilterFn: (row, _column, value: string) => {
      const needle = value.trim().toLowerCase();
      if (needle === '') return true;
      const issue = row.original;
      return [issue.title, issue.message, issue.ruleId, issue.contract, ...issue.codes, ...issue.types, ...issue.files]
        .some((text) => text.toLowerCase().includes(needle));
    },
    getRowCanExpand: () => true,
    getRowId: (issue) => issue.ruleId,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getExpandedRowModel: getExpandedRowModel(),
  });

  const graph = index.graph;
  const affected = new Set(graph.violations.map((v) => v.node)).size;
  const types = projects.reduce((sum, p) => sum + p.types, 0);
  const bound = projects.reduce((sum, p) => sum + p.boundTypes, 0);
  const selectedRule = route.selection?.kind === 'issue' ? route.selection.id : null;

  const contractOptions = [
    { value: 'all', label: 'All', count: issues.length },
    ...[...new Set(issues.map((i) => i.contract))].map((id) => ({
      value: id,
      label: id,
      count: issues.filter((i) => i.contract === id).length,
    })),
  ];

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-5 px-6 py-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-fg">Issues</h1>
          <p className="mt-1 text-muted">
            Contract violations grouped by rule. A rule broken in many places is one issue with many events.
          </p>
        </div>
      </header>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Stat label="Violations" value={graph.violations.length} tone={graph.violations.length > 0 ? 'danger' : 'success'} hint={`${plural(issues.length, 'rule')} broken`} />
        <Stat label="Types affected" value={affected} hint={`in ${plural(new Set(graph.violations.map((v) => v.file)).size, 'file')}`} />
        <Stat label="Ignored" value={(graph.ignored ?? []).length} hint="violations ignored with a reason" />
        <Stat
          label="Contract coverage"
          value={types > 0 ? `${Math.round((bound / types) * 100)}%` : '–'}
          tone={types > 0 && bound / types >= 0.5 ? 'success' : 'warning'}
          hint={types > 0 ? `${bound} of ${types} types bound` : 'no file table in this export'}
        />
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <label className="relative min-w-60 flex-1">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-faint" />
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Search rules, types, files, codes…"
            className="h-8 w-full rounded-md border border-line bg-surface pr-3 pl-8 text-fg placeholder:text-faint focus:border-accent focus:outline-none"
          />
        </label>
        <Segmented label="Contract" value={contract} options={contractOptions} onChange={setContract} />
      </div>

      {issues.length === 0 ? (
        <div className="rounded-lg border border-line bg-surface">
          <EmptyState icon={<PartyPopper className="size-8" />} title="No violations">
            Every bound type satisfies its contracts. See Coverage for the files no contract covers yet.
          </EmptyState>
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-line bg-surface">
          <table className="w-full table-fixed border-collapse">
            <colgroup>
              <col />
              <col className="w-28" />
              <col className="w-20" />
              <col className="w-20" />
            </colgroup>
            <thead>
              {table.getHeaderGroups().map((group) => (
                <tr key={group.id} className="border-b border-line bg-surface-2">
                  {group.headers.map((header) => {
                    const sorted = header.column.getIsSorted();
                    const sortable = header.column.getCanSort() && header.id !== 'issue';
                    return (
                      <th
                        key={header.id}
                        className={cn(
                          'px-3 py-2 text-left text-[11px] font-semibold tracking-wider text-faint uppercase',
                          header.id !== 'issue' && 'text-right',
                        )}
                      >
                        {sortable ? (
                          <button
                            type="button"
                            className="inline-flex items-center gap-1 hover:text-fg"
                            onClick={header.column.getToggleSortingHandler()}
                          >
                            {flexRender(header.column.columnDef.header, header.getContext())}
                            {sorted === 'asc' ? <ArrowUp className="size-3" /> : sorted === 'desc' ? <ArrowDown className="size-3" /> : null}
                          </button>
                        ) : (
                          flexRender(header.column.columnDef.header, header.getContext())
                        )}
                      </th>
                    );
                  })}
                </tr>
              ))}
            </thead>
            <tbody>
              {table.getRowModel().rows.map((row) => (
                <Fragment key={row.id}>
                  <tr
                    className={cn(
                      'relative cursor-pointer border-b border-line transition-colors hover:bg-surface-2',
                      selectedRule === row.original.ruleId && 'bg-accent-soft hover:bg-accent-soft',
                    )}
                    onClick={() => {
                      row.toggleExpanded(true);
                      select({ kind: 'issue', id: row.original.ruleId });
                    }}
                  >
                    {row.getVisibleCells().map((cell, i) => (
                      <td key={cell.id} className={cn('relative px-3 py-3 align-top', i > 0 && 'text-right')}>
                        {i === 0 && <SeverityBar severity={row.original.severity} />}
                        {i === 0 ? (
                          <div
                            onClick={(event) => {
                              event.stopPropagation();
                              row.toggleExpanded();
                              select({ kind: 'issue', id: row.original.ruleId });
                            }}
                          >
                            {flexRender(cell.column.columnDef.cell, cell.getContext())}
                          </div>
                        ) : (
                          flexRender(cell.column.columnDef.cell, cell.getContext())
                        )}
                      </td>
                    ))}
                  </tr>
                  {row.getIsExpanded() && (
                    <tr className="border-b border-line bg-bg/60">
                      <td colSpan={4} className="px-3 py-2 pl-14">
                        <Occurrences issue={row.original} />
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
              {table.getRowModel().rows.length === 0 && (
                <tr>
                  <td colSpan={4}>
                    <EmptyState title="Nothing matches">Try a different search or contract.</EmptyState>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function Occurrences(props: { issue: IssueGroup }): JSX.Element {
  const { select } = useExplorer();
  return (
    <ul className="divide-y divide-line">
      {props.issue.occurrences.map((violation, i) => (
        <li key={i} className="flex flex-wrap items-center gap-x-4 gap-y-1 py-2">
          <TypeLink id={violation.node} />
          {violation.member && <Badge mono>{violation.member}</Badge>}
          <FileLink path={violation.file} line={violation.line} className="min-w-0" />
          <span className="ml-auto flex items-center gap-2">
            <Tooltip content="Open this occurrence">
              <button
                type="button"
                className="text-xs font-medium text-accent-strong hover:underline"
                onClick={() => select({ kind: 'issue', id: props.issue.ruleId })}
              >
                Details
              </button>
            </Tooltip>
          </span>
        </li>
      ))}
    </ul>
  );
}
