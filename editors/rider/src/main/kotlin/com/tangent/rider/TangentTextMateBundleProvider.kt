package com.tangent.rider

import com.intellij.openapi.application.PathManager
import com.intellij.openapi.diagnostic.logger
import org.jetbrains.plugins.textmate.api.TextMateBundleProvider
import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.StandardCopyOption

private val LOG = logger<TangentTextMateBundleProvider>()

/**
 * Bump when the contents of `src/main/resources/textmate/tangent/` change, so that
 * already-extracted copies in the IDE system directory are replaced.
 */
private const val BUNDLE_VERSION = "1"

/** Resource paths under `textmate/tangent/`, relative to the bundle root. */
private val BUNDLE_FILES = listOf(
    "package.json",
    "language-configuration.json",
    "syntaxes/tangent.tmLanguage.json",
)

/**
 * Registers the VS Code-style TextMate bundle shipped in this plugin's resources
 * (`textmate/tangent/`, a copy of `editors/vscode`'s grammar) with the TextMate plugin.
 *
 * [TextMateBundleProvider.PluginBundle] takes a real filesystem [Path], but plugin
 * resources live inside the plugin jar, so the bundle is extracted once into
 * `<IDE system dir>/tangent/textmate-<BUNDLE_VERSION>/` and reused on later launches.
 */
internal class TangentTextMateBundleProvider : TextMateBundleProvider {

    override fun getBundles(): List<TextMateBundleProvider.PluginBundle> {
        val root = extractBundle() ?: return emptyList()
        return listOf(TextMateBundleProvider.PluginBundle("Tangent", root))
    }

    private fun extractBundle(): Path? {
        val root = Path.of(PathManager.getSystemPath(), "tangent", "textmate-$BUNDLE_VERSION")
        val marker = root.resolve(".extracted")
        if (Files.isRegularFile(marker)) {
            return root
        }

        val classLoader = javaClass.classLoader
        try {
            Files.createDirectories(root)
            for (name in BUNDLE_FILES) {
                val stream = classLoader.getResourceAsStream("textmate/tangent/$name")
                if (stream == null) {
                    LOG.warn("Tangent TextMate bundle resource is missing: textmate/tangent/$name")
                    return null
                }
                val target = root.resolve(name)
                Files.createDirectories(target.parent)
                stream.use { Files.copy(it, target, StandardCopyOption.REPLACE_EXISTING) }
            }
            Files.writeString(marker, BUNDLE_VERSION)
        } catch (e: IOException) {
            LOG.warn("Failed to extract the Tangent TextMate bundle to $root", e)
            return null
        }
        return root
    }
}
