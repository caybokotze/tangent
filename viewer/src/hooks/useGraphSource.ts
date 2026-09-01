import { useCallback, useEffect, useRef, useState } from 'react';
import { GraphParseError, parseGraphText } from '../graph/parse';
import type { TangentGraph } from '../graph/types';

export type GraphSource = {
  graph: TangentGraph | null;
  /** Where the current graph came from: a file name or a URL. */
  sourceName: string | null;
  loading: boolean;
  error: string | null;
  loadFile: (file: File) => Promise<void>;
  loadUrl: (url: string) => Promise<void>;
  loadSample: () => Promise<void>;
  clear: () => void;
};

function describeError(error: unknown): string {
  if (error instanceof GraphParseError) return error.message;
  if (error instanceof Error) return error.message;
  return String(error);
}

/** URL of the bundled sample graph, honouring Vite's base path. */
export function sampleUrl(): string {
  return `${import.meta.env.BASE_URL}sample.graph.json`;
}

/**
 * Owns the loaded graph. Reads `?graph=<url>` on first render, and exposes
 * loaders for the file picker, drag-and-drop and the sample button.
 */
export function useGraphSource(): GraphSource {
  const [graph, setGraph] = useState<TangentGraph | null>(null);
  const [sourceName, setSourceName] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestId = useRef(0);

  const loadUrl = useCallback(async (url: string): Promise<void> => {
    const id = ++requestId.current;
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(url);
      if (!response.ok) {
        throw new Error(`could not fetch ${url}: ${response.status} ${response.statusText}`);
      }
      const parsed = parseGraphText(await response.text());
      if (id !== requestId.current) return;
      setGraph(parsed);
      setSourceName(url);
    } catch (caught) {
      if (id !== requestId.current) return;
      setGraph(null);
      setSourceName(null);
      setError(describeError(caught));
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, []);

  const loadFile = useCallback(async (file: File): Promise<void> => {
    const id = ++requestId.current;
    setLoading(true);
    setError(null);
    try {
      const parsed = parseGraphText(await file.text());
      if (id !== requestId.current) return;
      setGraph(parsed);
      setSourceName(file.name);
    } catch (caught) {
      if (id !== requestId.current) return;
      setGraph(null);
      setSourceName(null);
      setError(`${file.name}: ${describeError(caught)}`);
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, []);

  const loadSample = useCallback(() => loadUrl(sampleUrl()), [loadUrl]);

  const clear = useCallback(() => {
    requestId.current++;
    setGraph(null);
    setSourceName(null);
    setError(null);
    setLoading(false);
  }, []);

  useEffect(() => {
    const fromQuery = new URLSearchParams(window.location.search).get('graph');
    if (fromQuery !== null && fromQuery !== '') void loadUrl(fromQuery);
  }, [loadUrl]);

  return { graph, sourceName, loading, error, loadFile, loadUrl, loadSample, clear };
}
