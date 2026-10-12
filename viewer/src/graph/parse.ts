import type {
  Contract,
  Edge,
  Exemption,
  Layer,
  Member,
  Rule,
  Snippet,
  SourceFile,
  TangentGraph,
  TangentNode,
  Violation,
} from './types';

/** Thrown by {@link parseGraph} when the document does not match the schema. */
export class GraphParseError extends Error {
  readonly issues: string[];

  constructor(issues: string[]) {
    const shown = issues.slice(0, 8);
    const more = issues.length - shown.length;
    super(
      `tangent.graph.json is not valid:\n  - ${shown.join('\n  - ')}` +
        (more > 0 ? `\n  ...and ${more} more problem${more === 1 ? '' : 's'}` : ''),
    );
    this.name = 'GraphParseError';
    this.issues = issues;
  }
}

class Ctx {
  readonly issues: string[] = [];

  fail(path: string, expected: string, actual: unknown): void {
    this.issues.push(`${path}: expected ${expected}, got ${describe(actual)}`);
  }
}

function describe(value: unknown): string {
  if (value === null) return 'null';
  if (value === undefined) return 'nothing';
  if (Array.isArray(value)) return `an array (${value.length} items)`;
  if (typeof value === 'string') return `the string ${JSON.stringify(truncate(value))}`;
  if (typeof value === 'object') return 'an object';
  return String(value);
}

function truncate(value: string): string {
  return value.length > 40 ? `${value.slice(0, 37)}...` : value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function str(ctx: Ctx, value: unknown, path: string, fallback = ''): string {
  if (typeof value === 'string') return value;
  ctx.fail(path, 'a string', value);
  return fallback;
}

function optionalStr(ctx: Ctx, value: unknown, path: string): string | null {
  if (value === undefined || value === null) return null;
  if (typeof value === 'string') return value;
  ctx.fail(path, 'a string or null', value);
  return null;
}

function num(ctx: Ctx, value: unknown, path: string, fallback = 0): number {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  ctx.fail(path, 'a number', value);
  return fallback;
}

function optionalNum(ctx: Ctx, value: unknown, path: string, fallback: number): number {
  if (value === undefined || value === null) return fallback;
  return num(ctx, value, path, fallback);
}

function bool(ctx: Ctx, value: unknown, path: string, fallback = false): boolean {
  if (typeof value === 'boolean') return value;
  ctx.fail(path, 'a boolean', value);
  return fallback;
}

function strArray(ctx: Ctx, value: unknown, path: string): string[] {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) {
    ctx.fail(path, 'an array of strings', value);
    return [];
  }
  return value.map((item, i) => str(ctx, item, `${path}[${i}]`));
}

function objArray<T>(
  ctx: Ctx,
  value: unknown,
  path: string,
  required: boolean,
  map: (item: Record<string, unknown>, itemPath: string) => T,
): T[] {
  if (value === undefined || value === null) {
    if (required) ctx.fail(path, 'an array', value);
    return [];
  }
  if (!Array.isArray(value)) {
    ctx.fail(path, 'an array', value);
    return [];
  }
  const out: T[] = [];
  value.forEach((item: unknown, i: number) => {
    const itemPath = `${path}[${i}]`;
    if (!isRecord(item)) {
      ctx.fail(itemPath, 'an object', item);
      return;
    }
    out.push(map(item, itemPath));
  });
  return out;
}

/**
 * Validates and narrows an arbitrary JSON document to a {@link TangentGraph}.
 * Throws {@link GraphParseError}, which lists every problem that was found.
 */
export function parseGraph(json: unknown): TangentGraph {
  const ctx = new Ctx();

  if (typeof json === 'string') {
    throw new GraphParseError([
      'the document is a JSON string, not an object - parse it with JSON.parse first',
    ]);
  }
  if (!isRecord(json)) {
    throw new GraphParseError([`the document: expected an object, got ${describe(json)}`]);
  }

  const version = num(ctx, json['version'], 'version', 1);
  if (typeof json['version'] === 'number' && version !== 1 && version !== 2) {
    ctx.issues.push(
      `version: expected 1 or 2, the schema versions this viewer understands, got ${version}`,
    );
  }

  const graph: TangentGraph = {
    version,
    project: str(ctx, json['project'], 'project', '(unnamed)'),
    generatedAt: str(ctx, json['generatedAt'], 'generatedAt', ''),
    layers: objArray(ctx, json['layers'], 'layers', true, (i, p) => parseLayer(ctx, i, p)),
    contracts: objArray(ctx, json['contracts'], 'contracts', true, (i, p) => parseContract(ctx, i, p)),
    nodes: objArray(ctx, json['nodes'], 'nodes', true, (i, p) => parseNode(ctx, i, p)),
    edges: objArray(ctx, json['edges'], 'edges', true, (i, p) => parseEdge(ctx, i, p)),
    violations: objArray(ctx, json['violations'], 'violations', true, (i, p) =>
      parseViolation(ctx, i, p),
    ),
  };

  const root = optionalStr(ctx, json['root'], 'root');
  if (root !== null) graph.root = root;

  graph.files = objArray(ctx, json['files'], 'files', false, (i, p) => parseFile(ctx, i, p));
  graph.exemptions = objArray(ctx, json['exemptions'], 'exemptions', false, (i, p) =>
    parseExemption(ctx, i, p),
  );

  if (json['rules'] !== undefined && json['rules'] !== null) {
    graph.rules = objArray(ctx, json['rules'], 'rules', false, (i, p) => parseRule(ctx, i, p));
  }

  const seen = new Set<string>();
  for (const node of graph.nodes) {
    if (seen.has(node.id)) ctx.issues.push(`nodes: duplicate node id ${JSON.stringify(node.id)}`);
    seen.add(node.id);
  }

  if (ctx.issues.length > 0) throw new GraphParseError(ctx.issues);
  return graph;
}

