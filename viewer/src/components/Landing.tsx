import { FileJson, Loader2, Upload } from 'lucide-react';
import { useRef, useState } from 'react';
import { cn } from '../lib/cn';
import { Logo } from './Shell';
import { Button } from './ui';

export type LandingProps = {
  loading: boolean;
  error: string | null;
  onLoadFile: (file: File) => void;
  onLoadUrl: (url: string) => void;
};

const EXAMPLES = [
  { file: 'scheduling.graph.json', title: 'Scheduling API', text: 'The demo in demo/Scheduling: 5 projects, 6 violations, 3 ignored.' },
  { file: 'orders.graph.json', title: 'Orders sample', text: 'samples/Orders: many deliberate violations across four layers.' },
  { file: 'sample.graph.json', title: 'Hand-written sample', text: 'A small fictional graph, schema version 1.' },
];

/** First-run page: file picker, drag-and-drop target and the bundled examples. */
export function Landing({ loading, error, onLoadFile, onLoadUrl }: LandingProps): JSX.Element {
  const fileInput = useRef<HTMLInputElement>(null);
  const [over, setOver] = useState(false);

  return (
    <div className="flex min-h-full items-center justify-center px-6 py-12">
      <div className="w-full max-w-2xl">
        <div className="flex items-center gap-3">
          <Logo className="size-10 text-accent" />
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-fg">Tangent</h1>
            <p className="text-muted">Explore your architecture contracts, what follows them, and what doesn’t.</p>
          </div>
        </div>

        <div
          className={cn(
            'mt-8 flex flex-col items-center gap-4 rounded-xl border-2 border-dashed px-6 py-10 text-center transition-colors',
            over ? 'border-accent bg-accent-soft' : 'border-line-strong bg-surface',
          )}
          onDragOver={(event) => {
            event.preventDefault();
            setOver(true);
          }}
          onDragLeave={() => setOver(false)}
          onDrop={(event) => {
            event.preventDefault();
            setOver(false);
            const file = event.dataTransfer.files[0];
            if (file) onLoadFile(file);
          }}
        >
          {loading ? <Loader2 className="size-8 animate-spin text-accent" /> : <FileJson className="size-8 text-faint" />}
          <div>
            <div className="font-semibold text-fg">{loading ? 'Loading…' : 'Drop a tangent.graph.json here'}</div>
            <div className="mt-1 text-muted">
              Export one with <code className="font-mono text-fg">tangent graph --include-unbound --include-source</code>
            </div>
          </div>
          <input
            ref={fileInput}
            type="file"
            accept="application/json,.json"
            hidden
            onChange={(event) => {
              const file = event.target.files?.[0];
              if (file) onLoadFile(file);
              event.target.value = '';
            }}
          />
          <Button variant="primary" onClick={() => fileInput.current?.click()}>
            <Upload className="size-4" /> Choose file
          </Button>
        </div>

        {error !== null && (
          <div className="mt-4 rounded-lg border border-danger/40 bg-danger-soft px-4 py-3 whitespace-pre-wrap text-danger">{error}</div>
        )}

        <div className="mt-8">
          <div className="text-[11px] font-semibold tracking-wider text-faint uppercase">Or open an example</div>
          <div className="mt-2 grid gap-3 sm:grid-cols-3">
            {EXAMPLES.map((example) => (
              <button
                key={example.file}
                type="button"
                onClick={() => onLoadUrl(`${import.meta.env.BASE_URL}${example.file}`)}
                className="rounded-lg border border-line bg-surface p-4 text-left transition-colors hover:border-accent hover:bg-surface-2"
              >
                <div className="font-semibold text-fg">{example.title}</div>
                <div className="mt-1 text-xs text-muted">{example.text}</div>
              </button>
            ))}
          </div>
        </div>

        <p className="mt-8 text-xs text-faint">
          Everything stays in your browser. To open a graph by URL, add <code className="font-mono">?graph=/path/to/tangent.graph.json</code>.
        </p>
      </div>
    </div>
  );
}
