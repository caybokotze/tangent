import * as TooltipPrimitive from '@radix-ui/react-tooltip';
import { useMemo, useState } from 'react';
import { ExplorerProvider } from './app/context';
import { DetailPanel } from './components/DetailPanel';
import { Landing } from './components/Landing';
import { Shell, useTheme } from './components/Shell';
import { buildIndex } from './graph/derive';
import { collectWarnings } from './graph/parse';
import { useGraphSource } from './hooks/useGraphSource';
import { useRoute } from './lib/route';
import { CoverageView } from './views/CoverageView';
import { GraphView } from './views/GraphView';
import { IssuesView } from './views/IssuesView';
import { RulesView } from './views/RulesView';

export default function App(): JSX.Element {
  const source = useGraphSource();
  const { graph, loadFile } = source;
  const [route, navigate] = useRoute();
  const [dragging, setDragging] = useState(false);
  useTheme();

  const index = useMemo(() => (graph ? buildIndex(graph) : null), [graph]);
  const warnings = useMemo(() => (graph ? collectWarnings(graph) : []), [graph]);

  const onDrop = (event: React.DragEvent): void => {
    event.preventDefault();
    setDragging(false);
    const file = event.dataTransfer.files[0];
    if (file) void loadFile(file);
  };

  if (!graph || !index) {
    return (
      <div className="h-full overflow-y-auto" onDragOver={(e) => e.preventDefault()} onDrop={onDrop}>
        <Landing
          loading={source.loading}
          error={source.error}
          onLoadFile={(file) => void loadFile(file)}
          onLoadUrl={(url) => void source.loadUrl(url)}
        />
      </div>
    );
  }

  return (
    <TooltipPrimitive.Provider>
      <ExplorerProvider index={index} route={route} navigate={navigate}>
        <div
          className="relative h-full"
          onDragOver={(event) => {
            event.preventDefault();
            setDragging(true);
          }}
          onDragLeave={(event) => {
            if (event.currentTarget === event.target) setDragging(false);
          }}
          onDrop={onDrop}
        >
          <Shell
            sourceName={source.sourceName}
            onLoadFile={(file) => void loadFile(file)}
            onClose={() => {
              source.clear();
              navigate({ selection: null });
            }}
            warnings={warnings}
            panel={route.selection ? <DetailPanel key={`${route.selection.kind}:${route.selection.id}`} selection={route.selection} /> : null}
          >
            {route.view === 'issues' && <IssuesView />}
            {route.view === 'rules' && <RulesView />}
            {route.view === 'coverage' && <CoverageView />}
            {route.view === 'graph' && <GraphView />}
          </Shell>
          {dragging && (
            <div className="pointer-events-none absolute inset-0 z-50 flex items-center justify-center bg-bg/80 text-lg font-semibold text-fg backdrop-blur-sm">
              Drop tangent.graph.json to open it
            </div>
          )}
        </div>
      </ExplorerProvider>
    </TooltipPrimitive.Provider>
  );
}
