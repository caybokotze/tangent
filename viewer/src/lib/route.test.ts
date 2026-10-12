import { describe, expect, it } from 'vitest';
import { DEFAULT_ROUTE, formatHash, parseHash } from './route';

describe('route', () => {
  it('defaults to the Issues view', () => {
    expect(parseHash('')).toEqual(DEFAULT_ROUTE);
    expect(parseHash('#/nowhere')).toEqual(DEFAULT_ROUTE);
  });

  it('reads the view and the selection', () => {
    const route = parseHash('#/rules?sel=rule%3AController%2Ftype%2Fmodifier%230');
    expect(route.view).toBe('rules');
    expect(route.selection).toEqual({ kind: 'rule', id: 'Controller/type/modifier#0' });
  });

  it('keeps colons inside the selected id', () => {
    expect(parseHash('#/issues?sel=issue:layer:Api->Infrastructure').selection).toEqual({
      kind: 'issue',
      id: 'layer:Api->Infrastructure',
    });
  });

  it('ignores an unknown selection kind and out-of-range graph settings', () => {
    const route = parseHash('#/graph?sel=planet:Mars&depth=9&dir=sideways');
    expect(route.selection).toBeNull();
    expect(route.depth).toBe(DEFAULT_ROUTE.depth);
    expect(route.direction).toBe(DEFAULT_ROUTE.direction);
  });

  it('round-trips a graph route', () => {
    const route = {
      view: 'graph' as const,
      selection: { kind: 'type' as const, id: 'Orders.Api.OrdersController' },
      root: 'contract:Controller',
      depth: 2,
      direction: 'out' as const,
    };
    expect(parseHash(formatHash(route))).toEqual(route);
  });

  it('drops graph settings outside the graph view', () => {
    expect(formatHash({ ...DEFAULT_ROUTE, view: 'coverage', root: 'layer:Domain', depth: 3 })).toBe('#/coverage');
  });
});
