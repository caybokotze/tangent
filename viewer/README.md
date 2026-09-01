# Tangent graph viewer

A static, read-only React app that renders `tangent.graph.json`, the architecture
graph exported by [Tangent](../TANGENT-PLAN.md). It shows layers as swimlanes,
types coloured by contract, illegal dependencies in red, and every violation with
its rule text.

There is no backend: the graph is read in the browser, either from a file you
pick or from a URL you pass in the query string.

## Running it

```bash
npm install
npm run dev        # http://localhost:5173
```

| script              | what it does                                       |
| ------------------- | -------------------------------------------------- |
| `npm run dev`       | Vite dev server with hot reload                     |
| `npm run build`     | typecheck, then emit a static site into `dist/`     |
| `npm run preview`   | serve the built `dist/` locally                     |
| `npm test`          | run the unit tests once (Vitest)                    |
| `npm run typecheck` | TypeScript in strict mode, no emit                  |

`dist/` is plain static files, so it can be served from any web server, a CI
artifact, or a GitHub Pages branch. The build uses a relative base path, so it
also works from a subdirectory.

## Loading a graph

Three ways, all equivalent:

- **File picker** - "Choose file" on the landing panel, or "Open..." in the toolbar.
- **Drag and drop** - drop a `.json` file anywhere on the window.
- **Query string** - `?graph=<url>`, for example
  `http://localhost:5173/?graph=/artifacts/tangent.graph.json`. The URL is fetched
  by the browser, so it has to be reachable and CORS-enabled. Relative URLs work
  and are resolved against the page.

"Load sample" opens `public/sample.graph.json`, a fictional four-layer "Orders"
solution with 22 types, 38 edges (3 of them illegal), 12 violations across six
contracts and 2 exemptions. Use it to see what the viewer does before pointing it
at a real export.

Invalid documents are rejected with a precise message - `nodes[3].line: expected a
number, got the string "twelve"` - rather than rendering half a graph. Problems
that are survivable (a dangling edge, an unknown contract id) show up as a banner
above the canvas.

## The views

**Graph** (default). One swimlane per layer, stacked in dependency order:
dependants on top, the layers they depend on below, with types that match no
layer in an "Unlayered" lane at the bottom. Lane contents are laid out with
[elkjs](https://github.com/kieler/elkjs); types are coloured by their first
contract (see the legend) and carry a red badge with the violation count and a
grey badge with the exemption count. Edges that break the declared layer
direction are drawn red and on top of everything else; allowed references are
muted and thin. Click a node to open the side panel; click the background to
close it.

**Side panel**. For the selected type: `file:line`, layer, contracts, exemption
count, reference counts, every violation (diagnostic code, severity, rule id,
message, `file:line:col`, and the `.tan` rule text when the export includes a
`rules` array), the member list, and its dependencies and dependents as links you
can follow.

**Contracts**. Pick a contract and get a matrix of bound types (rows) against
rule ids (columns). A cell fails when a violation cites that rule for that type;
hovering shows the failing messages, clicking a failing cell opens the type in the
side panel. Columns come from the `rules` array when the exporter emitted one,
plus any rule id seen in a violation for that contract.

**Filters**, shared by both views:

- **Search** by type name or full type id - matches are outlined, everything else
  is dimmed.
- **Focus** - show only the selected type and its 1-hop neighbourhood.
- **Unbound types** - hide types with no contract.
- **Only violations** - hide types with no violations.

## Schema

The viewer reads version 1 of the export described in TANGENT-PLAN.md section 11.1:
`version`, `project`, `generatedAt`, `layers[]`, `contracts[]`, `nodes[]`,
`edges[]` and `violations[]`. Two optional extras are understood:

- `node.layer` may be `null` for types that match no layer glob (what
  `--include-unbound` produces).
- a top-level `rules[]` array of `{ id, contract, text, tier }` so the side panel
  and the contract matrix can show the original `.tan` rule text.

## Layout

```
viewer/
  public/sample.graph.json   demo graph
  src/
    graph/                   no React in here
      types.ts               the export schema
      parse.ts               validation with path-precise error messages
      layout.ts              layer ordering + swimlane layout (elk, grid fallback)
      derive.ts              indexes, colours, filters, contract matrix
      __tests__/             Vitest unit tests
    components/              Toolbar, GraphCanvas, TypeNode, LaneNode, Legend,
                             SidePanel, ContractView, Landing
    hooks/useGraphSource.ts  file picker, drag-and-drop, ?graph= loading
    App.tsx                  state, filtering and layout orchestration
    styles.css               hand-written CSS, light and dark
```

The layout is deterministic and testable: `orderLayers` topologically sorts the
layers by `allowedDependencies` (tolerating cycles), and `computeLayout` places
nodes inside their lane with elkjs, falling back to a grid when elk is not
available - which is what the unit tests exercise.
