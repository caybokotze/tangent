import { ExternalLink } from 'lucide-react';
import { useMemo } from 'react';
import { cn } from '../lib/cn';
import { useHighlighted, type Lang } from '../lib/highlight';

export type CodeFrameProps = {
  /** Source lines, without terminators. */
  lines: string[];
  /** 1-based number of `lines[0]`. */
  startLine: number;
  /** Lines to mark, 1-based. */
  highlight?: number[];
  lang: Lang;
  /** Shown in the header; usually the file path. */
  title?: string;
  /** Opens the file in an editor. */
  href?: string | null;
  /** Show at most this many lines around the first highlighted line. */
  window?: number;
  className?: string;
};

/**
 * A read-only code excerpt with line numbers and the offending line marked,
 * highlighted by Shiki once it has loaded (plain text until then).
 */
export function CodeFrame(props: CodeFrameProps): JSX.Element {
  const { lines, startLine, highlight = [], lang, title, href, window: size } = props;

  const [from, to] = useMemo(() => {
    if (size === undefined || highlight.length === 0) return [0, lines.length];
    const focus = (highlight[0] ?? startLine) - startLine;
    const half = Math.floor(size / 2);
    const start = Math.max(0, Math.min(focus - half, lines.length - size));
    return [start, Math.min(lines.length, start + size)];
  }, [size, highlight, lines.length, startLine]);

  const code = useMemo(() => lines.join('\n'), [lines]);
  const tokens = useHighlighted(code, lang);
  const marked = new Set(highlight);
  const width = String(startLine + lines.length).length;

  return (
    <div className={cn('overflow-hidden rounded-lg border border-line bg-code', props.className)}>
      {(title || href) && (
        <div className="flex items-center justify-between gap-2 border-b border-line bg-surface-2 px-3 py-1.5">
          <span className="truncate font-mono text-[11px] text-muted">{title}</span>
          {href && (
            <a
              href={href}
              className="inline-flex shrink-0 items-center gap-1 text-[11px] font-medium text-accent-strong hover:underline"
            >
              Open in VS Code <ExternalLink className="size-3" />
            </a>
          )}
        </div>
      )}
      <div className="overflow-x-auto py-1.5">
        <pre className="min-w-max font-mono text-[12px] leading-[1.6]">
          {lines.slice(from, to).map((text, offset) => {
            const i = from + offset;
            const number = startLine + i;
            const hit = marked.has(number);
            const line = tokens?.[i];
            return (
              <div key={number} className={cn('flex pr-4', hit && 'bg-code-hit')}>
                <span
                  className={cn(
                    'sticky left-0 shrink-0 border-r pr-3 pl-3 text-right tabular-nums select-none',
                    hit ? 'border-danger bg-code-hit text-danger' : 'border-transparent bg-code text-faint',
                  )}
                  style={{ minWidth: `${width + 2.5}ch` }}
                >
                  {number}
                </span>
                <code className="pl-4 whitespace-pre">
                  {line
                    ? line.map((token, t) => (
                        <span
                          key={t}
                          className="shiki-token"
                          style={{ ['--shiki-dark' as string]: token.dark, ['--shiki-light' as string]: token.light }}
                        >
                          {token.content}
                        </span>
                      ))
                    : text || ' '}
                </code>
              </div>
            );
          })}
        </pre>
      </div>
    </div>
  );
}

/** A single highlighted snippet of DSL, for rule text. */
export function InlineCode(props: { code: string; lang: Lang; className?: string }): JSX.Element {
  const tokens = useHighlighted(props.code, props.lang);
  return (
    <code className={cn('font-mono text-[12px]', props.className)}>
      {tokens
        ? tokens.map((line, i) => (
            <span key={i}>
              {i > 0 && '\n'}
              {line.map((token, t) => (
                <span
                  key={t}
                  className="shiki-token"
                  style={{ ['--shiki-dark' as string]: token.dark, ['--shiki-light' as string]: token.light }}
                >
                  {token.content}
                </span>
              ))}
            </span>
          ))
        : props.code}
    </code>
  );
}
