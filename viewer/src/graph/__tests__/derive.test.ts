import { describe, expect, it } from 'vitest';
import {
  CONTRACT_PALETTE,
  DEFAULT_FILTERS,
  UNBOUND_COLOR,
  buildIndex,
  contractMatrix,
  graphStats,
  matchesSearch,
  nodeColor,
  ruleContract,
  visibleEdges,
  visibleNodes,
} from '../derive';
import { makeGraph } from './fixtures';

describe('buildIndex', () => {
  it('indexes violations by node, worst severity first', () => {
    const index = buildIndex(makeGraph());
    expect(index.violationsByNode.get('T.Domain.Order')?.[0]?.severity).toBe('error');
    expect(index.violationsByNode.get('T.App.GetOrder')).toBeUndefined();
  });

  it('assigns one palette colour per contract, in declaration order', () => {
    const index = buildIndex(makeGraph());
    expect(index.contractColors.get('Controller')).toBe(CONTRACT_PALETTE[0]);
    expect(index.contractColors.get('Entity')).toBe(CONTRACT_PALETTE[1]);
  });

  it('records neighbours in both directions', () => {
    const index = buildIndex(makeGraph());
    expect([...(index.neighbours.get('T.App.GetOrder') ?? [])].sort()).toEqual([
      'T.Api.OrdersController',
      'T.Domain.Order',
    ]);
    expect(index.outgoing.get('T.Api.OrdersController')).toHaveLength(2);
    expect(index.incoming.get('T.Domain.Order')).toHaveLength(1);
  });
});

describe('nodeColor', () => {
  it('uses the first contract, and the neutral colour when unbound', () => {
    const graph = makeGraph();
    const index = buildIndex(graph);
    const controller = graph.nodes.find((n) => n.id === 'T.Api.OrdersController')!;
    const unbound = graph.nodes.find((n) => n.id === 'T.App.GetOrder')!;
    expect(nodeColor(index, controller)).toBe(CONTRACT_PALETTE[0]);
    expect(nodeColor(index, unbound)).toBe(UNBOUND_COLOR);
  });
});

describe('ruleContract', () => {
  it('prefers the rules array and falls back to the id convention', () => {
    const index = buildIndex(makeGraph());
    expect(ruleContract(index, 'Entity/require/sealed#1')).toBe('Entity');
    expect(ruleContract(index, 'Unknown/some/rule#9')).toBe('Unknown');
    expect(ruleContract(index, 'noslash')).toBeNull();
  });
});

describe('visibleNodes', () => {
  const graph = makeGraph();
  const index = buildIndex(graph);

  it('shows everything by default', () => {
    expect(visibleNodes(index, DEFAULT_FILTERS)).toHaveLength(graph.nodes.length);
  });

  it('hides unbound types when asked', () => {
    const nodes = visibleNodes(index, { ...DEFAULT_FILTERS, showUnbound: false });
    expect(nodes.every((n) => n.contracts.length > 0)).toBe(true);
    expect(nodes).toHaveLength(4);
  });

  it('keeps only nodes with violations when asked', () => {
    const nodes = visibleNodes(index, { ...DEFAULT_FILTERS, onlyViolations: true });
    expect(nodes.map((n) => n.id).sort()).toEqual(['T.Api.OrdersController', 'T.Domain.Order']);
  });

  it('focus mode keeps the selection and its 1-hop neighbourhood', () => {
    const nodes = visibleNodes(index, {
      ...DEFAULT_FILTERS,
      focusMode: true,
      selectedNodeId: 'T.App.GetOrder',
    });
    expect(nodes.map((n) => n.id).sort()).toEqual([
      'T.Api.OrdersController',
      'T.App.GetOrder',
      'T.Domain.Order',
    ]);
  });

  it('focus mode keeps the selected node even when a toggle would hide it', () => {
    const nodes = visibleNodes(index, {
      ...DEFAULT_FILTERS,
      focusMode: true,
      onlyViolations: true,
      selectedNodeId: 'T.App.GetOrder',
    });
    expect(nodes.map((n) => n.id)).toContain('T.App.GetOrder');
  });

  it('search does not remove nodes, it only matches them', () => {
    expect(visibleNodes(index, { ...DEFAULT_FILTERS, search: 'zzz' })).toHaveLength(
      graph.nodes.length,
    );
    const order = graph.nodes.find((n) => n.id === 'T.Domain.Order')!;
    expect(matchesSearch(order, 'ord')).toBe(true);
    expect(matchesSearch(order, 'T.Domain')).toBe(true);
    expect(matchesSearch(order, 'guard')).toBe(false);
    expect(matchesSearch(order, '   ')).toBe(true);
  });
});

describe('visibleEdges', () => {
  it('drops edges with a hidden endpoint', () => {
    const graph = makeGraph();
    const visible = new Set(['T.Api.OrdersController', 'T.App.GetOrder']);
    expect(visibleEdges(graph, visible)).toHaveLength(1);
  });
});

describe('contractMatrix', () => {
  it('lists bound types as rows and contract rules as columns', () => {
    const index = buildIndex(makeGraph());
    const matrix = contractMatrix(index, 'Controller');
    expect(matrix.ruleIds).toEqual([
      'Controller/methods/attributes#1',
      'Controller/require/sealed#2',
    ]);
    expect(matrix.rows.map((r) => r.node.name)).toEqual(['OrdersController', 'HealthController']);
    expect(matrix.rows[0]?.cells[0]?.failing).toBe(true);
    expect(matrix.rows[0]?.cells[1]?.failing).toBe(false);
    expect(matrix.rows[1]?.failingCount).toBe(0);
  });

  it('derives columns from violations when the rules array is absent', () => {
    const graph = makeGraph();
    delete graph.rules;
    const matrix = contractMatrix(buildIndex(graph), 'Controller');
    expect(matrix.ruleIds).toEqual(['Controller/methods/attributes#1']);
    expect(matrix.rows[0]?.cells[0]?.violations[0]?.code).toBe('TNG0104');
  });

  it('is empty for a contract with no bound types', () => {
    const matrix = contractMatrix(buildIndex(makeGraph()), 'Nope');
    expect(matrix.rows).toEqual([]);
    expect(matrix.ruleIds).toEqual([]);
  });
});

describe('graphStats', () => {
  it('summarises the graph', () => {
    expect(graphStats(makeGraph())).toEqual({
      nodes: 6,
      edges: 5,
      violations: 2,
      ignored: 0,
      layeringViolations: 1,
      unbound: 2,
    });
  });
});