function parseLayer(ctx: Ctx, item: Record<string, unknown>, path: string): Layer {
  return {
    id: str(ctx, item['id'], `${path}.id`),
    namespaces: strArray(ctx, item['namespaces'], `${path}.namespaces`),
    allowedDependencies: strArray(ctx, item['allowedDependencies'], `${path}.allowedDependencies`),
  };
}

function parseContract(ctx: Ctx, item: Record<string, unknown>, path: string): Contract {
  return {
    id: str(ctx, item['id'], `${path}.id`),
    file: str(ctx, item['file'], `${path}.file`),
    line: num(ctx, item['line'], `${path}.line`),
    extends: optionalStr(ctx, item['extends'], `${path}.extends`),
    ruleCount: num(ctx, item['ruleCount'], `${path}.ruleCount`),
  };
}

function parseMember(ctx: Ctx, item: Record<string, unknown>, path: string): Member {
  return {
    name: str(ctx, item['name'], `${path}.name`),
    kind: str(ctx, item['kind'], `${path}.kind`),
    signature: str(ctx, item['signature'], `${path}.signature`),
  };
}

function parseNode(ctx: Ctx, item: Record<string, unknown>, path: string): TangentNode {
  return {
    id: str(ctx, item['id'], `${path}.id`),
    name: str(ctx, item['name'], `${path}.name`),
    kind: str(ctx, item['kind'], `${path}.kind`, 'class'),
    layer: optionalStr(ctx, item['layer'], `${path}.layer`),
    contracts: strArray(ctx, item['contracts'], `${path}.contracts`),
    file: str(ctx, item['file'], `${path}.file`),
    line: num(ctx, item['line'], `${path}.line`),
    violations: num(ctx, item['violations'], `${path}.violations`),
    exemptions: num(ctx, item['exemptions'], `${path}.exemptions`),
    members: objArray(ctx, item['members'], `${path}.members`, false, (i, p) =>
      parseMember(ctx, i, p),
    ),
    fileId: optionalNum(ctx, item['fileId'], `${path}.fileId`, -1),
  };
}

const FILE_STATUSES = new Set(['tracked', 'partial', 'untracked', 'contract']);

function parseFile(ctx: Ctx, item: Record<string, unknown>, path: string): SourceFile {
  const status = str(ctx, item['status'], `${path}.status`, 'untracked');
  if (!FILE_STATUSES.has(status)) {
    ctx.fail(`${path}.status`, 'tracked, partial, untracked or contract', status);
  }
  const file: SourceFile = {
    id: num(ctx, item['id'], `${path}.id`),
    path: str(ctx, item['path'], `${path}.path`),
    kind: str(ctx, item['kind'], `${path}.kind`, 'csharp'),
    project: optionalStr(ctx, item['project'], `${path}.project`),
    lines: num(ctx, item['lines'], `${path}.lines`),
    status: status as SourceFile['status'],
    types: strArray(ctx, item['types'], `${path}.types`),
    violations: num(ctx, item['violations'], `${path}.violations`),
  };
  const text = optionalStr(ctx, item['text'], `${path}.text`);
  if (text !== null) file.text = text;
  return file;
}

function parseSnippet(ctx: Ctx, value: unknown, path: string): Snippet | undefined {
  if (value === undefined || value === null) return undefined;
  if (!isRecord(value)) {
    ctx.fail(path, 'an object', value);
    return undefined;
  }
  return {
    startLine: num(ctx, value['startLine'], `${path}.startLine`, 1),
    lines: strArray(ctx, value['lines'], `${path}.lines`),
  };
}

