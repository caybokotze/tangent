import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { buildIndex } from '../derive';
import {
  LAYERS_CONTRACT,
  cleanMessage,
  contractSummaries,
  defaultRoots,
  editorLink,
  issueGroups,
  layerSummaries,
  matchesCoverageFilter,
  neighbourhood,
  pathInProject,
  projectCoverage,
  ruleSourceOf,
  rootsOf,
} from '../explore';
import { parseGraphText } from '../parse';
import { edge, makeGraph, node } from './fixtures';

/** The real export of demo/Scheduling (`tangent graph --include-unbound`). */
const schedulingPath = fileURLToPath(new URL('../../../public/scheduling.graph.json', import.meta.url));
const scheduling = buildIndex(parseGraphText(readFileSync(schedulingPath, 'utf8')));

describe('messages', () => {
  const message =
    'Controller: type must be sealed. (rule Controller/type/modifier#0, controllers.tan:8)';

  it('strips the rule suffix', () => {
    expect(cleanMessage(message)).toBe('Controller: type must be sealed.');
    expect(cleanMessage('no suffix here')).toBe('no suffix here');
  });

  it('reads where the rule was declared', () => {
    expect(ruleSourceOf(message)).toEqual({ file: 'controllers.tan', line: 8 });
    expect(ruleSourceOf('no suffix here')).toBeNull();
  });
});

describe('issueGroups', () => {
  const groups = issueGroups(scheduling);

  it('groups every violation under its rule id', () => {
    expect(groups.reduce((sum, g) => sum + g.occurrences.length, 0)).toBe(scheduling.graph.violations.length);
    expect(new Set(groups.map((g) => g.ruleId)).size).toBe(groups.length);
  });

  it('titles a contract rule with its .tan text and a layer rule with its message', () => {
    const sealed = groups.find((g) => g.ruleId === 'Controller/type/modifier#0');
    expect(sealed?.contract).toBe('Controller');
    expect(sealed?.title).toBe(scheduling.rulesById.get('Controller/type/modifier#0')?.text);
    expect(sealed?.source).toEqual({ file: 'controllers.tan', line: 8 });

    const layer = groups.find((g) => g.ruleId.startsWith('layer:'));
    expect(layer?.contract).toBe(LAYERS_CONTRACT);
    expect(layer?.title).toMatch(/^Layer Api may not depend on layer Infrastructure/);
    expect(layer?.types).toContain('Scheduling.Api.Diagnostics.SeedInspector');
  });
});

describe('contractSummaries', () => {
  const summaries = contractSummaries(scheduling);
  const controller = summaries.find((c) => c.id === 'Controller');

  it('lists the types each contract applies to', () => {
    const names = controller?.types.map((t) => t.name) ?? [];
    expect(names).toContain('AppointmentsController');
    expect(names).toContain('AvailabilityController');
  });

  it('marks each type pass, fail or exempt per rule', () => {
    const sealed = controller?.rules.find((r) => r.rule.id === 'Controller/type/modifier#0');
    expect(sealed?.failing).toBe(1);
    expect(sealed?.applications[0]?.status).toBe('fail');
    expect(sealed?.applications[0]?.node.name).toBe('AvailabilityController');
    expect((sealed?.passing ?? 0) + (sealed?.failing ?? 0) + (sealed?.exempt ?? 0)).toBe(controller?.types.length);

    const returns = controller?.rules.find((r) => r.rule.id === 'Controller/methods/returns#0');
    const exempt = returns?.applications.find((a) => a.status === 'exempt');
    expect(exempt?.node.name).toBe('AppointmentsController');
    expect(exempt?.exemptions[0]?.reason).toMatch(/calendar feed/);
  });

  it('counts the violations and failing types of a contract', () => {
    expect(controller?.violations).toBe(2);
    expect(controller?.failingTypes).toBe(2);
  });

  it('applies an inherited rule to the types of the child contract', () => {
    const graph = makeGraph({
      contracts: [
        { id: 'Entity', file: '.tangent/e.tan', line: 1, extends: null, ruleCount: 1 },
        { id: 'AggregateRoot', file: '.tangent/e.tan', line: 5, extends: 'Entity', ruleCount: 0 },
      ],
      rules: [{ id: 'Entity/type/modifier#0', contract: 'Entity', text: 'require sealed', tier: 0 }],
      nodes: [node('A.Order', 'Domain', ['AggregateRoot'], 0), node('A.Line', 'Domain', ['Entity'], 0)],
      edges: [],
      violations: [],
    });
    const entity = contractSummaries(buildIndex(graph)).find((c) => c.id === 'Entity');
    expect(entity?.types.map((t) => t.id).sort()).toEqual(['A.Line', 'A.Order']);
    expect(entity?.rules[0]?.passing).toBe(2);
  });
});

