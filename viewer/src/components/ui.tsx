import * as TooltipPrimitive from '@radix-ui/react-tooltip';
import { AlertOctagon, AlertTriangle, CheckCircle2, CircleDashed, Info, ShieldOff, XCircle } from 'lucide-react';
import type { ButtonHTMLAttributes, ReactNode } from 'react';
import type { FileStatus } from '../graph/types';
import type { RuleStatus } from '../graph/explore';
import { cn } from '../lib/cn';

export function Tooltip(props: { content: ReactNode; children: ReactNode; side?: 'top' | 'bottom' | 'left' | 'right' }): JSX.Element {
  return (
    <TooltipPrimitive.Root delayDuration={250}>
      <TooltipPrimitive.Trigger asChild>{props.children}</TooltipPrimitive.Trigger>
      <TooltipPrimitive.Portal>
        <TooltipPrimitive.Content
          side={props.side ?? 'top'}
          sideOffset={6}
          className="z-50 max-w-xs rounded-md border border-line-strong bg-surface-3 px-2 py-1 text-xs text-fg shadow-lg"
        >
          {props.content}
        </TooltipPrimitive.Content>
      </TooltipPrimitive.Portal>
    </TooltipPrimitive.Root>
  );
}

export function Button(
  props: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'default' | 'primary' | 'ghost'; size?: 'sm' | 'md' },
): JSX.Element {
  const { variant = 'default', size = 'md', className, ...rest } = props;
  return (
    <button
      type="button"
      {...rest}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md font-medium whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        size === 'sm' ? 'h-7 px-2 text-xs' : 'h-8 px-3 text-[13px]',
        variant === 'primary' && 'bg-accent text-white hover:bg-accent-strong',
        variant === 'default' && 'border border-line bg-surface-2 text-fg hover:border-line-strong hover:bg-surface-3',
        variant === 'ghost' && 'text-muted hover:bg-surface-2 hover:text-fg',
        className,
      )}
    />
  );
}

export function Badge(props: { children: ReactNode; tone?: 'neutral' | 'danger' | 'warning' | 'success' | 'accent' | 'info'; className?: string; mono?: boolean }): JSX.Element {
  const tone = props.tone ?? 'neutral';
  return (
    <span
      className={cn(
        'inline-flex h-5 items-center gap-1 rounded px-1.5 text-[11px] font-medium whitespace-nowrap',
        props.mono && 'font-mono',
        tone === 'neutral' && 'bg-surface-3 text-muted',
        tone === 'danger' && 'bg-danger-soft text-danger',
        tone === 'warning' && 'bg-warning-soft text-warning',
        tone === 'success' && 'bg-success-soft text-success',
        tone === 'accent' && 'bg-accent-soft text-accent-strong',
        tone === 'info' && 'bg-info-soft text-info',
        props.className,
      )}
    >
      {props.children}
    </span>
  );
}

export function severityTone(severity: string): 'danger' | 'warning' | 'info' | 'neutral' {
  if (severity === 'error') return 'danger';
  if (severity === 'warning') return 'warning';
  if (severity === 'info') return 'info';
  return 'neutral';
}

export function SeverityIcon(props: { severity: string; className?: string }): JSX.Element {
  const className = cn('size-4 shrink-0', props.className);
  if (props.severity === 'error') return <AlertOctagon className={cn(className, 'text-danger')} aria-label="error" />;
  if (props.severity === 'warning') return <AlertTriangle className={cn(className, 'text-warning')} aria-label="warning" />;
  return <Info className={cn(className, 'text-info')} aria-label={props.severity} />;
}

/** The coloured bar at the left edge of an issue row. */
export function SeverityBar(props: { severity: string }): JSX.Element {
  return (
    <span
      aria-hidden
      className={cn(
        'absolute inset-y-0 left-0 w-[3px]',
        props.severity === 'error' ? 'bg-danger' : props.severity === 'warning' ? 'bg-warning' : 'bg-info',
      )}
    />
  );
}

export function RuleStatusIcon(props: { status: RuleStatus; className?: string }): JSX.Element {
  const className = cn('size-4 shrink-0', props.className);
  if (props.status === 'fail') return <XCircle className={cn(className, 'text-danger')} aria-label="fails" />;
  if (props.status === 'ignored') return <ShieldOff className={cn(className, 'text-faint')} aria-label="ignored" />;
  return <CheckCircle2 className={cn(className, 'text-success')} aria-label="passes" />;
}

const FILE_STATUS_LABEL: Record<FileStatus, string> = {
  tracked: 'Tracked',
  partial: 'Partial',
  untracked: 'Untracked',
  contract: 'Contract',
};

