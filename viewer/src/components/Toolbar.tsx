import { useRef } from 'react';
import type { Filters } from '../graph/derive';
import type { GraphStats } from '../graph/derive';
import type { TangentGraph } from '../graph/types';

export type ViewMode = 'graph' | 'contract';

export type ToolbarProps = {
  graph: TangentGraph;
  stats: GraphStats;
  sourceName: string | null;
  view: ViewMode;
  filters: Filters;
  onViewChange: (view: ViewMode) => void;
  onFiltersChange: (filters: Filters) => void;
  onLoadFile: (file: File) => void;
  onClear: () => void;
};

export function Toolbar({
  graph,
  stats,
  sourceName,
  view,
  filters,
  onViewChange,
  onFiltersChange,
  onLoadFile,
  onClear,
}: ToolbarProps): JSX.Element {
  const fileInput = useRef<HTMLInputElement>(null);
  const set = (patch: Partial<Filters>): void => onFiltersChange({ ...filters, ...patch });

  return (
    <div className="toolbar">
      <div className="brand">
        <strong>Tangent</strong>
        <span title={sourceName ?? undefined}>
          {graph.project}
          {graph.generatedAt !== '' ? ` · ${graph.generatedAt}` : ''}
        </span>
      </div>

      <div className="segmented" role="group" aria-label="View">
        <button type="button" aria-pressed={view === 'graph'} onClick={() => onViewChange('graph')}>
          Graph
        </button>
        <button
          type="button"
          aria-pressed={view === 'contract'}
          onClick={() => onViewChange('contract')}
          disabled={graph.contracts.length === 0}
        >
          Contracts
        </button>
      </div>

      <div className="group">
        <input
          type="search"
          placeholder="Search type name"
          aria-label="Search type name"
          value={filters.search}
          onChange={(event) => set({ search: event.target.value })}
          size={18}
        />
      </div>

      <div className="group">
        <button
          type="button"
          className="toggle"
          aria-pressed={filters.focusMode}
          disabled={filters.selectedNodeId === null}
          title="Show only the selected type and its direct neighbours"
          onClick={() => set({ focusMode: !filters.focusMode })}
        >
          Focus
        </button>
        <label className="check">
          <input
            type="checkbox"
            checked={filters.showUnbound}
            onChange={(event) => set({ showUnbound: event.target.checked })}
          />
          Unbound types
        </label>
        <label className="check">
          <input
            type="checkbox"
            checked={filters.onlyViolations}
            onChange={(event) => set({ onlyViolations: event.target.checked })}
          />
          Only violations
        </label>
      </div>

      <div className="spacer" />

      <div className="stats" title="Totals for the whole graph">
        <span>
          <b>{stats.nodes}</b> types
        </span>
        <span>
          <b>{stats.edges}</b> edges
        </span>
        <span>
          <b>{stats.violations}</b> violations
        </span>
        <span>
          <b>{stats.layeringViolations}</b> illegal
        </span>
        <span>
          <b>{stats.exemptions}</b> exempt
        </span>
      </div>

      <div className="group">
        <input
          ref={fileInput}
          type="file"
          accept="application/json,.json"
          hidden
          onChange={(event) => {
            const file = event.target.files?.[0];
            if (file) onLoadFile(file);
            event.target.value = '';
          }}
        />
        <button type="button" onClick={() => fileInput.current?.click()}>
          Open...
        </button>
        <button type="button" onClick={onClear} title="Unload the current graph">
          Close
        </button>
      </div>
    </div>
  );
}
