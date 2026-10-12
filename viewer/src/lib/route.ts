import { useCallback, useEffect, useState } from 'react';

export type View = 'issues' | 'rules' | 'coverage' | 'graph';

export type SelectionKind = 'type' | 'rule' | 'file' | 'contract' | 'issue' | 'layer';

/** What the detail panel shows. `issue` is a rule id seen from the Issues view. */
export type Selection = { kind: SelectionKind; id: string };

export type Route = {
  view: View;
  selection: Selection | null;
  /** Graph root spec: `type:<id>`, `contract:<id>` or `layer:<id>`; null for the default. */
  root: string | null;
  depth: number;
  direction: 'out' | 'in' | 'both';
};

const VIEWS: View[] = ['issues', 'rules', 'coverage', 'graph'];
const KINDS: SelectionKind[] = ['type', 'rule', 'file', 'contract', 'issue', 'layer'];

export const DEFAULT_ROUTE: Route = { view: 'issues', selection: null, root: null, depth: 1, direction: 'both' };

/** `#/graph?sel=type:A.B&root=contract:Controller&depth=2&dir=out` -> Route. */
export function parseHash(hash: string): Route {
  const trimmed = hash.replace(/^#\/?/, '');
  const [path = '', query = ''] = trimmed.split('?', 2);
  const params = new URLSearchParams(query);

  const view = (VIEWS as string[]).includes(path) ? (path as View) : DEFAULT_ROUTE.view;

  let selection: Selection | null = null;
  const sel = params.get('sel');
  if (sel) {
    const colon = sel.indexOf(':');
    const kind = sel.slice(0, colon) as SelectionKind;
    if (colon > 0 && KINDS.includes(kind)) selection = { kind, id: sel.slice(colon + 1) };
  }

  const depth = Number(params.get('depth'));
  const dir = params.get('dir');

  return {
    view,
    selection,
    root: params.get('root'),
    depth: Number.isInteger(depth) && depth >= 1 && depth <= 4 ? depth : DEFAULT_ROUTE.depth,
    direction: dir === 'out' || dir === 'in' || dir === 'both' ? dir : DEFAULT_ROUTE.direction,
  };
}

export function formatHash(route: Route): string {
  const params = new URLSearchParams();
  if (route.selection) params.set('sel', `${route.selection.kind}:${route.selection.id}`);
  if (route.view === 'graph') {
    if (route.root) params.set('root', route.root);
    if (route.depth !== DEFAULT_ROUTE.depth) params.set('depth', String(route.depth));
    if (route.direction !== DEFAULT_ROUTE.direction) params.set('dir', route.direction);
  }
  const query = params.toString();
  return `#/${route.view}${query ? `?${query}` : ''}`;
}

/**
 * The route lives in `location.hash`, so every click is a history entry, the
 * back button works, and a view can be shared as a link. `?graph=` stays in
 * the search part and is not touched.
 */
export function useRoute(): [Route, (next: Partial<Route>) => void] {
  const [route, setRoute] = useState<Route>(() => parseHash(window.location.hash));

  useEffect(() => {
    const onChange = (): void => setRoute(parseHash(window.location.hash));
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);

  const navigate = useCallback((next: Partial<Route>) => {
    const merged = { ...parseHash(window.location.hash), ...next };
    const hash = formatHash(merged);
    if (hash !== window.location.hash) window.location.hash = hash;
    else setRoute(merged);
  }, []);

  return [route, navigate];
}
