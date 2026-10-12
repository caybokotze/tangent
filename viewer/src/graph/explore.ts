/**
 * Derived models for the v2 viewer: issues grouped by rule, which types each
 * rule applies to, file coverage, and rooted neighbourhoods for the graph.
 * Pure functions over a {@link GraphIndex}; the components never recompute
 * these themselves.
 */
import type { GraphIndex } from './derive';
import type { Ignore, Rule, SourceFile, TangentNode, Violation } from './types';
import { severityRank } from './types';

/** Pseudo-contract that owns the synthesised layer rules (`layer:A->B`). */
export const LAYERS_CONTRACT = 'Layers';

// ---------------------------------------------------------------------------
// Messages

const RULE_SUFFIX = /\s*\(rule ([^,()]+), ([^:()]+):(\d+)\)\s*$/;

/** A violation message without the trailing `(rule id, file:line)` the analyzer appends. */
export function cleanMessage(message: string): string {
  return message.replace(RULE_SUFFIX, '');
}

/** The `.tan` file and line a rule was declared at, when the message says so. */
export function ruleSourceOf(message: string): { file: string; line: number } | null {
  const match = RULE_SUFFIX.exec(message);
  if (!match || match[2] === undefined || match[3] === undefined) return null;
  return { file: match[2], line: Number(match[3]) };
}

/** The contract a rule id belongs to: `Controller/ctor/injects-only#0` -> `Controller`. */
export function contractOfRule(index: GraphIndex, ruleId: string): string {
  if (ruleId.startsWith('layer:')) return LAYERS_CONTRACT;
  const rule = index.rulesById.get(ruleId);
  if (rule) return rule.contract;
  const slash = ruleId.indexOf('/');
  return slash > 0 ? ruleId.slice(0, slash) : '';
}

// ---------------------------------------------------------------------------
// Issues

export type IssueGroup = {
  /** The rule id; also the group's stable key. */
  ruleId: string;
  contract: string;
  /** The rule's `.tan` text when known, otherwise the first cleaned message. */
  title: string;
  /** The first occurrence's message, cleaned. */
  message: string;
  codes: string[];
  /** The worst severity among the occurrences. */
  severity: string;
  tier: number | null;
  occurrences: Violation[];
  /** Distinct node ids, in first-seen order. */
  types: string[];
  files: string[];
  /** Where the rule is declared, parsed from the message. */
  source: { file: string; line: number } | null;
};

/**
 * Violations grouped by rule id, the way an issue tracker groups events.
 * Ordered worst severity first, then by occurrence count.
 */
export function issueGroups(index: GraphIndex): IssueGroup[] {
  const groups = new Map<string, IssueGroup>();

  for (const violation of index.graph.violations) {
    let group = groups.get(violation.ruleId);
    if (!group) {
      const rule = index.rulesById.get(violation.ruleId);
      const message = cleanMessage(violation.message);
      group = {
        ruleId: violation.ruleId,
        contract: contractOfRule(index, violation.ruleId),
        title: rule?.text ?? message,
        message,
        codes: [],
        severity: violation.severity,
        tier: rule?.tier ?? (violation.ruleId.startsWith('layer:') ? 1 : null),
        occurrences: [],
        types: [],
        files: [],
        source: ruleSourceOf(violation.message),
      };
      groups.set(violation.ruleId, group);
    }

    group.occurrences.push(violation);
    if (!group.codes.includes(violation.code)) group.codes.push(violation.code);
    if (severityRank(violation.severity) < severityRank(group.severity)) group.severity = violation.severity;
    if (violation.node !== '' && !group.types.includes(violation.node)) group.types.push(violation.node);
    if (!group.files.includes(violation.file)) group.files.push(violation.file);
  }

  return [...groups.values()].sort(
    (a, b) =>
      severityRank(a.severity) - severityRank(b.severity) ||
      b.occurrences.length - a.occurrences.length ||
      a.ruleId.localeCompare(b.ruleId),
  );
}

// ---------------------------------------------------------------------------
// Rules and the types they apply to

export type RuleStatus = 'pass' | 'fail' | 'ignored';

export type RuleApplication = {
  node: TangentNode;
  status: RuleStatus;
  violations: Violation[];
  ignores: Ignore[];
};

export type RuleSummary = {
  rule: Rule;
  applications: RuleApplication[];
  passing: number;
  failing: number;
  ignored: number;
  source: { file: string; line: number } | null;
};

export type ContractSummary = {
  id: string;
  file: string;
  line: number;
  extends: string | null;
  color: string;
  /** Types bound to this contract or to a contract that extends it. */
  types: TangentNode[];
  rules: RuleSummary[];
  failingTypes: number;
  violations: number;
};

