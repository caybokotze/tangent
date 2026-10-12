/**
 * TypeScript shape of `tangent.graph.json` (TANGENT-PLAN.md section 11.1).
 *
 * The exporter writes exactly these fields. Two documented extensions are
 * modelled as optional:
 *   - `TangentNode.layer` may be `null` for types that match no layer glob
 *     (for example when the graph was exported with `--include-unbound`).
 *   - `TangentGraph.rules` is emitted when the exporter is asked to inline the
 *     `.tan` rule text, so the viewer can show it next to a violation.
 *
 * Schema version 2 adds the file table (`files`, `fileId`) and, with
 * `--include-source`, `root`, violation snippets and contract text. All of
 * these are optional here so version 1 documents still load.
 */

/** A named region of the codebase with a declared dependency direction. */
export type Layer = {
  id: string;
  /** Namespace globs that make up the layer, e.g. `Orders.Domain.**`. */
  namespaces: string[];
  /** Ids of the layers this layer is allowed to depend on. */
  allowedDependencies: string[];
};

/** A named set of rules describing the shape of a kind of type. */
export type Contract = {
  id: string;
  file: string;
  line: number;
  /** Id of the contract this one extends, or `null`. */
  extends: string | null;
  ruleCount: number;
};

/** A member of a type, as listed on a node. */
export type Member = {
  name: string;
  kind: string;
  signature: string;
};

/** A C# type in the graph. */
export type TangentNode = {
  id: string;
  name: string;
  kind: string;
  /** Layer id, or `null` when the type belongs to no declared layer. */
  layer: string | null;
  /** Contracts the type is bound to; empty for unbound types. */
  contracts: string[];
  file: string;
  line: number;
  /** Number of violations reported against this type. */
  violations: number;
  /** Number of exemptions applied to this type. */
  exemptions: number;
  members: Member[];
  /** Index into `TangentGraph.files`, or -1. Absent in version 1 documents. */
  fileId?: number;
};

/** Coverage of a file: whether the types it declares are bound to contracts. */
export type FileStatus = 'tracked' | 'partial' | 'untracked' | 'contract';

/** One entry of the file table. Files are addressed by `id`, never by path. */
export type SourceFile = {
  id: number;
  /** Path relative to the solution root. */
  path: string;
  /** `csharp` or `contract` (a `.tan` file). */
  kind: string;
  /** The project that compiles the file; `null` for contract files. */
  project: string | null;
  lines: number;
  status: FileStatus;
  /** Ids of the types the file declares. */
  types: string[];
  violations: number;
  /** Whole file text: contract files exported with `--include-source` only. */
  text?: string;
};

/** Source lines around a violation. */
export type Snippet = {
  /** 1-based line number of `lines[0]`. */
  startLine: number;
  lines: string[];
};

/** A reference from one type to another. */
export type Edge = {
  from: string;
  to: string;
  kind: string;
  /** How many distinct references the analyzer saw. */
  count: number;
  /** True when the edge crosses a layer boundary that is not allowed. */
  violatesLayering: boolean;
};

export type Severity = 'error' | 'warning' | 'info' | 'hidden';

/** A single rule failure. */
export type Violation = {
  node: string;
  /** Member the rule was evaluated against, or `null` for type-level rules. */
  member: string | null;
  ruleId: string;
  /** Diagnostic id, e.g. `TNG0104`. */
  code: string;
  message: string;
  file: string;
  line: number;
  col: number;
  severity: string;
  /** Index into `TangentGraph.files`, or -1. Absent in version 1 documents. */
  fileId?: number;
  /** Present when the graph was exported with `--include-source`. */
  snippet?: Snippet;
};

/** An exemption that suppressed a rule for one type or member. */
export type Exemption = {
  node: string;
  member: string | null;
  ruleId: string;
  reason: string;
  file: string;
  line: number;
  fileId: number;
};

/** Optional inlined `.tan` rule text, keyed by the `ruleId` on a violation. */
export type Rule = {
  id: string;
  contract: string;
  text: string;
  /** 0 = symbol, 1 = reference, 2 = flow. */
  tier: number;
};

export type TangentGraph = {
  version: number;
  project: string;
  generatedAt: string;
  /** Absolute solution root, for `vscode://file/` links. `--include-source` only. */
  root?: string;
  layers: Layer[];
  contracts: Contract[];
  nodes: TangentNode[];
  edges: Edge[];
  violations: Violation[];
  rules?: Rule[];
  /** The file table. Empty for version 1 documents. */
  files?: SourceFile[];
  /** Applied exemptions. Empty for version 1 documents. */
  exemptions?: Exemption[];
};

/** Lane id used for nodes whose `layer` is `null`. */
export const UNLAYERED = '\u0000unlayered';

export const SEVERITY_ORDER: Record<string, number> = {
  error: 0,
  warning: 1,
  info: 2,
  hidden: 3,
};

export function severityRank(severity: string): number {
  return SEVERITY_ORDER[severity] ?? 99;
}
