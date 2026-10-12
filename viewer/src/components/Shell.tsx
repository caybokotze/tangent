import { AlertTriangle, FolderTree, Moon, Network, ScrollText, Sun, Upload, X } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useExplorer } from '../app/context';
import { cn, plural } from '../lib/cn';
import type { View } from '../lib/route';
import { Tooltip } from './ui';

function readTheme(): 'dark' | 'light' {
  try {
    return window.localStorage.getItem('tangent.theme') === 'light' ? 'light' : 'dark';
  } catch {
    return 'dark';
  }
}

export function useTheme(): ['dark' | 'light', () => void] {
  const [theme, setTheme] = useState(readTheme);
  useEffect(() => {
    document.documentElement.classList.toggle('light', theme === 'light');
    try {
      window.localStorage.setItem('tangent.theme', theme);
    } catch {
      // Storage can be unavailable (private mode); the theme then lasts for the session.
    }
  }, [theme]);
  return [theme, () => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))];
}

/** The Tangent mark: a line touching a curve at one point. */
export function Logo(props: { className?: string }): JSX.Element {
  return (
    <svg viewBox="0 0 32 32" className={props.className} aria-hidden>
      <path d="M5 25 C 9 9, 23 9, 27 25" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" opacity="0.55" />
      <path d="M3 13.2 L 29 13.2" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" />
      <circle cx="16" cy="13.2" r="3.2" fill="currentColor" />
    </svg>
  );
}

type NavItem = { view: View; label: string; icon: ReactNode; count?: number; tone?: 'danger' };

export function Shell(props: {
  sourceName: string | null;
  onLoadFile: (file: File) => void;
  onClose: () => void;
  warnings: string[];
  children: ReactNode;
  panel: ReactNode;
}): JSX.Element {
  const { index, issues, contracts, projects, route, navigate } = useExplorer();
  const [theme, toggleTheme] = useTheme();
  const input = useRef<HTMLInputElement>(null);
  const graph = index.graph;
  const untracked = projects.reduce((sum, p) => sum + p.untracked, 0);

  const nav: NavItem[] = [
    { view: 'issues', label: 'Issues', icon: <AlertTriangle className="size-4" />, count: graph.violations.length, tone: 'danger' },
    { view: 'rules', label: 'Rules', icon: <ScrollText className="size-4" />, count: contracts.length },
    { view: 'coverage', label: 'Coverage', icon: <FolderTree className="size-4" />, count: untracked },
    { view: 'graph', label: 'Graph', icon: <Network className="size-4" /> },
  ];

  return (
    <div className="flex h-full min-h-0">
      <aside className="flex w-56 shrink-0 flex-col border-r border-line bg-sidebar">
        <div className="flex h-14 items-center gap-2.5 px-4">
          <Logo className="size-7 text-accent" />
          <div className="min-w-0">
            <div className="text-[15px] leading-tight font-bold tracking-tight text-fg">Tangent</div>
            <div className="truncate text-[11px] leading-tight text-faint">architecture conformance</div>
          </div>
        </div>

        <div className="mx-3 mb-3 rounded-lg border border-line bg-surface px-3 py-2.5">
          <div className="text-[10px] font-semibold tracking-wider text-faint uppercase">Project</div>
          <div className="truncate font-semibold text-fg">{graph.project}</div>
          <div className="mt-0.5 truncate text-[11px] text-faint" title={props.sourceName ?? undefined}>
            {graph.generatedAt ? new Date(graph.generatedAt).toLocaleString() : props.sourceName}
          </div>
        </div>

        <nav className="flex flex-col gap-0.5 px-2" aria-label="Views">
          {nav.map((item) => {
            const active = route.view === item.view;
            return (
              <button
                key={item.view}
                type="button"
                onClick={() => navigate({ view: item.view })}
                aria-current={active ? 'page' : undefined}
                className={cn(
                  'relative flex h-9 items-center gap-2.5 rounded-md px-3 text-left font-medium transition-colors',
                  active ? 'bg-accent-soft text-fg' : 'text-muted hover:bg-surface-2 hover:text-fg',
                )}
              >
                {active && <span className="absolute inset-y-1.5 left-0 w-[3px] rounded-full bg-accent" />}
                <span className={active ? 'text-accent-strong' : ''}>{item.icon}</span>
                <span className="flex-1">{item.label}</span>
                {item.count !== undefined && item.count > 0 && (
                  <span
                    className={cn(
                      'rounded-full px-1.5 text-[11px] leading-5 font-semibold tabular-nums',
                      item.tone === 'danger' ? 'bg-danger text-white' : 'bg-surface-3 text-muted',
                    )}
                  >
                    {item.count}
                  </span>
                )}
              </button>
            );
          })}
        </nav>

        <div className="mt-4 px-5 text-[11px] leading-relaxed text-faint">
          {plural(graph.nodes.length, 'type')} · {plural(graph.edges.length, 'edge')}
          <br />
          {plural(issues.length, 'rule')} broken · {plural((graph.exemptions ?? []).length, 'exemption')}
        </div>

        <div className="mt-auto flex items-center gap-1 border-t border-line p-2">
          <input
            ref={input}
            type="file"
            accept=".json,application/json"
            className="hidden"
            onChange={(event) => {
              const file = event.target.files?.[0];
              if (file) props.onLoadFile(file);
              event.target.value = '';
            }}
          />
          <Tooltip content="Open another tangent.graph.json">
            <button type="button" onClick={() => input.current?.click()} className="flex h-8 flex-1 items-center gap-2 rounded-md px-2 text-muted hover:bg-surface-2 hover:text-fg">
              <Upload className="size-4" /> Open…
            </button>
          </Tooltip>
          <Tooltip content={theme === 'dark' ? 'Light theme' : 'Dark theme'}>
            <button type="button" onClick={toggleTheme} className="flex size-8 items-center justify-center rounded-md text-muted hover:bg-surface-2 hover:text-fg" aria-label="Toggle theme">
              {theme === 'dark' ? <Sun className="size-4" /> : <Moon className="size-4" />}
            </button>
          </Tooltip>
          <Tooltip content="Close this graph">
            <button type="button" onClick={props.onClose} className="flex size-8 items-center justify-center rounded-md text-muted hover:bg-surface-2 hover:text-fg" aria-label="Close graph">
              <X className="size-4" />
            </button>
          </Tooltip>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {props.warnings.length > 0 && (
          <div className="border-b border-warning/30 bg-warning-soft px-4 py-1.5 text-xs text-warning">{props.warnings.join(' · ')}</div>
        )}
        {!graph.root && graph.violations.length > 0 && graph.violations.every((v) => !v.snippet) && (
          <div className="border-b border-line bg-surface-2 px-4 py-1.5 text-xs text-muted">
            This export has no source. Re-run <code className="font-mono text-fg">tangent graph --include-source</code> to see the
            code next to each violation and open files in VS Code.
          </div>
        )}
        <div className="relative flex min-h-0 flex-1">
          <main className="min-w-0 flex-1 overflow-y-auto">{props.children}</main>
          {props.panel}
        </div>
      </div>
    </div>
  );
}