/** Contract id -> the contract ids that extend it, directly or transitively, itself included. */
function descendants(index: GraphIndex): Map<string, Set<string>> {
  const parents = new Map(index.graph.contracts.map((c) => [c.id, c.extends] as const));
  const result = new Map<string, Set<string>>();
  for (const contract of index.graph.contracts) {
    let current: string | null | undefined = contract.id;
    const seen = new Set<string>();
    while (current && !seen.has(current)) {
      seen.add(current);
      let set = result.get(current);
      if (!set) {
        set = new Set();
        result.set(current, set);
      }
      set.add(contract.id);
      current = parents.get(current) ?? null;
    }
  }
  return result;
}

function byNodeAndRule<T extends { node: string; ruleId: string }>(items: T[]): Map<string, T[]> {
  const map = new Map<string, T[]>();
  for (const item of items) {
    const key = `${item.node}\u0000${item.ruleId}`;
    const list = map.get(key);
    if (list) list.push(item);
    else map.set(key, [item]);
  }
  return map;
}

/**
 * Every contract with the types it applies to and, per rule, which of those
 * types pass, fail or are ignored. A rule declared on a parent contract is
 * inherited unchanged (same id) by every contract that extends it.
 */
export function contractSummaries(index: GraphIndex): ContractSummary[] {
  const { graph } = index;
  const family = descendants(index);
  const violations = byNodeAndRule(graph.violations);
  const ignores = byNodeAndRule(graph.ignored ?? []);

  const sources = new Map<string, { file: string; line: number }>();
  for (const violation of graph.violations) {
    if (sources.has(violation.ruleId)) continue;
    const source = ruleSourceOf(violation.message);
    if (source) sources.set(violation.ruleId, source);
  }

  return graph.contracts.map((contract) => {
    const members = family.get(contract.id) ?? new Set([contract.id]);
    const types = graph.nodes.filter((node) => node.contracts.some((c) => members.has(c)));

    const rules = (graph.rules ?? [])
      .filter((rule) => rule.contract === contract.id)
      .map((rule): RuleSummary => {
        const applications = types.map((node): RuleApplication => {
          const key = `${node.id}\u0000${rule.id}`;
          const failed = violations.get(key) ?? [];
          const ignored = ignores.get(key) ?? [];
          const status: RuleStatus = failed.length > 0 ? 'fail' : ignored.length > 0 ? 'ignored' : 'pass';
          return { node, status, violations: failed, ignores: ignored };
        });
        applications.sort(
          (a, b) => statusRank(a.status) - statusRank(b.status) || a.node.name.localeCompare(b.node.name),
        );
        return {
          rule,
          applications,
          passing: applications.filter((a) => a.status === 'pass').length,
          failing: applications.filter((a) => a.status === 'fail').length,
          ignored: applications.filter((a) => a.status === 'ignored').length,
          source: sources.get(rule.id) ?? null,
        };
      });

    const failingIds = new Set<string>();
    let violationCount = 0;
    for (const violation of graph.violations) {
      if (contractOfRule(index, violation.ruleId) !== contract.id) continue;
      violationCount++;
      failingIds.add(violation.node);
    }

    return {
      id: contract.id,
      file: contract.file,
      line: contract.line,
      extends: contract.extends,
      color: index.contractColors.get(contract.id) ?? '#8b94a7',
      types,
      rules,
      failingTypes: failingIds.size,
      violations: violationCount,
    };
  });
}

function statusRank(status: RuleStatus): number {
  return status === 'fail' ? 0 : status === 'ignored' ? 1 : 2;
}

export type LayerRuleSummary = {
  from: string;
  allowed: string[];
  violations: Violation[];
  /** Types in this layer. */
  types: TangentNode[];
};

/** The layer matrix as rules: what each layer may depend on and what broke it. */
export function layerSummaries(index: GraphIndex): LayerRuleSummary[] {
  return index.graph.layers.map((layer) => ({
    from: layer.id,
    allowed: layer.allowedDependencies,
    violations: index.graph.violations.filter((v) => v.ruleId.startsWith(`layer:${layer.id}->`)),
    types: index.graph.nodes.filter((node) => node.layer === layer.id),
  }));
}

// ---------------------------------------------------------------------------
// Coverage

export type ProjectCoverage = {
  project: string;
  files: SourceFile[];
  tracked: number;
  partial: number;
  untracked: number;
  violations: number;
  /** Declared types, and how many of them are bound to a contract. */
  types: number;
  boundTypes: number;
};

export type CoverageFilter = 'all' | 'untracked' | 'partial' | 'tracked' | 'violating';

export function matchesCoverageFilter(file: SourceFile, filter: CoverageFilter): boolean {
  switch (filter) {
    case 'all':
      return true;
    case 'violating':
      return file.violations > 0;
    default:
      return file.status === filter;
  }
}

