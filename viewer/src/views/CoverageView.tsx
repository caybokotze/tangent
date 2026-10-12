import { ChevronRight, FileCode2, Folder, Package } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useExplorer } from '../app/context';
import { Badge, CoverageBar, EmptyState, FileStatusBadge, Segmented, Stat } from '../components/ui';
import { matchesCoverageFilter, pathInProject, type CoverageFilter, type ProjectCoverage } from '../graph/explore';
import type { SourceFile } from '../graph/types';
import { cn, plural, shortName } from '../lib/cn';

type Folder = { name: string; path: string; folders: Folder[]; files: SourceFile[] };

function buildTree(files: SourceFile[]): Folder {
  const root: Folder = { name: '', path: '', folders: [], files: [] };
  for (const file of files) {
    const parts = pathInProject(file).split('/');
    parts.pop();
    let folder = root;
    for (const part of parts) {
      let next = folder.folders.find((f) => f.name === part);
      if (!next) {
        next = { name: part, path: `${folder.path}${part}/`, folders: [], files: [] };
        folder.folders.push(next);
      }
      folder = next;
    }
    folder.files.push(file);
  }
  const sort = (folder: Folder): void => {
    folder.folders.sort((a, b) => a.name.localeCompare(b.name));
    folder.files.sort((a, b) => a.path.localeCompare(b.path));
    folder.folders.forEach(sort);
  };
  sort(root);
  return root;
}

export function CoverageView(): JSX.Element {
  const { projects, index } = useExplorer();
  const [filter, setFilter] = useState<CoverageFilter>('all');

  const all = projects.flatMap((p) => p.files);
  const count = (f: CoverageFilter): number => all.filter((file) => matchesCoverageFilter(file, f)).length;
  const types = projects.reduce((sum, p) => sum + p.types, 0);
  const bound = projects.reduce((sum, p) => sum + p.boundTypes, 0);
  const contractFiles = (index.graph.files ?? []).filter((f) => f.kind === 'contract');

  if ((index.graph.files ?? []).length === 0) {
    return (
      <div className="mx-auto max-w-3xl px-6 py-10">
        <div className="rounded-lg border border-line bg-surface">
          <EmptyState icon={<FileCode2 className="size-8" />} title="This export has no file table">
            Coverage needs a graph exported by a current <code>tangent graph</code> (schema version 2). Re-run the export
            and open it again.
          </EmptyState>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-5 px-6 py-6">
      <header>
        <h1 className="text-xl font-semibold text-fg">Coverage</h1>
        <p className="mt-1 text-muted">
          Which source files your contracts describe. <em>Untracked</em> files declare no type that a contract binds;
          layer rules still apply to them.
        </p>
      </header>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Stat label="Types bound" value={types > 0 ? `${Math.round((bound / types) * 100)}%` : '–'} hint={`${bound} of ${types} declared types`} />
        <Stat label="Tracked files" value={count('tracked')} tone="success" hint="every type bound" />
        <Stat label="Partial files" value={count('partial')} tone="warning" hint="some types bound" />
        <Stat label="Untracked files" value={count('untracked')} hint={`${plural(contractFiles.length, 'contract file')} in .tangent`} />
      </div>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {projects.map((project) => (
          <ProjectCard key={project.project} project={project} />
        ))}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Segmented
          label="Filter files"
          value={filter}
          onChange={setFilter}
          options={[
            { value: 'all', label: 'All', count: all.length },
            { value: 'untracked', label: 'Untracked', count: count('untracked') },
            { value: 'partial', label: 'Partial', count: count('partial') },
            { value: 'tracked', label: 'Tracked', count: count('tracked') },
            { value: 'violating', label: 'With violations', count: count('violating') },
          ]}
        />
      </div>

      <div className="overflow-hidden rounded-lg border border-line bg-surface">
        {projects.map((project) => (
          <ProjectTree key={project.project} project={project} filter={filter} />
        ))}
      </div>
    </div>
  );
}

function ProjectCard(props: { project: ProjectCoverage }): JSX.Element {
  const { project } = props;
  const percent = project.types > 0 ? Math.round((project.boundTypes / project.types) * 100) : 0;
  return (
    <div className="rounded-lg border border-line bg-surface p-4">
      <div className="flex items-start gap-2">
        <Package className="mt-0.5 size-4 shrink-0 text-faint" />
        <span className="min-w-0 flex-1 font-semibold break-all text-fg">{project.project}</span>
      </div>
      {project.violations > 0 && (
        <div className="mt-1.5 pl-6">
          <Badge tone="danger">{plural(project.violations, 'violation')}</Badge>
        </div>
      )}
      <div className="mt-3 flex items-baseline gap-2">
        <span className="text-2xl font-semibold tabular-nums">{percent}%</span>
        <span className="text-muted">of types bound</span>
      </div>
      <CoverageBar tracked={project.tracked} partial={project.partial} untracked={project.untracked} className="mt-2" />
      <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] whitespace-nowrap text-muted tabular-nums">
        <span><span className="text-success">●</span> {project.tracked} tracked</span>
        <span><span className="text-warning">●</span> {project.partial} partial</span>
        <span><span className="text-faint">●</span> {project.untracked} untracked</span>
      </div>
    </div>
  );
}

