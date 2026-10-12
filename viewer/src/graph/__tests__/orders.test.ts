import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { buildIndex, contractMatrix, graphStats } from '../derive';
import { collectWarnings, parseGraphText } from '../parse';
import { computeLayout } from '../layout';

/**
 * The real export, produced by `tangent graph samples/Orders --include-unbound` and copied here so the
 * viewer can be opened on actual data with `?graph=/orders.graph.json`. Unlike sample.graph.json this
 * file is generated, so these tests are the contract between the exporter and the viewer.
 */
const ordersPath = fileURLToPath(new URL('../../../public/orders.graph.json', import.meta.url));
const graph = parseGraphText(readFileSync(ordersPath, 'utf8'));

describe('public/orders.graph.json', () => {
  it('parses with no referential problems', () => {
    expect(collectWarnings(graph)).toEqual([]);
    expect(graph.version).toBe(2);
    expect(graph.project).toBe('Orders');
    expect(graph.generatedAt).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/);
  });

  it('has the four layers the sample declares', () => {
    expect(graph.layers.map((l) => l.id).sort()).toEqual([
      'Api',
      'Application',
      'Domain',
      'Infrastructure',
    ]);
    expect(graph.layers.find((l) => l.id === 'Domain')?.allowedDependencies).toEqual([]);
    expect(graph.layers.find((l) => l.id === 'Api')?.allowedDependencies).toEqual([
      'Application',
      'Domain',
    ]);
  });

  it('has node violation counts that match the violations array', () => {
    const counted = new Map<string, number>();
    for (const violation of graph.violations) {
      counted.set(violation.node, (counted.get(violation.node) ?? 0) + 1);
    }
    for (const node of graph.nodes) {
      expect(node.violations, `${node.id} violation count`).toBe(counted.get(node.id) ?? 0);
    }
  });

  it('shows the layer violations the sample is built around', () => {
    const stats = graphStats(graph);
    expect(stats.nodes).toBeGreaterThanOrEqual(20);
    expect(stats.edges).toBeGreaterThanOrEqual(30);
    expect(stats.layeringViolations).toBeGreaterThan(0);
    expect(stats.ignored).toBeGreaterThan(0);

    const illegal = graph.edges.filter((e) => e.violatesLayering);
    expect(illegal).toContainEqual(
      expect.objectContaining({
        from: 'Orders.Api.Controllers.ReportsController',
        to: 'Orders.Infrastructure.Persistence.OrdersDbContext',
      }),
    );
  });

  it('inlines the .tan rule text for the violations it reports', () => {
    expect(graph.rules).toBeDefined();
    const ruleIds = new Set((graph.rules ?? []).map((r) => r.id));
    const contractViolations = graph.violations.filter(
      (v) => v.ruleId.length > 0 && !v.ruleId.startsWith('layer:'),
    );
    expect(contractViolations.length).toBeGreaterThan(0);
    for (const violation of contractViolations) {
      expect(ruleIds, `rule ${violation.ruleId}`).toContain(violation.ruleId);
    }
  });

  it('produces a contract matrix for every contract', () => {
    const index = buildIndex(graph);
    for (const contract of graph.contracts) {
      const matrix = contractMatrix(index, contract.id);
      expect(matrix.ruleIds.length, `${contract.id} columns`).toBeGreaterThan(0);
    }
  });

  it('lays out into swimlanes', async () => {
    const layout = await computeLayout({
      layers: graph.layers,
      nodes: graph.nodes,
      edges: graph.edges,
    });
    expect(layout.nodes).toHaveLength(graph.nodes.length);
    expect(layout.lanes.map((l) => l.label)).toContain('Domain');
  });
});
