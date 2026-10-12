import { useEffect, useState } from 'react';
import type { HighlighterCore, ThemedToken } from 'shiki/core';

export type Lang = 'csharp' | 'tangent' | 'text';

/** One highlighted line: tokens with a light and a dark colour each. */
export type TokenLine = { content: string; light?: string; dark?: string }[];

let highlighter: Promise<HighlighterCore | null> | null = null;

/**
 * Shiki, loaded on first use with only what the viewer needs: C#, the `.tan`
 * grammar the VS Code extension ships, two GitHub themes, and the JavaScript
 * regex engine (no WASM).
 */
function load(): Promise<HighlighterCore | null> {
  highlighter ??= (async () => {
    try {
      const [{ createHighlighterCore }, { createJavaScriptRegexEngine }, csharp, dark, light, tangent] =
        await Promise.all([
          import('shiki/core'),
          import('shiki/engine/javascript'),
          import('@shikijs/langs/csharp'),
          import('@shikijs/themes/github-dark-default'),
          import('@shikijs/themes/github-light-default'),
          import('../../../editors/vscode/syntaxes/tangent.tmLanguage.json'),
        ]);
      return await createHighlighterCore({
        engine: createJavaScriptRegexEngine(),
        themes: [dark.default, light.default],
        langs: [
          csharp.default,
          { ...(tangent.default as object), name: 'tangent', aliases: ['tan'] } as never,
        ],
      });
    } catch (error) {
      console.warn('[tangent] syntax highlighting is unavailable:', error);
      return null;
    }
  })();
  return highlighter;
}

function tokenize(engine: HighlighterCore, code: string, lang: Lang): TokenLine[] {
  const result = engine.codeToTokens(code, {
    lang,
    themes: { dark: 'github-dark-default', light: 'github-light-default' },
    defaultColor: false,
  });
  return result.tokens.map((line: ThemedToken[]) =>
    line.map((token) => {
      const style = (token.htmlStyle ?? {}) as Record<string, string>;
      return { content: token.content, dark: style['--shiki-dark'], light: style['--shiki-light'] };
    }),
  );
}

/** Highlighted lines for `code`, or null until Shiki is ready (render plain text meanwhile). */
export function useHighlighted(code: string, lang: Lang): TokenLine[] | null {
  const [lines, setLines] = useState<TokenLine[] | null>(null);

  useEffect(() => {
    if (lang === 'text') {
      setLines(null);
      return;
    }
    let cancelled = false;
    void load().then((engine) => {
      if (cancelled || !engine) return;
      try {
        setLines(tokenize(engine, code, lang));
      } catch {
        setLines(null);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [code, lang]);

  return lines;
}

export function langOf(path: string): Lang {
  if (path.endsWith('.cs')) return 'csharp';
  if (path.endsWith('.tan')) return 'tangent';
  return 'text';
}
