package com.tangent.rider

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspServerSupportProvider
import com.intellij.platform.lsp.api.ProjectWideLspServerDescriptor
import java.nio.charset.StandardCharsets

/**
 * Starts `dotnet tangent serve` (configurable, see [TangentSettings]) over stdio the first
 * time a `.tan` file is opened in a project, using the JetBrains LSP API available in
 * commercial IntelliJ-based IDEs since 2023.2.
 */
internal class TangentLspServerSupportProvider : LspServerSupportProvider {

    override fun fileOpened(project: Project, file: VirtualFile, serverStarter: LspServerStarter) {
        if (file.extension.equals(TANGENT_EXTENSION, ignoreCase = true)) {
            serverStarter.ensureServerStarted(TangentLspServerDescriptor(project))
        }
    }
}

internal class TangentLspServerDescriptor(project: Project) :
    ProjectWideLspServerDescriptor(project, "Tangent") {

    override fun isSupportedFile(file: VirtualFile): Boolean =
        file.extension.equals(TANGENT_EXTENSION, ignoreCase = true)

    override fun createCommandLine(): GeneralCommandLine {
        val settings = TangentSettings.getInstance()
        return GeneralCommandLine(settings.commandLine())
            .withWorkDirectory(project.basePath)
            .withCharset(StandardCharsets.UTF_8)
    }
}
