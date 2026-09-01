import { useCallback, useEffect, useMemo, useState } from 'react';
import { ContractView } from './components/ContractView';
import { GraphCanvas } from './components/GraphCanvas';
import { Landing } from './components/Landing';
import { Legend } from './components/Legend';
import { SidePanel } from './components/SidePanel';
import { Toolbar, type ViewMode } from './components/Toolbar';
import { DEFAULT_FILTERS, buildIndex, graphStats, visibleEdges, visibleNodes } from './graph/derive';
import type { Filters } from './graph/derive';
import { computeLayout, loadElk } from './graph/layout';
import type { ElkLike, GraphLayout } from './graph/layout';
import { collectWarnings } from './graph/parse';
import { useGraphSource } from './hooks/useGraphSource';

/** elkjs is loaded once, lazily, and shared by every layout run. */
let elkPromise: Promise<ElkLike | null> | null = null;
function elk(): Promise<ElkLike | null> {
  elkPromise ??= loadElk();
  return elkPromise;
}

export default function App(): JSX.Element {
  const source = useGraphSource();
  const { graph, loadFile } = source;

  const [view, setView] = useState<ViewMode>('graph');
  const [filters, setFilters] = useState<Filters>(DEFAULT_FILTERS);
  const [contractId, setContractId] = useState<string | null>(null);
  const [layout, setLayout] = useState<GraphLayout | null>(null);
  const [dragging, setDragging] = useState(false);

  const index = useMemo(() => (graph ? buildIndex(graph) : null), [graph]);
  const stats = useMemo(() => (graph ? graphStats(graph) : null), [graph]);
  const warnings = useMemo(() => (graph ? collectWarnings(graph) : []), [graph]);

  // Reset the per-graph UI state whenever a new document is loaded.
  useEffect(() => {
    setFilters(DEFAULT_FILTERS);
    setLayout(null);
    setContractId(graph?.contracts[0]?.id ?? null);
    setView('graph');
  }, [graph]);

  const shown = useMemo(() => (index ? visibleNodes(index, filters) : []), [index, filters]);
  const shownIds = useMemo(() => new Set(shown.map((node) => node.id)), [shown]);
  const shownEdges = useMemo(
    () => (graph ? visibleEdges(graph, shownIds) : []),
    [graph, shownIds],
  );

  const layoutKey = useMemo(() => shown.map((node) => node.id).join('|'), [shown]);

  useEffect(() => {
    if (!graph) return;
    let cancelled = false;
    void (async () => {
      const engine = await elk();
      const next = await computeLayout({
        layers: graph.layers,
        nodes: shown,
        edges: shownEdges,
        elk: engine,
        // With filters on, an empty lane is just noise; unfiltered it documents
        // that the layer exists.
        options: { hideEmptyLanes: shown.length !== graph.nodes.length },
      });
      if (!cancelled) setLayout(next);
    })();
    return () => {
      cancelled = true;
    };
    // `layoutKey` stands in for `shown`: the layout only depends on which nodes are visible.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [graph, layoutKey]);

  const select = useCallback((nodeId: string | null) => {
    setFilters((current) => ({
      ...current,
      selectedNodeId: nodeId,
      focusMode: nodeId === null ? false : current.focusMode,
    }));
  }, []);

  const selectedNode =
    index && filters.selectedNodeId !== null ? index.nodesById.get(filters.selectedNodeId) : undefined;

  const onDrop = (event: React.DragEvent): void => {
    event.preventDefault();
    setDragging(false);
    const file = event.dataTransfer.files[0];
    if (file) void loadFile(file);
  };

  if (!graph || !index || !stats) {
    return (
      <div
        className="app"
        onDragOver={(event) => event.preventDefault()}
        onDrop={onDrop}
      >
        <Landing
          loading={source.loading}
          error={source.error}
          onLoadFile={(file) => void loadFile(file)}
          onLoadSample={() => void source.loadSample()}
        />
      </div>
    );
  }

  return (
    <div
      className="app"
      onDragOver={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={(event) => {
        if (event.currentTarget === event.target) setDragging(false);
      }}
      onDrop={onDrop}
    >
      <Toolbar
        graph={graph}
        stats={stats}
        sourceName={source.sourceName}
        view={view}
        filters={filters}
        onViewChange={setView}
        onFiltersChange={setFilters}
        onLoadFile={(file) => void loadFile(file)}
        onClear={source.clear}
      />

      {source.error !== null && <div className="banner">{source.error}</div>}
      {warnings.length > 0 && <div className="banner">{warnings.join(' · ')}</div>}

      <div className="app-body">
        <div className="app-main">
          {view === 'graph' ? (
            layout ? (
              <>
                <GraphCanvas
                  index={index}
                  layout={layout}
                  nodes={shown}
                  edges={shownEdges}
                  search={filters.search}
                  selectedNodeId={filters.selectedNodeId}
                  onSelect={select}
                />
                <Legend index={index} />
              </>
            ) : (
              <div className="landing">
                <p className="empty">Laying out {shown.length} types...</p>
              </div>
            )
          ) : (
            <ContractView
              index={index}
              contractId={contractId ?? graph.contracts[0]?.id ?? ''}
              onContractChange={setContractId}
              onSelect={select}
            />
          )}
        </div>

        {selectedNode && (
          <SidePanel
            index={index}
            node={selectedNode}
            onClose={() => select(null)}
            onSelect={select}
          />
        )}
      </div>

      {dragging && <div className="drop-overlay">Drop tangent.graph.json to load it</div>}
    </div>
  );
}
