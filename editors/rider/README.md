# Tangent for Rider / JetBrains IDEs

Editor support for [Tangent](../../TANGENT-PLAN.md), an architectural conformance checker for .NET
whose contracts are written in `.tan` files. Same two features as the
[VS Code extension](../vscode/README.md): the `source.tangent` TextMate grammar, and the
`dotnet tangent serve` language server over stdio.

> [!WARNING]
> **The plugin in this directory has never been compiled.** It was written against the published
> JetBrains APIs on a machine with no JDK and no Gradle, so nothing here — not the Gradle build,
> not the Kotlin sources, not `plugin.xml` — has been run through a compiler or an IDE.
> Treat it as a reviewed draft. Expect to fix signatures on the first `./gradlew buildPlugin`.
> See [Unverified API points](#unverified-api-points) for the specific places to look.
>
> **The zero-build path below needs none of this** and works today with a stock Rider.

---

## Zero-build path (works today)

Two independent halves: highlighting via TextMate, and language-server features via LSP4IJ.
Do either or both.

### 1. Syntax highlighting — import the VS Code folder as a TextMate bundle

JetBrains IDEs read VS Code extension folders directly: they parse `package.json` and use
`contributes.languages` and `contributes.grammars`. `editors/vscode` already has both, so it can be
imported as-is — no packaging, no build.

1. <kbd>Settings</kbd> → **Editor** → **TextMate Bundles**.
2. Click **+** (*Add bundle*).
3. Select the **`editors/vscode`** folder of this repository (the folder that *contains*
   `package.json`, not the `syntaxes` folder and not the `.json` file itself).
4. Make sure the new **Tangent** entry is ticked, then **Apply**.
5. Open any `.tan` file. If it is still plain text, check
   **Settings** → **Editor** → **File Types** → **TextMate files** and add `*.tan` to
   *File name patterns*, or remove `*.tan` from whatever other file type has claimed it.

TextMate only takes over files whose extension is **unknown** to the IDE (or is plain text), so if
something else in your setup has registered `.tan`, step 5 is the fix.

The grammar is regex-only and knows nothing about declared names — it is the fallback for users
without the language server. `language-configuration.json` (comment toggling, brackets) is picked up
from the same folder.

### 2. Language server — LSP4IJ

The JetBrains LSP API is not usable without writing a plugin, so for the zero-build path use
[**LSP4IJ**](https://plugins.jetbrains.com/plugin/23257-lsp4ij) by Red Hat.

**Prerequisite:** the server ships with the Tangent CLI —

```
dotnet tool install -g Tangent.Cli
```

Check it is on `PATH` with `dotnet tangent --version`.

**Install LSP4IJ:** <kbd>Settings</kbd> → **Plugins** → **Marketplace** → search `LSP4IJ` → **Install**
→ restart.

**Add the server (UI steps — these are the reliable ones):**

1. <kbd>Settings</kbd> → **Languages & Frameworks** → **Language Servers**, click **+**
   (or: **View** → **Tool Windows** → **LSP Consoles**, then the *New Language Server* action).
2. **Server** tab:
   - **Name:** `Tangent`
   - **Command:** `dotnet tangent serve`
3. **Mappings** tab → **File name patterns** → **+**:
   - **File name pattern:** `*.tan`
   - **Language ID:** `tangent`  ← the LSP `languageId` the server receives

   If you prefer to map by IDE concept rather than by pattern, use the **IntelliJ File Type** row
   with file type **TextMate files** instead (the file type `.tan` resolves to once the TextMate
   bundle above is imported). The `*.tan` pattern is simpler and does not depend on step 1.
4. **OK**. Open a `.tan` file; the LSP Consoles tool window should show `Tangent` as *started*.

**Importing the same definition as JSON.** LSP4IJ can export and import user-defined servers as
*templates*: a directory (or a zip of such directories) where only `template.json` is required —
`settings.json`, `initializationOptions.json` and `README.md` are optional siblings. Save the
following as `tangent/template.json` and import it with the **Template** dropdown in the
*New Language Server* dialog (*Import from custom template…*), then point it at the `tangent`
folder:

```json
{
  "id": "tangent",
  "name": "Tangent",
  "programArgs": {
    "default": "sh -c \"dotnet tangent serve\"",
    "windows": "dotnet tangent serve"
  },
  "fileTypeMappings": [
    {
      "fileType": {
        "name": "Tangent",
        "patterns": ["*.tan"]
      },
      "languageId": "tangent"
    }
  ]
}
```

> This JSON matches LSP4IJ's documented `template.json` shape (`id`, `name`, `programArgs` with
> `default`/`windows` keys, `fileTypeMappings[].fileType.name` / `.patterns` / `.languageId`), but it
> has **not** been round-tripped through an actual LSP4IJ install. If the import is rejected, use the
> UI steps above — they are the supported path — and then use LSP4IJ's own **Export** action on the
> resulting server to get a `template.json` that is correct for your LSP4IJ version.

**Go to definition from C#** (the `[Tangent("Controller")]` → `contract Controller` jump that the
VS Code extension implements via the custom `tangent/contractDefinition` request) is **not**
available on this path. It needs plugin code.

