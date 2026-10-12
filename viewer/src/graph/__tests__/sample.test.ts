import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { buildIndex, contractMatrix, graphStats } from '../derive';
import { collectWarnings, parseGraphText } from '../parse';
import { computeLayout } from '../layout';

const samplePath = fileURLToPath(new URL('../../../public/sample.graph.json', import.meta.url));
const graph = parseGraphText(readFileSync(samplePath, 'utf8'));

describe('public/sample.graph.json', () => {
  it('parses and is internally consistent', () => {
    expect(collectWarnings(graph)).toEqual([]);
    expect(graph.project).toBe('Orders');
    expect(graph.layers).toHaveLength(4);
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

  it('covers the features the viewer needs to show', () => {
    const stats = graphStats(graph);
    expect(stats.nodes).toBeGreaterThanOrEqual(20);
    expect(stats.edges).toBeGreaterThanOrEqual(30);
    expect(stats.layeringViolations).toBe(3);
    expect(stats.violations).toBeGreaterThanOrEqual(10);
    expect(stats.ignored).toBe(2);
    expect(new Set(graph.violations.map((v) => v.ruleId)).size).toBeGreaterThanOrEqual(5);
    expect(graph.nodes.some((n) => n.layer === null)).toBe(true);
    expect(graph.nodes.some((n) => n.contracts.length > 1)).toBe(true);
    expect(graph.contracts.some((c) => c.extends !== null)).toBe(true);
  });

  it('produces a contract matrix for every contract', () => {
    const index = buildIndex(graph);
    for (const contract of graph.contracts) {
      const matrix = contractMatrix(index, contract.id);
      expect(matrix.ruleIds.length, `${contract.id} columns`).toBeGreaterThan(0);
      expect(matrix.rows.length, `${contract.id} rows`).toBeGreaterThan(0);
    }
  });

  it('lays out with the Domain layer at the bottom', async () => {
    const layout = await computeLayout({
      layers: graph.layers,
      nodes: graph.nodes,
      edges: graph.edges,
    });
    expect(layout.lanes.map((l) => l.label)).toEqual([
      'Api',
      'Infrastructure',
      'Application',
      'Domain',
      'Unlayered',
    ]);
    expect(layout.nodes).toHaveLength(graph.nodes.length);
  });
});
