import { describe, expect, it } from 'vitest';
import { DEFAULT_LAYOUT_OPTIONS, assignLanes, computeLayout, orderLayers } from '../layout';
import type { ElkGraph, ElkLike } from '../layout';
import { UNLAYERED } from '../types';
import type { Layer } from '../types';
import { makeGraph } from './fixtures';

const layer = (id: string, ...deps: string[]): Layer => ({
  id,
  namespaces: [`${id}.**`],
  allowedDependencies: deps,
});

describe('orderLayers', () => {
  it('puts dependants before the layers they depend on', () => {
    const order = orderLayers([
      layer('Domain'),
      layer('Application', 'Domain'),
      layer('Api', 'Application', 'Domain'),
    ]);
    expect(order.indexOf('Api')).toBeLessThan(order.indexOf('Application'));
    expect(order.indexOf('Application')).toBeLessThan(order.indexOf('Domain'));
  });

  it('keeps declaration order between independent layers', () => {
    const order = orderLayers([
      layer('Api', 'Application', 'Domain'),
      layer('Infrastructure', 'Application', 'Domain'),
      layer('Application', 'Domain'),
      layer('Domain'),
    ]);
    expect(order).toEqual(['Api', 'Infrastructure', 'Application', 'Domain']);
  });

  it('returns every layer exactly once when there is a cycle', () => {
    const order = orderLayers([layer('A', 'B'), layer('B', 'C'), layer('C', 'A')]);
    expect(order).toHaveLength(3);
    expect([...order].sort()).toEqual(['A', 'B', 'C']);
  });

  it('tolerates a self dependency and unknown dependency ids', () => {
    const order = orderLayers([layer('A', 'A', 'Nope'), layer('B', 'A')]);
    expect(order).toEqual(['B', 'A']);
  });

  it('handles an empty layer list', () => {
    expect(orderLayers([])).toEqual([]);
  });
});

describe('assignLanes', () => {
  it('groups nodes by layer and appends an unlayered lane last', () => {
    const graph = makeGraph();
    const lanes = assignLanes(graph.layers, graph.nodes);
    expect(lanes.map((l) => l.id)).toEqual(['Api', 'App', 'Domain', UNLAYERED]);
    expect(lanes[3]?.label).toBe('Unlayered');
    expect(lanes[3]?.nodes.map((n) => n.id)).toEqual(['T.Shared.Guard']);
    expect(lanes[0]?.nodes).toHaveLength(2);
  });

  it('treats a node with an unknown layer id as unlayered', () => {
    const graph = makeGraph();
    const first = graph.nodes[0];
    if (first) first.layer = 'Ghost';
    const lanes = assignLanes(graph.layers, graph.nodes);
    expect(lanes.at(-1)?.id).toBe(UNLAYERED);
    expect(lanes.at(-1)?.nodes.map((n) => n.id)).toContain('T.Api.OrdersController');
  });

  it('emits an empty lane for a layer with no nodes', () => {
    const graph = makeGraph();
    const lanes = assignLanes([...graph.layers, layer('Empty')], graph.nodes);
    expect(lanes.find((l) => l.id === 'Empty')?.nodes).toEqual([]);
  });
});

/** An elk stub: stacks nodes in a single column, so positions are predictable. */
const stubElk: ElkLike = {
  layout(graph: ElkGraph): Promise<ElkGraph> {
    const children = (graph.children ?? []).map((child, i) => ({
      ...child,
      x: 0,
      y: i * 100,
    }));
    return Promise.resolve({ ...graph, children });
  },
};

