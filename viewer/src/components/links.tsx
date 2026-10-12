import { FileCode2, ScrollText } from 'lucide-react';
import type { ReactNode } from 'react';
import { useExplorer } from '../app/context';
import { cn, shortName } from '../lib/cn';
import { nodeColor } from '../graph/derive';

const linkClass =
  'inline-flex min-w-0 items-center gap-1.5 rounded text-left text-fg decoration-accent/60 underline-offset-2 hover:text-accent-strong hover:underline';

/** A type name that opens the type in the detail panel. */
export function TypeLink(props: { id: string; className?: string; full?: boolean }): JSX.Element {
  const { index, select } = useExplorer();
  const node = index.nodesById.get(props.id);
  const color = node ? nodeColor(index, node) : 'var(--faint)';
  return (
    <button
      type="button"
      className={cn(linkClass, 'font-medium', props.className)}
      onClick={(event) => {
        event.stopPropagation();
        select({ kind: 'type', id: props.id });
      }}
      title={props.id}
    >
      <span className="size-2 shrink-0 rounded-sm" style={{ background: color }} />
      <span className="truncate">{props.full ? props.id : node?.name ?? shortName(props.id)}</span>
    </button>
  );
}

/** A file path that opens the file in the detail panel. */
export function FileLink(props: { path: string; line?: number; className?: string; children?: ReactNode }): JSX.Element {
  const { filesByPath, select } = useExplorer();
  const file = filesByPath.get(props.path);
  const label = props.children ?? `${props.path}${props.line ? `:${props.line}` : ''}`;
  if (!file) {
    return <span className={cn('truncate font-mono text-[12px] text-muted', props.className)}>{label}</span>;
  }
  return (
    <button
      type="button"
      className={cn(linkClass, 'font-mono text-[12px] text-muted', props.className)}
      onClick={(event) => {
        event.stopPropagation();
        select({ kind: 'file', id: String(file.id) });
      }}
      title={props.path}
    >
      <FileCode2 className="size-3.5 shrink-0" />
      <span className="truncate">{label}</span>
    </button>
  );
}

/** A rule id that opens the rule in the detail panel. */
export function RuleLink(props: { id: string; className?: string; children?: ReactNode }): JSX.Element {
  const { select } = useExplorer();
  return (
    <button
      type="button"
      className={cn(linkClass, 'font-mono text-[12px]', props.className)}
      onClick={(event) => {
        event.stopPropagation();
        select({ kind: 'rule', id: props.id });
      }}
      title={props.id}
    >
      <ScrollText className="size-3.5 shrink-0 text-faint" />
      <span className="truncate">{props.children ?? props.id}</span>
    </button>
  );
}
