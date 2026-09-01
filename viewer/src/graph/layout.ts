import type { Edge, Layer, TangentNode } from './types';
import { UNLAYERED } from './types';

/**
 * The slice of the elkjs API this module uses. Keeping it structural means the
 * layout can be driven by the real elkjs in the browser and by a stub (or
 * nothing at all, falling back to a grid) in tests.
 */
export type ElkLike = {
  layout(graph: ElkGraph): Promise<ElkGraph>;
};

export type ElkGraph = {
  id: string;
  layoutOptions?: Record<string, string>;
  children?: ElkGraph[];
  edges?: { id: string; sources: string[]; targets: string[] }[];
  x?: number;
  y?: number;
  width?: number;
  height?: number;
};

export type LayoutOptions = {
  nodeWidth: number;
  nodeHeight: number;
  /** Horizontal padding between a lane border and its nodes. */
  lanePaddingX: number;
  /** Space reserved at the top of a lane for its label. */
  laneHeaderHeight: number;
  /** Padding between the last row of nodes and the lane bottom border. */
  lanePaddingBottom: number;
  /** Vertical space between lanes. */
  laneGap: number;
  nodeGapX: number;
  nodeGapY: number;
  minLaneWidth: number;
  minLaneHeight: number;
  /** Target number of columns used by the grid fallback. */
  maxColumns: number;
  /** Drop lanes that ended up with no nodes, e.g. while filters are active. */
  hideEmptyLanes: boolean;
};

export const DEFAULT_LAYOUT_OPTIONS: LayoutOptions = {
  nodeWidth: 210,
  nodeHeight: 68,
  lanePaddingX: 28,
  laneHeaderHeight: 46,
  lanePaddingBottom: 28,
  laneGap: 40,
  nodeGapX: 36,
  nodeGapY: 28,
  minLaneWidth: 640,
  minLaneHeight: 120,
  maxColumns: 6,
  hideEmptyLanes: false,
};

export type PositionedNode = {
  id: string;
  laneId: string;
  /** Absolute position of the node's top-left corner. */
  x: number;
  y: number;
  width: number;
  height: number;
};

export type LaneBox = {
  /** Layer id, or {@link UNLAYERED} for types that belong to no layer. */
  id: string;
  label: string;
  /** 0 for the most-dependent layer, increasing towards leaf dependencies. */
  order: number;
  x: number;
  y: number;
  width: number;
  height: number;
  nodeIds: string[];
};

export type GraphLayout = {
  lanes: LaneBox[];
  nodes: PositionedNode[];
  width: number;
  height: number;
};

/**
 * Orders layers so that dependants come before the layers they depend on:
 * a topological sort of the `allowedDependencies` edges.
 *
 * Cycles are tolerated. When no layer is free of incoming dependants, the
 * remaining layer with the fewest unresolved dependants is emitted next, with
 * declaration order as the tie-break, so the result is always a total order
 * containing every layer exactly once.
 */
export function orderLayers(layers: Layer[]): string[] {
  const ids = layers.map((layer) => layer.id);
  const index = new Map(ids.map((id, i) => [id, i] as const));

  // dependants.get(x) = number of not-yet-emitted layers that depend on x.
  const dependants = new Map<string, number>(ids.map((id) => [id, 0] as const));
  const deps = new Map<string, string[]>();
  for (const layer of layers) {
    const targets = layer.allowedDependencies.filter(
      (dep) => index.has(dep) && dep !== layer.id,
    );
    deps.set(layer.id, [...new Set(targets)]);
    for (const dep of deps.get(layer.id) ?? []) {
      dependants.set(dep, (dependants.get(dep) ?? 0) + 1);
    }
  }

  const remaining = new Set(ids);
  const order: string[] = [];

  while (remaining.size > 0) {
    let best: string | null = null;
    let bestCount = Number.POSITIVE_INFINITY;
    for (const id of remaining) {
      const count = dependants.get(id) ?? 0;
      if (count < bestCount) {
        best = id;
        bestCount = count;
        if (count === 0) break; // declaration order wins among zero-dependant layers
      }
    }
    if (best === null) break;
    remaining.delete(best);
    order.push(best);
    for (const dep of deps.get(best) ?? []) {
      dependants.set(dep, Math.max(0, (dependants.get(dep) ?? 0) - 1));
    }
  }

  return order;
}

/** Groups nodes into lanes, preserving {@link orderLayers} order. */
export function assignLanes(
  layers: Layer[],
  nodes: TangentNode[],
): { id: string; label: string; nodes: TangentNode[] }[] {
  const order = orderLayers(layers);
  const known = new Set(order);
  const buckets = new Map<string, TangentNode[]>();
  for (const id of order) buckets.set(id, []);

  for (const node of nodes) {
    const key = node.layer !== null && known.has(node.layer) ? node.layer : UNLAYERED;
    const bucket = buckets.get(key);
    if (bucket) bucket.push(node);
    else buckets.set(key, [node]);
  }

  const lanes: { id: string; label: string; nodes: TangentNode[] }[] = [];
  for (const id of order) {
    lanes.push({ id, label: id, nodes: buckets.get(id) ?? [] });
  }
  const unlayered = buckets.get(UNLAYERED);
  if (unlayered && unlayered.length > 0) {
    lanes.push({ id: UNLAYERED, label: 'Unlayered', nodes: unlayered });
  }
  return lanes;
}

type LocalPlacement = { id: string; x: number; y: number };