describe('computeLayout', () => {
  it('orders lanes by dependency direction', async () => {
    const graph = makeGraph();
    const layout = await computeLayout({ layers: graph.layers, nodes: graph.nodes, edges: graph.edges });
    const ids = layout.lanes.map((lane) => lane.id);
    expect(ids).toEqual(['Api', 'App', 'Domain', UNLAYERED]);

    for (let i = 1; i < layout.lanes.length; i++) {
      const previous = layout.lanes[i - 1]!;
      const current = layout.lanes[i]!;
      expect(current.order).toBe(previous.order + 1);
      // Dependencies sit strictly below their dependants, with no overlap.
      expect(current.y).toBeGreaterThanOrEqual(previous.y + previous.height);
    }
  });

  it('places every node strictly inside the bounds of its own lane', async () => {
    const graph = makeGraph();
    const layout = await computeLayout({ layers: graph.layers, nodes: graph.nodes, edges: graph.edges });
    const lanes = new Map(layout.lanes.map((lane) => [lane.id, lane] as const));
    const nodeLayer = new Map(graph.nodes.map((n) => [n.id, n.layer] as const));

    expect(layout.nodes).toHaveLength(graph.nodes.length);
    for (const positioned of layout.nodes) {
      const lane = lanes.get(positioned.laneId);
      expect(lane, `lane for ${positioned.id}`).toBeDefined();
      if (!lane) continue;
      expect(positioned.laneId).toBe(nodeLayer.get(positioned.id) ?? UNLAYERED);
      expect(positioned.x).toBeGreaterThanOrEqual(lane.x);
      expect(positioned.y).toBeGreaterThanOrEqual(lane.y);
      expect(positioned.x + positioned.width).toBeLessThanOrEqual(lane.x + lane.width);
      expect(positioned.y + positioned.height).toBeLessThanOrEqual(lane.y + lane.height);
      // Nodes never cover the lane header.
      expect(positioned.y).toBeGreaterThanOrEqual(lane.y + DEFAULT_LAYOUT_OPTIONS.laneHeaderHeight);
      expect(lane.nodeIds).toContain(positioned.id);
    }
  });

  it('never overlaps two nodes', async () => {
    const graph = makeGraph();
    const layout = await computeLayout({ layers: graph.layers, nodes: graph.nodes, edges: graph.edges });
    for (let i = 0; i < layout.nodes.length; i++) {
      for (let j = i + 1; j < layout.nodes.length; j++) {
        const a = layout.nodes[i]!;
        const b = layout.nodes[j]!;
        const overlaps =
          a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
        expect(overlaps, `${a.id} overlaps ${b.id}`).toBe(false);
      }
    }
  });

  it('keeps the invariants when elk does the placement', async () => {
    const graph = makeGraph();
    const layout = await computeLayout({
      layers: graph.layers,
      nodes: graph.nodes,
      edges: graph.edges,
      elk: stubElk,
    });
    const lanes = new Map(layout.lanes.map((lane) => [lane.id, lane] as const));
    expect(layout.nodes).toHaveLength(graph.nodes.length);
    for (const positioned of layout.nodes) {
      const lane = lanes.get(positioned.laneId)!;
      expect(positioned.x + positioned.width).toBeLessThanOrEqual(lane.x + lane.width);
      expect(positioned.y + positioned.height).toBeLessThanOrEqual(lane.y + lane.height);
    }
  });

  it('falls back to the grid when elk throws', async () => {
    const broken: ElkLike = { layout: () => Promise.reject(new Error('worker unavailable')) };
    const graph = makeGraph();
    const layout = await computeLayout({
      layers: graph.layers,
      nodes: graph.nodes,
      edges: graph.edges,
      elk: broken,
    });
    expect(layout.nodes).toHaveLength(graph.nodes.length);
    expect(layout.lanes).toHaveLength(4);
  });

  it('gives every lane the same width and a positive height', async () => {
    const graph = makeGraph();
    const layout = await computeLayout({ layers: graph.layers, nodes: graph.nodes, edges: graph.edges });
    for (const lane of layout.lanes) {
      expect(lane.width).toBe(layout.width);
      expect(lane.height).toBeGreaterThan(0);
    }
    const last = layout.lanes.at(-1)!;
    expect(layout.height).toBe(last.y + last.height);
  });

  it('drops empty lanes when asked', async () => {
    const graph = makeGraph();
    const nodes = graph.nodes.filter((node) => node.layer === 'Domain');
    const layout = await computeLayout({
      layers: graph.layers,
      nodes,
      edges: graph.edges,
      options: { hideEmptyLanes: true },
    });
    expect(layout.lanes.map((lane) => lane.id)).toEqual(['Domain']);
    expect(layout.lanes[0]?.order).toBe(0);
    expect(layout.lanes[0]?.y).toBe(0);
  });

  it('keeps empty lanes by default', async () => {
    const graph = makeGraph();
    const nodes = graph.nodes.filter((node) => node.layer === 'Domain');
    const layout = await computeLayout({ layers: graph.layers, nodes, edges: graph.edges });
    expect(layout.lanes.map((lane) => lane.id)).toEqual(['Api', 'App', 'Domain']);
  });

  it('handles an empty graph', async () => {
    const layout = await computeLayout({ layers: [], nodes: [], edges: [] });
    expect(layout).toEqual({ lanes: [], nodes: [], width: DEFAULT_LAYOUT_OPTIONS.minLaneWidth, height: 0 });
  });

  it('honours option overrides', async () => {
    const graph = makeGraph();
    const layout = await computeLayout({
      layers: graph.layers,
      nodes: graph.nodes,
      edges: graph.edges,
      options: { nodeWidth: 100, nodeHeight: 40, maxColumns: 1 },
    });
    expect(layout.nodes.every((n) => n.width === 100 && n.height === 40)).toBe(true);
    const apiNodes = layout.nodes.filter((n) => n.laneId === 'Api');
    expect(new Set(apiNodes.map((n) => n.x)).size).toBe(1); // single column
  });
});