function parseEdge(ctx: Ctx, item: Record<string, unknown>, path: string): Edge {
  return {
    from: str(ctx, item['from'], `${path}.from`),
    to: str(ctx, item['to'], `${path}.to`),
    kind: str(ctx, item['kind'], `${path}.kind`, 'reference'),
    count: num(ctx, item['count'], `${path}.count`, 1),
    violatesLayering: bool(ctx, item['violatesLayering'], `${path}.violatesLayering`),
  };
}

function parseViolation(ctx: Ctx, item: Record<string, unknown>, path: string): Violation {
  const violation: Violation = {
    node: str(ctx, item['node'], `${path}.node`),
    member: optionalStr(ctx, item['member'], `${path}.member`),
    ruleId: str(ctx, item['ruleId'], `${path}.ruleId`),
    code: str(ctx, item['code'], `${path}.code`),
    message: str(ctx, item['message'], `${path}.message`),
    file: str(ctx, item['file'], `${path}.file`),
    line: num(ctx, item['line'], `${path}.line`),
    col: num(ctx, item['col'], `${path}.col`),
    severity: str(ctx, item['severity'], `${path}.severity`, 'warning'),
    fileId: optionalNum(ctx, item['fileId'], `${path}.fileId`, -1),
  };
  const snippet = parseSnippet(ctx, item['snippet'], `${path}.snippet`);
  if (snippet !== undefined) violation.snippet = snippet;
  return violation;
}

function parseExemption(ctx: Ctx, item: Record<string, unknown>, path: string): Exemption {
  return {
    node: str(ctx, item['node'], `${path}.node`),
    member: optionalStr(ctx, item['member'], `${path}.member`),
    ruleId: str(ctx, item['ruleId'], `${path}.ruleId`),
    reason: str(ctx, item['reason'], `${path}.reason`),
    file: str(ctx, item['file'], `${path}.file`),
    line: num(ctx, item['line'], `${path}.line`),
    fileId: optionalNum(ctx, item['fileId'], `${path}.fileId`, -1),
  };
}

function parseRule(ctx: Ctx, item: Record<string, unknown>, path: string): Rule {
  return {
    id: str(ctx, item['id'], `${path}.id`),
    contract: str(ctx, item['contract'], `${path}.contract`),
    text: str(ctx, item['text'], `${path}.text`),
    tier: num(ctx, item['tier'], `${path}.tier`),
  };
}

/**
 * Referential problems that are worth showing the user but that do not stop the
 * graph from rendering: dangling edges, violations for unknown nodes, and so on.
 */
export function collectWarnings(graph: TangentGraph): string[] {
  const warnings: string[] = [];
  const nodeIds = new Set(graph.nodes.map((n) => n.id));
  const layerIds = new Set(graph.layers.map((l) => l.id));
  const contractIds = new Set(graph.contracts.map((c) => c.id));

  let danglingEdges = 0;
  for (const edge of graph.edges) {
    if (!nodeIds.has(edge.from) || !nodeIds.has(edge.to)) danglingEdges++;
  }
  if (danglingEdges > 0) {
    warnings.push(`${danglingEdges} edge(s) reference a type that is not in nodes[]`);
  }

  const unknownViolationNodes = new Set<string>();
  for (const violation of graph.violations) {
    if (!nodeIds.has(violation.node)) unknownViolationNodes.add(violation.node);
  }
  if (unknownViolationNodes.size > 0) {
    warnings.push(`${unknownViolationNodes.size} violation target(s) are not in nodes[]`);
  }

  const unknownLayers = new Set<string>();
  for (const node of graph.nodes) {
    if (node.layer !== null && !layerIds.has(node.layer)) unknownLayers.add(node.layer);
  }
  for (const layer of graph.layers) {
    for (const dep of layer.allowedDependencies) {
      if (!layerIds.has(dep)) unknownLayers.add(dep);
    }
  }
  if (unknownLayers.size > 0) {
    warnings.push(`unknown layer id(s): ${[...unknownLayers].join(', ')}`);
  }

  const unknownContracts = new Set<string>();
  for (const node of graph.nodes) {
    for (const contract of node.contracts) {
      if (!contractIds.has(contract)) unknownContracts.add(contract);
    }
  }
  if (unknownContracts.size > 0) {
    warnings.push(`unknown contract id(s): ${[...unknownContracts].join(', ')}`);
  }

  return warnings;
}

/** Convenience wrapper: parse JSON text, then validate it. */
export function parseGraphText(text: string): TangentGraph {
  let json: unknown;
  try {
    json = JSON.parse(text) as unknown;
  } catch (error) {
    throw new GraphParseError([
      `the file is not valid JSON: ${error instanceof Error ? error.message : String(error)}`,
    ]);
  }
  return parseGraph(json);
}