/** C# files per project, with how much of each project the contracts cover. */
export function projectCoverage(index: GraphIndex): ProjectCoverage[] {
  const bound = new Set(index.graph.nodes.filter((n) => n.contracts.length > 0).map((n) => n.id));
  const byProject = new Map<string, SourceFile[]>();

  for (const file of index.graph.files ?? []) {
    if (file.kind !== 'csharp') continue;
    const project = file.project ?? '(no project)';
    const list = byProject.get(project);
    if (list) list.push(file);
    else byProject.set(project, [file]);
  }

  return [...byProject.entries()]
    .map(([project, files]) => {
      let types = 0;
      let boundTypes = 0;
      for (const file of files) {
        types += file.types.length;
        for (const id of file.types) if (bound.has(id)) boundTypes++;
      }
      return {
        project,
        files,
        tracked: files.filter((f) => f.status === 'tracked').length,
        partial: files.filter((f) => f.status === 'partial').length,
        untracked: files.filter((f) => f.status === 'untracked').length,
        violations: files.reduce((sum, f) => sum + f.violations, 0),
        types,
        boundTypes,
      };
    })
    .sort((a, b) => a.project.localeCompare(b.project));
}

/**
 * A file's path inside its project: strips the leading directories up to and
 * including the project folder, so `src/Orders.Api/Controllers/X.cs` becomes
 * `Controllers/X.cs`.
 */
export function pathInProject(file: SourceFile): string {
  if (file.project) {
    const marker = `/${file.project}/`;
    const at = `/${file.path}`.indexOf(marker);
    if (at >= 0) return `/${file.path}`.slice(at + marker.length);
  }
  return file.path;
}

// ---------------------------------------------------------------------------
// Rooted neighbourhoods

export type Direction = 'out' | 'in' | 'both';

export type Neighbourhood = {
  /** Node ids to draw, roots included. */
  ids: Set<string>;
  /** Hop distance from the nearest root. */
  depthOf: Map<string, number>;
  /** Nodes reachable within the depth that the cap left out. */
  truncated: number;
};

/**
 * Breadth-first walk from `roots`, at most `depth` hops, following edges in
 * `direction`, keeping at most `cap` nodes. Nearer nodes always win over
 * farther ones, so truncation only ever drops the outer ring.
 */
export function neighbourhood(
  index: GraphIndex,
  roots: string[],
  depth: number,
  direction: Direction,
  cap: number,
  include: (node: TangentNode) => boolean = () => true,
): Neighbourhood {
  const ids = new Set<string>();
  const depthOf = new Map<string, number>();
  const reachable = new Set<string>();

  let frontier: string[] = [];
  for (const root of roots) {
    if (!index.nodesById.has(root) || reachable.has(root)) continue;
    reachable.add(root);
    frontier.push(root);
    if (ids.size < cap) {
      ids.add(root);
      depthOf.set(root, 0);
    }
  }

  for (let hop = 1; hop <= depth && frontier.length > 0; hop++) {
    const next: string[] = [];
    for (const id of frontier) {
      const targets: string[] = [];
      if (direction !== 'in') for (const edge of index.outgoing.get(id) ?? []) targets.push(edge.to);
      if (direction !== 'out') for (const edge of index.incoming.get(id) ?? []) targets.push(edge.from);

      for (const target of targets) {
        if (reachable.has(target)) continue;
        const node = index.nodesById.get(target);
        if (!node || !include(node)) continue;
        reachable.add(target);
        next.push(target);
        if (ids.size < cap) {
          ids.add(target);
          depthOf.set(target, hop);
        }
      }
    }
    frontier = next;
  }

  return { ids, depthOf, truncated: reachable.size - ids.size };
}

/**
 * The default graph root: every type with a violation, or every bound type
 * when nothing is broken, so the first view is never the whole solution.
 */
export function defaultRoots(index: GraphIndex): string[] {
  const failing = index.graph.nodes.filter((n) => n.violations > 0).map((n) => n.id);
  if (failing.length > 0) return failing;
  return index.graph.nodes.filter((n) => n.contracts.length > 0).map((n) => n.id);
}

/** Resolves a root spec from the URL: `type:<id>`, `contract:<id>` or `layer:<id>`. */
export function rootsOf(index: GraphIndex, spec: string | null): string[] {
  if (spec === null || spec === '') return defaultRoots(index);
  const colon = spec.indexOf(':');
  const kind = colon > 0 ? spec.slice(0, colon) : 'type';
  const id = colon > 0 ? spec.slice(colon + 1) : spec;
  switch (kind) {
    case 'contract':
      return index.graph.nodes.filter((n) => n.contracts.includes(id)).map((n) => n.id);
    case 'layer':
      return index.graph.nodes.filter((n) => n.layer === id).map((n) => n.id);
    default:
      return index.nodesById.has(id) ? [id] : [];
  }
}

// ---------------------------------------------------------------------------
// Editor links

/** A `vscode://file/` link, when the graph carries the absolute root. */
export function editorLink(root: string | undefined, file: string, line: number, col = 1): string | null {
  if (!root || file === '') return null;
  const absolute = /^[A-Za-z]:\//.test(file) || file.startsWith('/') ? file : `${root.replace(/\/$/, '')}/${file}`;
  return `vscode://file/${absolute}:${line}:${col}`;
}