function ProjectTree(props: { project: ProjectCoverage; filter: CoverageFilter }): JSX.Element | null {
  const files = useMemo(
    () => props.project.files.filter((file) => matchesCoverageFilter(file, props.filter)),
    [props.project.files, props.filter],
  );
  const tree = useMemo(() => buildTree(files), [files]);
  const [open, setOpen] = useState(true);
  if (files.length === 0) return null;

  return (
    <div className="border-b border-line last:border-b-0">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        className="flex w-full items-center gap-2 bg-surface-2 px-3 py-2 text-left"
      >
        <ChevronRight className={cn('size-4 text-faint transition-transform', open && 'rotate-90')} />
        <Package className="size-4 text-faint" />
        <span className="font-semibold text-fg">{props.project.project}</span>
        <span className="text-xs text-faint">{plural(files.length, 'file')}</span>
      </button>
      {open && <FolderRows folder={tree} depth={0} />}
    </div>
  );
}

function FolderRows(props: { folder: Folder; depth: number }): JSX.Element {
  return (
    <>
      {props.folder.folders.map((folder) => (
        <FolderRow key={folder.path} folder={folder} depth={props.depth} />
      ))}
      {props.folder.files.map((file) => (
        <FileRow key={file.id} file={file} depth={props.depth} />
      ))}
    </>
  );
}

function FolderRow(props: { folder: Folder; depth: number }): JSX.Element {
  const [open, setOpen] = useState(true);
  return (
    <>
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        className="flex w-full items-center gap-2 py-1.5 pr-3 text-left text-muted hover:bg-surface-2"
        style={{ paddingLeft: `${16 + props.depth * 18}px` }}
      >
        <ChevronRight className={cn('size-3.5 text-faint transition-transform', open && 'rotate-90')} />
        <Folder className="size-3.5 text-faint" />
        <span>{props.folder.name}</span>
      </button>
      {open && <FolderRows folder={props.folder} depth={props.depth + 1} />}
    </>
  );
}

function FileRow(props: { file: SourceFile; depth: number }): JSX.Element {
  const { file } = props;
  const { index, route, select } = useExplorer();
  const selected = route.selection?.kind === 'file' && route.selection.id === String(file.id);
  const name = file.path.slice(file.path.lastIndexOf('/') + 1);

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => select({ kind: 'file', id: String(file.id) })}
      onKeyDown={(event) => {
        if (event.key === 'Enter') select({ kind: 'file', id: String(file.id) });
      }}
      className={cn(
        'flex cursor-pointer items-center gap-3 border-t border-line/60 py-1.5 pr-3 hover:bg-surface-2',
        selected && 'bg-accent-soft hover:bg-accent-soft',
      )}
      style={{ paddingLeft: `${34 + props.depth * 18}px` }}
    >
      <FileCode2 className={cn('size-4 shrink-0', file.status === 'untracked' ? 'text-faint' : 'text-muted')} />
      <span className={cn('w-72 shrink-0 truncate font-mono text-[12px]', file.status === 'untracked' ? 'text-muted' : 'text-fg')}>
        {name}
      </span>
      <span className="flex min-w-0 flex-1 flex-wrap gap-1">
        {file.types.slice(0, 4).map((id) => {
          const node = index.nodesById.get(id);
          const bound = (node?.contracts.length ?? 0) > 0;
          return (
            <span
              key={id}
              className={cn(
                'rounded px-1.5 text-[11px]',
                bound ? 'bg-surface-3 text-fg' : 'border border-dashed border-line-strong text-faint',
              )}
              title={bound ? `${id}: ${node?.contracts.join(', ')}` : `${id}: no contract`}
            >
              {shortName(id)}
            </span>
          );
        })}
        {file.types.length > 4 && <span className="text-[11px] text-faint">+{file.types.length - 4}</span>}
        {file.types.length === 0 && <span className="text-[11px] text-faint italic">no types</span>}
      </span>
      {file.violations > 0 && <Badge tone="danger">{file.violations}</Badge>}
      <FileStatusBadge status={file.status} />
    </div>
  );
}
