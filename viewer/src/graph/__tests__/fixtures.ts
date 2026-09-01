import type { TangentGraph } from '../types';

/** A small, hand-written graph used by the unit tests. */
export function makeGraph(overrides: Partial<TangentGraph> = {}): TangentGraph {
  const graph: TangentGraph = {
    version: 1,
    project: 'Test',
    generatedAt: '2026-01-01T00:00:00Z',
    layers: [
      { id: 'Api', namespaces: ['T.Api.**'], allowedDependencies: ['App', 'Domain'] },
      { id: 'Domain', namespaces: ['T.Domain.**'], allowedDependencies: [] },
      { id: 'App', namespaces: ['T.App.**'], allowedDependencies: ['Domain'] },
    ],
    contracts: [
      { id: 'Controller', file: '.tangent/api.tan', line: 1, extends: null, ruleCount: 2 },
      { id: 'Entity', file: '.tangent/domain.tan', line: 1, extends: null, ruleCount: 2 },
    ],
    nodes: [
      node('T.Api.OrdersController', 'Api', ['Controller'], 1),
      node('T.Api.HealthController', 'Api', ['Controller'], 0),
      node('T.App.GetOrder', 'App', [], 0),
      node('T.Domain.Order', 'Domain', ['Entity'], 1),
      node('T.Domain.OrderLine', 'Domain', ['Entity'], 0),
      node('T.Shared.Guard', null, [], 0),
    ],
    edges: [
      edge('T.Api.OrdersController', 'T.App.GetOrder'),
      edge('T.App.GetOrder', 'T.Domain.Order'),
      edge('T.Domain.Order', 'T.Domain.OrderLine'),
      edge('T.Domain.Order', 'T.Api.OrdersController', true),
      edge('T.Api.OrdersController', 'T.Shared.Guard'),
    ],
    violations: [
      {
        node: 'T.Api.OrdersController',
        member: 'Get',
        ruleId: 'Controller/methods/attributes#1',
        code: 'TNG0104',
        message: 'missing attribute',
        file: 'src/OrdersController.cs',
        line: 10,
        col: 5,
        severity: 'warning',
      },
      {
        node: 'T.Domain.Order',
        member: null,
        ruleId: 'Entity/require/sealed#1',
        code: 'TNG0131',
        message: 'not sealed',
        file: 'src/Order.cs',
        line: 4,
        col: 14,
        severity: 'error',
      },
    ],
    rules: [
      {
        id: 'Controller/methods/attributes#1',
        contract: 'Controller',
        text: 'methods { require attribute HttpMethodAttribute }',
        tier: 0,
      },
      { id: 'Controller/require/sealed#2', contract: 'Controller', text: 'require sealed', tier: 0 },
      { id: 'Entity/require/sealed#1', contract: 'Entity', text: 'require sealed', tier: 0 },
    ],
  };
  return { ...graph, ...overrides };
}

export function node(
  id: string,
  layer: string | null,
  contracts: string[],
  violations: number,
  exemptions = 0,
): TangentGraph['nodes'][number] {
  const dot = id.lastIndexOf('.');
  return {
    id,
    name: dot >= 0 ? id.slice(dot + 1) : id,
    kind: 'class',
    layer,
    contracts,
    file: `src/${id}.cs`,
    line: 1,
    violations,
    exemptions,
    members: [{ name: 'Get', kind: 'method', signature: 'void Get()' }],
  };
}

export function edge(from: string, to: string, violatesLayering = false): TangentGraph['edges'][number] {
  return { from, to, kind: 'reference', count: 1, violatesLayering };
}