/** Simple row-major grid placement, used as the fallback when elk is absent. */
function gridPlacements(
  nodes: TangentNode[],
  options: LayoutOptions,
): { placements: LocalPlacement[]; width: number; height: number } {
  if (nodes.length === 0) return { placements: [], width: 0, height: 0 };

  const columns = Math.max(1, Math.min(options.maxColumns, Math.ceil(Math.sqrt(nodes.length * 1.8))));
  const placements = nodes.map((node, i) => ({
    id: node.id,
    x: (i % columns) * (options.nodeWidth + options.nodeGapX),
    y: Math.floor(i / columns) * (options.nodeHeight + options.nodeGapY),
  }));
  const rows = Math.ceil(nodes.length / columns);
  const usedColumns = Math.min(columns, nodes.length);
  return {
    placements,
    width: usedColumns * options.nodeWidth + (usedColumns - 1) * options.nodeGapX,
    height: rows * options.nodeHeight + (rows - 1) * options.nodeGapY,
  };
}

async function elkPlacements(
  elk: ElkLike,
  laneId: string,
  nodes: TangentNode[],
  edges: Edge[],
  options: LayoutOptions,
): Promise<{ placements: LocalPlacement[]; width: number; height: number }> {
  const ids = new Set(nodes.map((n) => n.id));
  const internal = edges.filter((edge) => ids.has(edge.from) && ids.has(edge.to) && edge.from !== edge.to);

  const request: ElkGraph = {
    id: `lane:${laneId}`,
    layoutOptions: {
      'elk.algorithm': 'layered',
      'elk.direction': 'RIGHT',
      'elk.spacing.nodeNode': String(options.nodeGapY),
      'elk.layered.spacing.nodeNodeBetweenLayers': String(options.nodeGapX),
      'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
      'elk.aspectRatio': '3.2',
    },
    children: nodes.map((node) => ({
      id: node.id,
      width: options.nodeWidth,
      height: options.nodeHeight,
    })),
    edges: internal.map((edge, i) => ({
      id: `e${i}:${edge.from}->${edge.to}`,
      sources: [edge.from],
      targets: [edge.to],
    })),
  };

  const result = await elk.layout(request);
  const placements: LocalPlacement[] = [];
  let width = 0;
  let height = 0;
  for (const child of result.children ?? []) {
    const x = child.x ?? 0;
    const y = child.y ?? 0;
    placements.push({ id: child.id, x, y });
    width = Math.max(width, x + (child.width ?? options.nodeWidth));
    height = Math.max(height, y + (child.height ?? options.nodeHeight));
  }
  if (placements.length !== nodes.length) {
    throw new Error(`elk returned ${placements.length} positions for ${nodes.length} nodes`);
  }
  return { placements, width, height };
}

export type ComputeLayoutInput = {
  layers: Layer[];
  nodes: TangentNode[];
  edges: Edge[];
  /** Pass `null` (or omit) to use the deterministic grid fallback. */
  elk?: ElkLike | null;
  options?: Partial<LayoutOptions>;
};

/**
 * Lays the graph out as swimlanes, one per layer, stacked top to bottom in
 * dependency order, with nodes placed inside their own lane.
 *
 * Every returned node is positioned strictly inside the bounds of its lane.
 */
export async function computeLayout(input: ComputeLayoutInput): Promise<GraphLayout> {
  const options: LayoutOptions = { ...DEFAULT_LAYOUT_OPTIONS, ...(input.options ?? {}) };
  const allGroups = assignLanes(input.layers, input.nodes);
  const laneGroups = options.hideEmptyLanes
    ? allGroups.filter((group) => group.nodes.length > 0)
    : allGroups;

  const laid: {
    id: string;
    label: string;
    placements: LocalPlacement[];
    contentWidth: number;
    contentHeight: number;
  }[] = [];

  for (const group of laneGroups) {
    let placed: { placements: LocalPlacement[]; width: number; height: number };
    if (input.elk && group.nodes.length > 0) {
      try {
        placed = await elkPlacements(input.elk, group.id, group.nodes, input.edges, options);
      } catch {
        placed = gridPlacements(group.nodes, options);
      }
    } else {
      placed = gridPlacements(group.nodes, options);
    }
    laid.push({
      id: group.id,
      label: group.label,
      placements: placed.placements,
      contentWidth: placed.width,
      contentHeight: placed.height,
    });
  }

  const laneWidth = Math.max(
    options.minLaneWidth,
    ...laid.map((lane) => lane.contentWidth + options.lanePaddingX * 2),
  );

  const lanes: LaneBox[] = [];
  const nodes: PositionedNode[] = [];
  let y = 0;

  laid.forEach((lane, order) => {
    const laneHeight = Math.max(
      options.minLaneHeight,
      options.laneHeaderHeight + lane.contentHeight + options.lanePaddingBottom,
    );
    lanes.push({
      id: lane.id,
      label: lane.label,
      order,
      x: 0,
      y,
      width: laneWidth,
      height: laneHeight,
      nodeIds: lane.placements.map((p) => p.id),
    });
    for (const placement of lane.placements) {
      nodes.push({
        id: placement.id,
        laneId: lane.id,
        x: options.lanePaddingX + placement.x,
        y: y + options.laneHeaderHeight + placement.y,
        width: options.nodeWidth,
        height: options.nodeHeight,
      });
    }
    y += laneHeight + options.laneGap;
  });

  const height = lanes.length > 0 ? y - options.laneGap : 0;
  return { lanes, nodes, width: laneWidth, height };
}

/** Loads the real elkjs bundle. Returns `null` if it cannot be loaded. */
export async function loadElk(): Promise<ElkLike | null> {
  try {
    const mod = await import('elkjs/lib/elk.bundled.js');
    const Ctor = (mod.default ?? mod) as unknown as new () => ElkLike;
    return new Ctor();
  } catch {
    return null;
  }
}