export function FileStatusBadge(props: { status: FileStatus }): JSX.Element {
  const tone = props.status === 'tracked' ? 'success' : props.status === 'partial' ? 'warning' : props.status === 'contract' ? 'accent' : 'neutral';
  return (
    <Badge tone={tone}>
      {props.status === 'untracked' ? <CircleDashed className="size-3" /> : null}
      {FILE_STATUS_LABEL[props.status]}
    </Badge>
  );
}

/** A contract name with its colour dot. */
export function ContractChip(props: { id: string; color: string; onClick?: () => void; className?: string }): JSX.Element {
  const content = (
    <>
      <span className="size-2 shrink-0 rounded-full" style={{ background: props.color }} />
      {props.id}
    </>
  );
  const className = cn(
    'inline-flex h-5 items-center gap-1.5 rounded border border-line bg-surface-2 px-1.5 text-[11px] font-medium text-fg',
    props.onClick && 'hover:border-line-strong hover:bg-surface-3',
    props.className,
  );
  return props.onClick ? (
    <button type="button" className={className} onClick={props.onClick}>
      {content}
    </button>
  ) : (
    <span className={className}>{content}</span>
  );
}

/** A horizontal stacked bar: green tracked, amber partial, grey untracked. */
export function CoverageBar(props: { tracked: number; partial: number; untracked: number; className?: string }): JSX.Element {
  const total = Math.max(1, props.tracked + props.partial + props.untracked);
  return (
    <div className={cn('flex h-1.5 w-full overflow-hidden rounded-full bg-surface-3', props.className)}>
      <div className="bg-success" style={{ width: `${(props.tracked / total) * 100}%` }} />
      <div className="bg-warning" style={{ width: `${(props.partial / total) * 100}%` }} />
    </div>
  );
}

export function PassBar(props: { passing: number; failing: number; ignored: number }): JSX.Element {
  const total = Math.max(1, props.passing + props.failing + props.ignored);
  return (
    <div className="flex h-1.5 w-24 overflow-hidden rounded-full bg-surface-3">
      <div className="bg-success" style={{ width: `${(props.passing / total) * 100}%` }} />
      <div className="bg-faint" style={{ width: `${(props.ignored / total) * 100}%` }} />
      <div className="bg-danger" style={{ width: `${(props.failing / total) * 100}%` }} />
    </div>
  );
}

export function EmptyState(props: { icon?: ReactNode; title: string; children?: ReactNode }): JSX.Element {
  return (
    <div className="flex flex-col items-center justify-center gap-2 px-6 py-16 text-center">
      {props.icon && <div className="text-faint">{props.icon}</div>}
      <div className="text-sm font-medium text-fg">{props.title}</div>
      {props.children && <div className="max-w-md text-muted">{props.children}</div>}
    </div>
  );
}

export function SectionTitle(props: { children: ReactNode; right?: ReactNode }): JSX.Element {
  return (
    <div className="mb-2 flex items-center justify-between gap-2">
      <h3 className="text-[11px] font-semibold tracking-wider text-faint uppercase">{props.children}</h3>
      {props.right}
    </div>
  );
}

export function Stat(props: { label: string; value: ReactNode; tone?: 'danger' | 'success' | 'warning'; hint?: string }): JSX.Element {
  return (
    <div className="min-w-0 rounded-lg border border-line bg-surface px-4 py-3">
      <div className="text-[11px] font-medium tracking-wide text-faint uppercase">{props.label}</div>
      <div
        className={cn(
          'mt-1 text-2xl font-semibold tabular-nums',
          props.tone === 'danger' && 'text-danger',
          props.tone === 'success' && 'text-success',
          props.tone === 'warning' && 'text-warning',
        )}
      >
        {props.value}
      </div>
      {props.hint && <div className="mt-0.5 truncate text-xs text-muted">{props.hint}</div>}
    </div>
  );
}

/** A segmented control built on Radix ToggleGroup semantics, kept simple. */
export function Segmented<T extends string>(props: {
  value: T;
  options: { value: T; label: ReactNode; count?: number }[];
  onChange: (value: T) => void;
  label: string;
}): JSX.Element {
  return (
    <div role="radiogroup" aria-label={props.label} className="inline-flex rounded-md border border-line bg-surface-2 p-0.5">
      {props.options.map((option) => {
        const active = option.value === props.value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => props.onChange(option.value)}
            className={cn(
              'inline-flex h-6 items-center gap-1.5 rounded px-2 text-xs font-medium whitespace-nowrap transition-colors',
              active ? 'bg-surface-3 text-fg shadow-sm' : 'text-muted hover:text-fg',
            )}
          >
            {option.label}
            {option.count !== undefined && <span className="tabular-nums text-faint">{option.count}</span>}
          </button>
        );
      })}
    </div>
  );
}
