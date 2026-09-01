import { describe, expect, it } from 'vitest';
import { GraphParseError, collectWarnings, parseGraph, parseGraphText } from '../parse';
import { makeGraph } from './fixtures';

const minimal = {
  version: 1,
  project: 'Orders',
  generatedAt: '2026-09-22T09:30:00Z',
  layers: [{ id: 'Domain', namespaces: ['Orders.Domain.**'], allowedDependencies: [] }],
  contracts: [{ id: 'Entity', file: '.tangent/domain.tan', line: 1, extends: null, ruleCount: 3 }],
  nodes: [
    {
      id: 'Orders.Domain.Order',
      name: 'Order',
      kind: 'class',
      layer: 'Domain',
      contracts: ['Entity'],
      file: 'src/Order.cs',
      line: 4,
      violations: 0,
      exemptions: 0,
      members: [{ name: 'Id', kind: 'property', signature: 'Guid Id { get; }' }],
    },
  ],
  edges: [],
  violations: [],
};

describe('parseGraph', () => {
  it('accepts a minimal well-formed document', () => {
    const graph = parseGraph(minimal);
    expect(graph.project).toBe('Orders');
    expect(graph.nodes).toHaveLength(1);
    expect(graph.nodes[0]?.members[0]?.name).toBe('Id');
    expect(graph.rules).toBeUndefined();
  });

  it('round-trips the full fixture including optional rules and null layer', () => {
    const graph = parseGraph(JSON.parse(JSON.stringify(makeGraph())) as unknown);
    expect(graph.rules).toHaveLength(3);
    expect(graph.nodes.find((n) => n.id === 'T.Shared.Guard')?.layer).toBeNull();
  });

  it('accepts a null contract extends and a null violation member', () => {
    const graph = parseGraph({
      ...minimal,
      violations: [
        {
          node: 'Orders.Domain.Order',
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
    });
    expect(graph.contracts[0]?.extends).toBeNull();
    expect(graph.violations[0]?.member).toBeNull();
  });

  it('rejects a non-object document', () => {
    expect(() => parseGraph(42)).toThrow(GraphParseError);
    expect(() => parseGraph(null)).toThrow(/expected an object/);
  });

  it('rejects a JSON string that was not parsed first', () => {
    expect(() => parseGraph(JSON.stringify(minimal))).toThrow(/JSON.parse/);
  });

  it('reports missing top-level arrays with their path', () => {
    const { nodes: _nodes, edges: _edges, ...rest } = minimal;
    try {
      parseGraph(rest);
      expect.unreachable('should have thrown');
    } catch (error) {
      expect(error).toBeInstanceOf(GraphParseError);
      const issues = (error as GraphParseError).issues;
      expect(issues).toContain('nodes: expected an array, got nothing');
      expect(issues).toContain('edges: expected an array, got nothing');
    }
  });

  it('reports the path of a bad field inside an array item', () => {
    const bad = {
      ...minimal,
      nodes: [{ ...minimal.nodes[0], line: 'twelve', contracts: 'Entity' }],
    };
    try {
      parseGraph(bad);
      expect.unreachable('should have thrown');
    } catch (error) {
      const issues = (error as GraphParseError).issues;
      expect(issues).toContain('nodes[0].line: expected a number, got the string "twelve"');
      expect(issues).toContain('nodes[0].contracts: expected an array of strings, got the string "Entity"');
    }
  });

  it('rejects an unsupported schema version', () => {
    expect(() => parseGraph({ ...minimal, version: 2 })).toThrow(/version: expected 1/);
  });

  it('rejects duplicate node ids', () => {
    expect(() => parseGraph({ ...minimal, nodes: [minimal.nodes[0], minimal.nodes[0]] })).toThrow(
      /duplicate node id/,
    );
  });

  it('collects several issues in one error', () => {
    try {
      parseGraph({ ...minimal, project: 7, generatedAt: false });
      expect.unreachable('should have thrown');
    } catch (error) {
      expect((error as GraphParseError).issues.length).toBeGreaterThanOrEqual(2);
    }
  });
});

describe('parseGraphText', () => {
  it('parses JSON text', () => {
    expect(parseGraphText(JSON.stringify(minimal)).project).toBe('Orders');
  });

  it('reports invalid JSON clearly', () => {
    expect(() => parseGraphText('{ not json')).toThrow(/not valid JSON/);
  });
});

describe('collectWarnings', () => {
  it('is quiet for a consistent graph', () => {
    expect(collectWarnings(makeGraph())).toEqual([]);
  });

  it('flags dangling edges, unknown layers and unknown contracts', () => {
    const graph = makeGraph();
    graph.edges.push({
      from: 'T.Api.OrdersController',
      to: 'T.Missing.Type',
      kind: 'reference',
      count: 1,
      violatesLayering: false,
    });
    const first = graph.nodes[0];
    if (first) {
      first.layer = 'Ghost';
      first.contracts = ['NoSuchContract'];
    }
    const warnings = collectWarnings(graph);
    expect(warnings.some((w) => w.includes('edge(s)'))).toBe(true);
    expect(warnings.some((w) => w.includes('Ghost'))).toBe(true);
    expect(warnings.some((w) => w.includes('NoSuchContract'))).toBe(true);
  });
});
