import type { Edge, Rule, TangentGraph, TangentNode, Violation } from './types';
import { severityRank } from './types';

/**
 * Contract colours. Chosen to stay legible on both the light and the dark
 * surface colours in styles.css; index 0 is used for the first contract in the
 * graph, wrapping around when there are more contracts than colours.
 */
export const CONTRACT_PALETTE = [
  '#3b82f6',
  '#a855f7',
  '#14b8a6',
  '#f97316',
  '#ec4899',
  '#84cc16',
  '#6366f1',
  '#eab308',
  '#06b6d4',
  '#ef4444',
] as const;

export const UNBOUND_COLOR = '#8b94a7';

/** Derived indexes over a graph, computed once per loaded document. */
export type GraphIndex = {
  graph: TangentGraph;
  nodesById: Map<string, TangentNode>;
  violationsByNode: Map<string, Violation[]>;
  rulesById: Map<string, Rule>;
  /** Contract id -> colour, in declaration order. */
  contractColors: Map<string, string>;
  /** Node id -> ids of nodes it references or is referenced by. */
  neighbours: Map<string, Set<string>>;
  outgoing: Map<string, Edge[]>;
  incoming: Map<string, Edge[]>;
};

export function buildIndex(graph: TangentGraph): GraphIndex {
  const nodesById = new Map(graph.nodes.map((node) => [node.id, node] as const));

  const violationsByNode = new Map<string, Violation[]>();
  for (const violation of graph.violations) {
    const list = violationsByNode.get(violation.node);
    if (list) list.push(violation);
    else violationsByNode.set(violation.node, [violation]);
  }
  for (const list of violationsByNode.values()) {
    list.sort(
      (a, b) =>
        severityRank(a.severity) - severityRank(b.severity) ||
        a.file.localeCompare(b.file) ||
        a.line - b.line ||
        a.col - b.col,
    );
  }

  const rulesById = new Map((graph.rules ?? []).map((rule) => [rule.id, rule] as const));

  const contractColors = new Map<string, string>();
  graph.contracts.forEach((contract, i) => {
    contractColors.set(
      contract.id,
      CONTRACT_PALETTE[i % CONTRACT_PALETTE.length] ?? UNBOUND_COLOR,
    );
  });

  const neighbours = new Map<string, Set<string>>();
  const outgoing = new Map<string, Edge[]>();
  const incoming = new Map<string, Edge[]>();
  const link = (map: Map<string, Set<string>>, a: string, b: string): void => {
    const set = map.get(a);
    if (set) set.add(b);
    else map.set(a, new Set([b]));
  };
  const push = (map: Map<string, Edge[]>, key: string, edge: Edge): void => {
    const list = map.get(key);
    if (list) list.push(edge);
    else map.set(key, [edge]);
  };
  for (const edge of graph.edges) {
    link(neighbours, edge.from, edge.to);
    link(neighbours, edge.to, edge.from);
    push(outgoing, edge.from, edge);
    push(incoming, edge.to, edge);
  }

  return { graph, nodesById, violationsByNode, rulesById, contractColors, neighbours, outgoing, incoming };
}

/** Colour for a node: its first contract, or the neutral unbound colour. */
export function nodeColor(index: GraphIndex, node: TangentNode): string {
  const first = node.contracts[0];
  if (first === undefined) return UNBOUND_COLOR;
  return index.contractColors.get(first) ?? UNBOUND_COLOR;
}

/**
 * The contract a rule id belongs to. Uses the `rules` array when the exporter
 * inlined it, otherwise falls back to the `Contract/...#n` id convention.
 */
export function ruleContract(index: GraphIndex, ruleId: string): string | null {
  const rule = index.rulesById.get(ruleId);
  if (rule) return rule.contract;
  const slash = ruleId.indexOf('/');
  return slash > 0 ? ruleId.slice(0, slash) : null;
}

export type Filters = {
  search: string;
  showUnbound: boolean;
  onlyViolations: boolean;
  focusMode: boolean;
  selectedNodeId: string | null;
};

export const DEFAULT_FILTERS: Filters = {
  search: '',
  showUnbound: true,
  onlyViolations: false,
  focusMode: false,
  selectedNodeId: null,
};

