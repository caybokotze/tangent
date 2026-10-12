# Tangent viewer

A static, read-only React app for `tangent.graph.json`, the export of `tangent graph`. It answers four
questions about a codebase, one view each:

| View | Question |
|---|---|
| **Issues** (landing) | What breaks the contracts, and where? Violations grouped by rule, like an issue tracker groups events. |
| **Rules** | What did we declare, and which types pass or fail each rule? |
| **Coverage** | Which files do the contracts describe, and which are untracked? |
| **Graph** | What does this type, contract or layer depend on, and what depends on it? |

One detail panel serves every view. Every type, rule, file, contract and layer in it is a link, so you
can click from an issue to the offending type, to its file, to the other types in that file, to the
rule it breaks, to the other types that rule applies to, and so on. The selection lives in the URL
hash, so the back button works and any view can be shared as a link.

There is no backend: the graph is read in the browser, from a file you pick or a URL in the query
string.

The hosted copy is <https://caybokotze.github.io/tangent/>. `.github/workflows/pages.yml` builds and
deploys it when `viewer/` or the `.tan` grammar changes on `main`. It makes no network request
except to fetch its own files, so a graph opened there never leaves the browser.

## Running it

```bash
npm install
npm run dev        # http://localhost:5173
```

To look at the demo with code snippets and editor links, export it with source first:

```bash
cd ../demo/Scheduling
dotnet tangent graph Scheduling.slnx --include-unbound --include-source -o ../../viewer/public/scheduling.local.graph.json
```

and open <http://localhost:5173/?graph=/scheduling.local.graph.json>. `*.local.graph.json` is
git-ignored: `--include-source` records the absolute path of your checkout.

| script              | what it does                                    |
| ------------------- | ----------------------------------------------- |
| `npm run dev`       | Vite dev server with hot reload                 |
| `npm run build`     | typecheck, then emit a static site into `dist/` |
| `npm run preview`   | serve the built `dist/` locally                 |
| `npm test`          | run the unit tests once (Vitest)                |
| `npm run typecheck` | TypeScript in strict mode, no emit              |

`dist/` is plain static files with a relative base path, so it works from any web server, a CI
artifact, or a subdirectory.

## Loading a graph

- **File picker**: "Choose file" on the landing page, or "Open…" in the sidebar.
- **Drag and drop**: drop a `.json` file anywhere on the window.
- **Query string**: `?graph=<url>`, for example `?graph=/artifacts/tangent.graph.json`. The browser
  fetches it, so it must be reachable and CORS-enabled.
- **Examples** on the landing page: the Scheduling demo, the Orders sample, and a small hand-written
  graph.

Invalid documents are rejected with a precise message (`nodes[3].line: expected a number, got the
string "twelve"`). Survivable problems, such as a dangling edge, show as a banner.

## The views

**Issues.** One row per broken rule, worst severity first, then by event count. A row shows the rule's
`.tan` text, the first message, the contract, the diagnostic codes, the rule id and where the rule is
declared. Expand a row for its events; select it for the detail panel with the rule's `.tan` source and
the code of every event. Search covers rules, messages, types, files and codes; the chips filter by
contract. Sorting and expansion use TanStack Table.

**Rules.** Contracts on the left (plus **Layers**), rules on the right. Each rule shows its text, tier
and a pass/ignored/fail bar; expand it for every type it applies to and the result for each. A rule
declared on a parent contract applies to the types of every contract that extends it. The Layers page
lists what each layer may depend on and every dependency that breaks it.

**Coverage.** Per project: the share of declared types bound to a contract, and the files that are
tracked (every type bound), partial (some) and untracked (none, or no type at all). Below, a file tree
per project with the declared types (dashed when unbound), violation counts and status. The filter
narrows the tree to untracked, partial, tracked or violating files.

**Graph.** Never the whole solution: a **root** plus a bounded neighbourhood (TANGENT-PLAN.md §11.3).
The root is the types with violations (default), a contract, a layer, or one type (double-click a node,
or "Show in graph" in the panel). **Depth** is 1 to 4 hops; **direction** is depends-on, used-by or
both; a cap of 150 nodes truncates with an explicit "N more — Expand". Layers are swimlanes in
dependency order, laid out with elkjs. Illegal edges are red and animated. Selecting a type centres
and zooms on it.

**Code.** With `--include-source`, violations carry 15 lines either side and the `.tan` files their full
text. Both are highlighted by [Shiki](https://shiki.style) with the TextMate grammar that
`editors/vscode` ships, so `.tan` looks the same here as in VS Code. Each frame has an "Open in VS Code"
`vscode://file/` link. Without source the panel says how to get it.

**Theme.** Dark by default; the sidebar toggles light. The choice is kept in `localStorage`.

## Schema

The viewer reads versions 1 and 2 of the export (TANGENT-PLAN.md §11.1, `src/graph/types.ts`). Version 2
adds `files[]` (the file table), `fileId` on nodes and violations, `ignored[]`, and with
`--include-source` `root`, `violation.snippet` and `file.text`. Version 1 documents still load; the
Coverage view then says the export has no file table.

## Layout

```
viewer/
  public/                     example graphs
  src/
    graph/                    no React in here
      types.ts                the export schema
      parse.ts                validation with path-precise error messages
      derive.ts               indexes, colours, the v1 contract matrix
      explore.ts              issues by rule, rule applications, coverage, rooted neighbourhoods
      layout.ts               layer ordering + swimlane layout (elk, grid fallback)
      __tests__/              Vitest unit tests, including against the real demo export
    lib/
      route.ts                the URL hash: view, selection, graph root/depth/direction
      highlight.ts            lazy Shiki with C# and the .tan grammar
    app/context.tsx           derived models and navigation, shared by every view
    views/                    IssuesView, RulesView, CoverageView, GraphView
    components/               Shell, DetailPanel, CodeFrame, links, graph/GraphCanvas, ui
    hooks/useGraphSource.ts   file picker, drag-and-drop, ?graph= loading
    index.css                 Tailwind v4, the palette as CSS variables, React Flow overrides
```

Styling is Tailwind throughout, with the palette as CSS variables so the dark and light themes share
every class. Tooltips come from Radix; icons from lucide.
