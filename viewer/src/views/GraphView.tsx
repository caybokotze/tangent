import { Search } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useExplorer } from '../app/context';
import { GraphCanvas } from '../components/graph/GraphCanvas';
import { Button, Segmented, Tooltip } from '../components/ui';
import { visibleEdges } from '../graph/derive';
import { neighbourhood, rootsOf, type Direction } from '../graph/explore';
import { computeLayout, loadElk, type ElkLike, type GraphLayout } from '../graph/layout';
import { plural, shortName } from '../lib/cn';

const CAP_STEP = 150;

let elkPromise: Promise<ElkLike | null> | null = null;
function elk(): Promise<ElkLike | null> {
  elkPromise ??= loadElk();
  return elkPromise;
}

export function GraphView(): JSX.Element {
  const { index, contracts, route, navigate, select } = useExplorer();
  const [showUnbound, setShowUnbound] = useState(true);
  const [cap, setCap] = useState(CAP_STEP);
  const [search, setSearch] = useState('');
  const [layout, setLayout] = useState<GraphLayout | null>(null);

  const roots = useMemo(() => rootsOf(index, route.root), [index, route.root]);
  const rootSet = useMemo(() => new Set(roots), [roots]);

  const hood = useMemo(
    () =>
      neighbourhood(index, roots, route.depth, route.direction, cap, (node) =>
        showUnbound ? true : node.contracts.length > 0 || rootSet.has(node.id),
      ),
    [index, roots, rootSet, route.depth, route.direction, cap, showUnbound],
  );

  const nodes = useMemo(() => index.graph.nodes.filter((n) => hood.ids.has(n.id)), [index, hood]);
  const edges = useMemo(() => visibleEdges(index.graph, hood.ids), [index, hood]);
  const key = useMemo(() => nodes.map((n) => n.id).join('|'), [nodes]);

  useEffect(() => {
    setCap(CAP_STEP);
  }, [route.root, route.depth, route.direction]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const next = await computeLayout({
        layers: index.graph.layers,
        nodes,
        edges,
        elk: await elk(),
        options: { hideEmptyLanes: true },
      });
      if (!cancelled) setLayout(next);
    })();
    return () => {
      cancelled = true;
    };
    // `key` stands in for `nodes`: the layout depends only on which nodes are drawn.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [index, key]);

  const selectedId = route.selection?.kind === 'type' ? route.selection.id : null;
  const rootValue = route.root ?? '';
  const isTypeRoot = rootValue.startsWith('type:');

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-wrap items-center gap-3 border-b border-line bg-surface/60 px-4 py-2.5">
        <label className="flex items-center gap-2 text-xs text-muted">
          Root
          <select
            value={rootValue}
            onChange={(event) => navigate({ root: event.target.value === '' ? null : event.target.value })}
            className="h-7 max-w-64 rounded-md border border-line bg-surface-2 px-2 text-xs text-fg focus:border-accent focus:outline-none"
          >
            <option value="">Types with violations</option>
            {isTypeRoot && <option value={rootValue}>Type: {shortName(rootValue.slice(5))}</option>}
            <optgroup label="Contract">
              {contracts.map((c) => (
                <option key={c.id} value={`contract:${c.id}`}>
                  {c.id} ({c.types.length})
                </option>
              ))}
            </optgroup>
            <optgroup label="Layer">
              {index.graph.layers.map((l) => (
                <option key={l.id} value={`layer:${l.id}`}>
                  {l.id}
                </option>
              ))}
            </optgroup>
          </select>
        </label>

        <Segmented
          label="Depth"
          value={String(route.depth)}
          onChange={(value) => navigate({ depth: Number(value) })}
          options={[1, 2, 3, 4].map((d) => ({ value: String(d), label: `${d} hop${d > 1 ? 's' : ''}` }))}
        />

        <Segmented<Direction>
          label="Direction"
          value={route.direction}
          onChange={(direction) => navigate({ direction })}
          options={[
            { value: 'out', label: 'Depends on' },
            { value: 'in', label: 'Used by' },
            { value: 'both', label: 'Both' },
          ]}
        />

        <label className="flex items-center gap-1.5 text-xs text-muted">
          <input type="checkbox" checked={showUnbound} onChange={(e) => setShowUnbound(e.target.checked)} className="accent-[var(--accent)]" />
          Unbound types
        </label>

        <label className="relative ml-auto">
          <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-faint" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Highlight type…"
            className="h-7 w-44 rounded-md border border-line bg-surface-2 pr-2 pl-7 text-xs text-fg placeholder:text-faint focus:border-accent focus:outline-none"
          />
        </label>
      </div>

      <div className="flex items-center gap-3 border-b border-line px-4 py-1.5 text-xs text-muted">
        <span>
          {plural(nodes.length, 'type')} · {plural(edges.length, 'edge')}
          {edges.some((e) => e.violatesLayering) && (
            <span className="text-danger"> · {plural(edges.filter((e) => e.violatesLayering).length, 'illegal edge')}</span>
          )}
        </span>
        {hood.truncated > 0 && (
          <span className="flex items-center gap-2">
            <span className="text-warning">{hood.truncated} more within {plural(route.depth, 'hop')}</span>
            <Button size="sm" onClick={() => setCap((c) => c + CAP_STEP)}>
              Expand
            </Button>
          </span>
        )}
        <span className="ml-auto text-faint">Click a type to inspect it · double-click to make it the root</span>
      </div>

      <div className="relative min-h-0 flex-1">
        {roots.length === 0 ? (
          <div className="flex h-full items-center justify-center text-muted">The root matches no type in this graph.</div>
        ) : layout ? (
          <GraphCanvas
            index={index}
            layout={layout}
            nodes={nodes}
            edges={edges}
            roots={rootSet}
            selectedId={selectedId}
            search={search}
            onSelect={(id) => select(id === null ? null : { kind: 'type', id })}
            onReroot={(id) => navigate({ root: `type:${id}`, selection: { kind: 'type', id } })}
          />
        ) : (
          <div className="flex h-full items-center justify-center text-muted">Laying out {plural(nodes.length, 'type')}…</div>
        )}

        <div className="pointer-events-none absolute top-3 left-3 flex max-w-xs flex-wrap gap-1.5">
          {contracts
            .filter((c) => nodes.some((n) => n.contracts.includes(c.id)))
            .map((c) => (
              <Tooltip key={c.id} content={`${c.types.length} bound types`}>
                <span className="pointer-events-auto inline-flex items-center gap-1.5 rounded-md border border-line bg-surface/90 px-2 py-0.5 text-[11px] text-muted backdrop-blur">
                  <span className="size-2 rounded-full" style={{ background: c.color }} />
                  {c.id}
                </span>
              </Tooltip>
            ))}
        </div>
      </div>
    </div>
  );
}
