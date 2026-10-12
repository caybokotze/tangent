import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { GraphIndex } from '../graph/derive';
import {
  contractSummaries,
  issueGroups,
  layerSummaries,
  projectCoverage,
  type ContractSummary,
  type IssueGroup,
  type LayerRuleSummary,
  type ProjectCoverage,
} from '../graph/explore';
import type { Ignore, SourceFile } from '../graph/types';
import type { Route, Selection } from '../lib/route';

/** Everything a view needs: the index, the derived models, and navigation. */
export type Explorer = {
  index: GraphIndex;
  issues: IssueGroup[];
  contracts: ContractSummary[];
  layers: LayerRuleSummary[];
  projects: ProjectCoverage[];
  filesById: Map<number, SourceFile>;
  filesByPath: Map<string, SourceFile>;
  ignoredByNode: Map<string, Ignore[]>;
  route: Route;
  navigate: (next: Partial<Route>) => void;
  select: (selection: Selection | null) => void;
};

const ExplorerContext = createContext<Explorer | null>(null);

export function ExplorerProvider(props: {
  index: GraphIndex;
  route: Route;
  navigate: (next: Partial<Route>) => void;
  children: ReactNode;
}): JSX.Element {
  const { index, route, navigate, children } = props;

  const derived = useMemo(() => {
    const files = index.graph.files ?? [];
    const ignoredByNode = new Map<string, Ignore[]>();
    for (const ignore of index.graph.ignored ?? []) {
      const list = ignoredByNode.get(ignore.node);
      if (list) list.push(ignore);
      else ignoredByNode.set(ignore.node, [ignore]);
    }
    return {
      issues: issueGroups(index),
      contracts: contractSummaries(index),
      layers: layerSummaries(index),
      projects: projectCoverage(index),
      filesById: new Map(files.map((f) => [f.id, f] as const)),
      filesByPath: new Map(files.map((f) => [f.path, f] as const)),
      ignoredByNode,
    };
  }, [index]);

  const value = useMemo<Explorer>(
    () => ({
      index,
      ...derived,
      route,
      navigate,
      select: (selection) => navigate({ selection }),
    }),
    [index, derived, route, navigate],
  );

  return <ExplorerContext.Provider value={value}>{children}</ExplorerContext.Provider>;
}

export function useExplorer(): Explorer {
  const value = useContext(ExplorerContext);
  if (!value) throw new Error('useExplorer must be used inside <ExplorerProvider>');
  return value;
}

/** The file a node or violation points at: by id when the graph has a file table, else by path. */
export function useFileOf(): (item: { fileId?: number; file: string }) => SourceFile | undefined {
  const { filesById, filesByPath } = useExplorer();
  return (item) =>
    item.fileId !== undefined && item.fileId >= 0 ? filesById.get(item.fileId) : filesByPath.get(item.file);
}