describe('layerSummaries', () => {
  it('attaches layer violations to the layer they start in', () => {
    const api = layerSummaries(scheduling).find((l) => l.from === 'Api');
    expect(api?.violations).toHaveLength(1);
    expect(api?.allowed).not.toContain('Infrastructure');
  });
});

describe('projectCoverage', () => {
  const projects = projectCoverage(scheduling);

  it('covers every C# file exactly once', () => {
    const files = projects.flatMap((p) => p.files);
    const csharp = (scheduling.graph.files ?? []).filter((f) => f.kind === 'csharp');
    expect(files).toHaveLength(csharp.length);
  });

  it('counts statuses and bound types per project', () => {
    const api = projects.find((p) => p.project === 'Scheduling.Api');
    expect(api).toBeDefined();
    expect((api?.tracked ?? 0) + (api?.partial ?? 0) + (api?.untracked ?? 0)).toBe(api?.files.length);
    expect(api?.boundTypes).toBeLessThanOrEqual(api?.types ?? 0);
    expect(api?.violations).toBeGreaterThan(0);
  });

  it('shortens paths to the part inside the project', () => {
    const file = (scheduling.graph.files ?? []).find((f) => f.path.endsWith('AvailabilityController.cs'));
    expect(file && pathInProject(file)).toBe('Controllers/AvailabilityController.cs');
  });

  it('filters by status and by violations', () => {
    const file = (scheduling.graph.files ?? []).find((f) => f.path.endsWith('SeedInspector.cs'));
    expect(file && matchesCoverageFilter(file, 'untracked')).toBe(true);
    expect(file && matchesCoverageFilter(file, 'violating')).toBe(true);
    expect(file && matchesCoverageFilter(file, 'tracked')).toBe(false);
  });
});

describe('neighbourhood', () => {
  // a -> b -> c -> d, and e -> b
  const index = buildIndex(
    makeGraph({
      nodes: ['a', 'b', 'c', 'd', 'e'].map((id) => node(id, null, [], 0)),
      edges: [edge('a', 'b'), edge('b', 'c'), edge('c', 'd'), edge('e', 'b')],
      violations: [],
    }),
  );

  it('walks downstream to the given depth', () => {
    expect([...neighbourhood(index, ['b'], 1, 'out', 100).ids].sort()).toEqual(['b', 'c']);
    expect([...neighbourhood(index, ['b'], 2, 'out', 100).ids].sort()).toEqual(['b', 'c', 'd']);
  });

  it('walks upstream and both ways', () => {
    expect([...neighbourhood(index, ['b'], 1, 'in', 100).ids].sort()).toEqual(['a', 'b', 'e']);
    expect([...neighbourhood(index, ['b'], 1, 'both', 100).ids].sort()).toEqual(['a', 'b', 'c', 'e']);
  });

  it('records the hop distance', () => {
    const result = neighbourhood(index, ['a'], 3, 'out', 100);
    expect(result.depthOf.get('a')).toBe(0);
    expect(result.depthOf.get('d')).toBe(3);
  });

  it('caps the result and reports what it left out, nearest first', () => {
    const result = neighbourhood(index, ['b'], 1, 'both', 2);
    expect(result.ids.size).toBe(2);
    expect(result.ids.has('b')).toBe(true);
    expect(result.truncated).toBe(2);
  });

  it('skips nodes the filter excludes', () => {
    const result = neighbourhood(index, ['a'], 3, 'out', 100, (n) => n.id !== 'c');
    expect([...result.ids].sort()).toEqual(['a', 'b']);
  });
});

describe('roots', () => {
  it('defaults to the types that have violations', () => {
    const roots = defaultRoots(scheduling);
    expect(roots.length).toBeGreaterThan(0);
    for (const id of roots) expect(scheduling.nodesById.get(id)?.violations).toBeGreaterThan(0);
  });

  it('resolves type, contract and layer specs', () => {
    expect(rootsOf(scheduling, 'type:Scheduling.Api.Diagnostics.SeedInspector')).toEqual([
      'Scheduling.Api.Diagnostics.SeedInspector',
    ]);
    expect(rootsOf(scheduling, 'contract:Controller').length).toBeGreaterThan(1);
    expect(rootsOf(scheduling, 'layer:Domain').every((id) => scheduling.nodesById.get(id)?.layer === 'Domain')).toBe(true);
    expect(rootsOf(scheduling, 'type:Nope')).toEqual([]);
  });
});

describe('editorLink', () => {
  it('needs the root', () => {
    expect(editorLink(undefined, 'src/A.cs', 3)).toBeNull();
  });

  it('joins the root and the relative path', () => {
    expect(editorLink('C:/src/Orders/', 'src/A.cs', 3, 9)).toBe('vscode://file/C:/src/Orders/src/A.cs:3:9');
  });

  it('keeps an absolute path', () => {
    expect(editorLink('C:/src/Orders', 'D:/lib/B.cs', 1)).toBe('vscode://file/D:/lib/B.cs:1:1');
  });
});