export function matchesSearch(node: TangentNode, search: string): boolean {
  if (search.trim() === '') return true;
  const needle = search.trim().toLowerCase();
  return node.name.toLowerCase().includes(needle) || node.id.toLowerCase().includes(needle);
}

/**
 * Which nodes are rendered, given the toggles. Search only highlights, so it
 * does not remove nodes here; focus mode and the two toggles do.
 */
export function visibleNodes(index: GraphIndex, filters: Filters): TangentNode[] {
  const { graph } = index;
  let nodes = graph.nodes;

  if (!filters.showUnbound) nodes = nodes.filter((node) => node.contracts.length > 0);
  if (filters.onlyViolations) nodes = nodes.filter((node) => node.violations > 0);

  if (filters.focusMode && filters.selectedNodeId !== null) {
    const keep = new Set<string>([filters.selectedNodeId]);
    for (const id of index.neighbours.get(filters.selectedNodeId) ?? []) keep.add(id);
    const selected = index.nodesById.get(filters.selectedNodeId);
    nodes = nodes.filter((node) => keep.has(node.id));
    // The selected node stays visible even when a toggle would have hidden it.
    if (selected && !nodes.some((node) => node.id === selected.id)) nodes = [selected, ...nodes];
  }

  return nodes;
}

/** Edges whose endpoints are both visible. */
export function visibleEdges(graph: TangentGraph, visible: Set<string>): Edge[] {
  return graph.edges.filter((edge) => visible.has(edge.from) && visible.has(edge.to));
}

export type MatrixCell = {
  ruleId: string;
  failing: boolean;
  violations: Violation[];
};

export type MatrixRow = {
  node: TangentNode;
  cells: MatrixCell[];
  failingCount: number;
};

export type ContractMatrix = {
  contractId: string;
  ruleIds: string[];
  rows: MatrixRow[];
};

/**
 * Bound types x rules for one contract. Columns come from the `rules` array
 * when the exporter emitted it, plus any rule id seen in a violation that
 * belongs to the contract; rows are the types bound to the contract.
 */
export function contractMatrix(index: GraphIndex, contractId: string): ContractMatrix {
  const { graph } = index;
  const ruleIds: string[] = [];
  const seen = new Set<string>();
  const add = (id: string): void => {
    if (!seen.has(id)) {
      seen.add(id);
      ruleIds.push(id);
    }
  };

  for (const rule of graph.rules ?? []) {
    if (rule.contract === contractId) add(rule.id);
  }

  const rows: MatrixRow[] = [];
  const bound = graph.nodes.filter((node) => node.contracts.includes(contractId));

  for (const node of bound) {
    for (const violation of index.violationsByNode.get(node.id) ?? []) {
      if (ruleContract(index, violation.ruleId) === contractId) add(violation.ruleId);
    }
  }

  for (const node of bound) {
    const byRule = new Map<string, Violation[]>();
    for (const violation of index.violationsByNode.get(node.id) ?? []) {
      const list = byRule.get(violation.ruleId);
      if (list) list.push(violation);
      else byRule.set(violation.ruleId, [violation]);
    }
    const cells = ruleIds.map((ruleId) => {
      const violations = byRule.get(ruleId) ?? [];
      return { ruleId, failing: violations.length > 0, violations };
    });
    rows.push({
      node,
      cells,
      failingCount: cells.reduce((sum, cell) => sum + (cell.failing ? 1 : 0), 0),
    });
  }

  rows.sort((a, b) => b.failingCount - a.failingCount || a.node.name.localeCompare(b.node.name));
  return { contractId, ruleIds, rows };
}

export type GraphStats = {
  nodes: number;
  edges: number;
  violations: number;
  ignored: number;
  layeringViolations: number;
  unbound: number;
};

export function graphStats(graph: TangentGraph): GraphStats {
  return {
    nodes: graph.nodes.length,
    edges: graph.edges.length,
    violations: graph.violations.length,
    ignored: graph.nodes.reduce((sum, node) => sum + node.ignored, 0),
    layeringViolations: graph.edges.filter((edge) => edge.violatesLayering).length,
    unbound: graph.nodes.filter((node) => node.contracts.length === 0).length,
  };
}
