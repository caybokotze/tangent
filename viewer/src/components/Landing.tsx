import { useRef, useState } from 'react';

export type LandingProps = {
  loading: boolean;
  error: string | null;
  onLoadFile: (file: File) => void;
  onLoadSample: () => void;
};

/** First-run panel: file picker, drag-and-drop target and the bundled sample. */
export function Landing({ loading, error, onLoadFile, onLoadSample }: LandingProps): JSX.Element {
  const fileInput = useRef<HTMLInputElement>(null);
  const [over, setOver] = useState(false);

  return (
    <div className="landing">
      <div className="landing-card">
        <h1>Tangent graph viewer</h1>
        <p>
          Open a <code>tangent.graph.json</code> exported by <code>tangent graph</code> to explore
          layers, contracts and violations. Everything stays in your browser.
        </p>

        <div
          className={`dropzone ${over ? 'over' : ''}`}
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
          <div>{loading ? 'Loading...' : 'Drop tangent.graph.json here'}</div>
          <div className="actions">
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
            <button type="button" className="primary" onClick={() => fileInput.current?.click()}>
              Choose file
            </button>
            <button type="button" onClick={onLoadSample}>
              Load sample
            </button>
          </div>
        </div>

        <p className="hint">
          You can also point the viewer at a URL: <code>?graph=https://host/tangent.graph.json</code>
          . The file is fetched by the browser, so it must be reachable and CORS-enabled.
        </p>

        {error !== null && <div className="error">{error}</div>}
      </div>
    </div>
  );
}