---

## Plugin path (unverified)

### Build

Requires **JDK 17 or newer** (JDK 21 is fine) and nothing else — Gradle comes from the wrapper.

```bash
cd editors/rider
./gradlew buildPlugin          # Windows: .\gradlew.bat buildPlugin
```

The signed-nothing distribution lands in `build/distributions/tangent-rider-0.1.0.zip`, installable
via <kbd>Settings</kbd> → **Plugins** → ⚙ → **Install Plugin from Disk…**.

Other useful tasks:

| Task | What it does |
| --- | --- |
| `./gradlew runIde` | Launches a sandbox Rider with the plugin loaded. First run downloads ~2 GB. |
| `./gradlew verifyPlugin` | Runs the JetBrains Plugin Verifier against recommended IDEs. |
| `./gradlew build` | Compiles and runs `verifyPluginProjectConfiguration`. |

### The Gradle wrapper is incomplete

`gradle/wrapper/gradle-wrapper.properties` is committed, but `gradle-wrapper.jar`, `gradlew` and
`gradlew.bat` are **not** — a binary jar could not be produced here. With any local Gradle ≥ 8.5:

```bash
cd editors/rider
gradle wrapper --gradle-version 8.10.2
```

That regenerates the properties file *and* the jar and both launcher scripts. (If you have no Gradle
at all, [SDKMAN!](https://sdkman.io/) / `winget install Gradle.Gradle` / `brew install gradle`, or
just run the build with a plain `gradle buildPlugin` and skip the wrapper entirely.)

### What the plugin adds over the zero-build path

- The TextMate bundle is **shipped inside the plugin** (`src/main/resources/textmate/tangent/`), so
  there is no manual *Add bundle* step.
- A real `.tan` file type with a Tangent icon, so the file shows up as *Tangent* rather than as
  *TextMate files*.
- The language server runs on the **native JetBrains LSP API** — no LSP4IJ dependency, status shown
  in the *Language Services* widget in the status bar.
- Settings under <kbd>Settings</kbd> → **Tools** → **Tangent**: server command (default `dotnet`) and
  arguments (default `tangent serve`). Applying restarts a running server.

### Layout

```
editors/rider/
├─ README.md
├─ .gitignore
├─ build.gradle.kts                    IntelliJ Platform Gradle Plugin 2.x, Kotlin DSL
├─ settings.gradle.kts
├─ gradle.properties                   platformVersion = 2024.2, pluginSinceBuild = 242
├─ gradle/wrapper/gradle-wrapper.properties
└─ src/main/
   ├─ kotlin/com/tangent/rider/
   │  ├─ TangentFileType.kt            *.tan file type + icon
   │  ├─ TangentTextMateBundleProvider.kt
   │  ├─ TangentLspServerSupportProvider.kt
   │  └─ TangentSettings.kt            persistent state + Settings page
   └─ resources/
      ├─ META-INF/plugin.xml
      ├─ icons/tangent.svg
      └─ textmate/tangent/             bundled copy of the VS Code grammar
         ├─ package.json
         ├─ language-configuration.json
         └─ syntaxes/tangent.tmLanguage.json
```

`textmate/tangent/syntaxes/tangent.tmLanguage.json` and `language-configuration.json` are **byte-for-byte
copies** of `editors/vscode/`. When the grammar changes there, copy it again and bump
`BUNDLE_VERSION` in `TangentTextMateBundleProvider.kt` so already-extracted copies are refreshed.

---

## Unverified API points

Ordered by how likely they are to be the thing that breaks. All were written against
[JetBrains' published docs](https://plugins.jetbrains.com/docs/intellij/language-server-protocol.html)
and the `intellij-community` sources, not against a compiler.

1. **`<depends>com.intellij.modules.ultimate</depends>`.** The LSP API docs say plugins targeting IDE
   builds before 2025.2.1 must depend on this module, and they list Rider as an LSP-capable IDE — but
   that Rider actually *declares* `com.intellij.modules.ultimate` was not confirmed. If the plugin
   silently fails to load, this line is the first suspect; try removing it (Rider's LSP classes are on
   the classpath either way), or, when moving to 2025.2.1+, replace it with
   `com.intellij.modules.lsp`.

2. **`TangentFileType` vs. the TextMate grammar.** A plain `FileType` registered for `tan` would
   *suppress* TextMate highlighting: `TextMateFileType.isMyFileType` only replaces file types that are
   `UnknownFileType`, `PlainTextFileType`, or implement the marker interface
   `org.jetbrains.plugins.textmate.TextMateBackedFileType`. `TangentFileType` implements that marker
   for exactly this reason. If highlighting disappears, that is why — do not "simplify" the marker
   away. (This is settled from the source of `TextMateFileType.java`; what is unverified is only that
   `TextMateBackedFileType` is exported from the textmate plugin's public surface in 2024.2.)

3. **`FileType` method set.** `isBinary()` and `isReadOnly()` are overridden explicitly;
   `getCharset(VirtualFile, byte[])` and `getDisplayName()` are assumed to have usable defaults.
   If Kotlin complains about unimplemented members, add them.

4. **TextMate bundle extraction.** `TextMateBundleProvider.PluginBundle(name, path)` takes a real
   `java.nio.file.Path`, but plugin resources live inside a jar. `TangentTextMateBundleProvider`
   therefore copies the three bundle files into
   `<IDE system dir>/tangent/textmate-<BUNDLE_VERSION>/` once and returns that directory. The
   alternative — having Gradle place the bundle beside the jar in the plugin distribution and
   resolving `PluginManagerCore.getPlugin(...)!!.pluginPath` — is tidier but needs a `prepareSandbox`
   customisation. The signatures here (`getBundles(): List<PluginBundle>`, EP
   `com.intellij.textmate.bundleProvider`, `dynamic="true"`) are taken verbatim from
   `TextMateBundleProvider.kt`.

5. **`LspServerStarter` resolution.** `fileOpened(project, file, serverStarter: LspServerStarter)`
   uses the nested interface `LspServerSupportProvider.LspServerStarter` unqualified, relying on
   Kotlin resolving nested classifiers inherited from a supertype. If the compiler disagrees, qualify
   it as `LspServerSupportProvider.LspServerStarter`.

6. **`LspServerManager.getInstance(project).stopAndRestartIfNeeded(Class)`.** Correct for 2024.2.
   Deprecated from 2026.1.4 in favour of `LspClientManager.getInstance(project)` and
   `stopAndRestartClientsIfNeeded`; likewise `LspServerSupportProvider` /
   `ProjectWideLspServerDescriptor` are renamed to `LspIntegrationProvider` /
   `ProjectWideLspClientDescriptor` and the EP to `com.intellij.platform.lsp.integrationProvider`.
   Both old names still work, deprecated, so this compiles with warnings on newer platforms.

7. **`FormBuilder` in `TangentConfigurable`.** Deliberately the old, stable `com.intellij.util.ui`
   builder rather than the Kotlin UI DSL v2, to reduce the number of unverified API calls.
   `addLabeledComponent(String, JComponent)`, `addComponent(JComponent)` and
   `addComponentFillVertically(JComponent, int)` are the only methods used.

8. **Gradle plugin version.** `org.jetbrains.intellij.platform` is pinned to `2.1.0` because that
   release is contemporaneous with the 2024.2 target and matches the API used here
   (`pluginConfiguration { ideaVersion { untilBuild = provider { null } } }`,
   `pluginVerification { ides { recommended() } }`). Newer 2.x releases should work; note that
   `instrumentationTools()` was *removed* from the dependencies block in 2.x, so do not add it back
   from an older tutorial.

9. **Kotlin level.** `gradle.properties` sets `kotlin.stdlib.default.dependency = false` and
   `build.gradle.kts` pins `apiVersion`/`languageVersion` to 1.9 — Rider 2024.2 bundles Kotlin 1.9.x
   and a newer stdlib API would fail at runtime. Raise both together when raising `platformVersion`.

10. **Not implemented.** The C#-side go-to-definition from `[Tangent("Contract")]`, the
    *Restart Language Server* and *Show Dependency Graph* actions, and the `tangent/contractDefinition`
    custom request all exist in the VS Code extension but have no counterpart here. The JetBrains LSP
    API supports custom requests via `LspServerDescriptor` / `LspServer.sendRequest`, so it is
    straightforward to add once the basics build.
