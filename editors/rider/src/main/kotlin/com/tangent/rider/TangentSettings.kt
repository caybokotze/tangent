package com.tangent.rider

import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.components.service
import com.intellij.openapi.options.Configurable
import com.intellij.openapi.project.ProjectManager
import com.intellij.platform.lsp.api.LspServerManager
import com.intellij.ui.components.JBLabel
import com.intellij.ui.components.JBTextField
import com.intellij.util.execution.ParametersListUtil
import com.intellij.util.ui.FormBuilder
import javax.swing.JComponent
import javax.swing.JPanel

/**
 * Application-level settings for the Tangent language server.
 *
 * Defaults mirror the VS Code extension: `dotnet` + `tangent serve`.
 */
@Service(Service.Level.APP)
@State(name = "TangentSettings", storages = [Storage("tangent.xml")])
class TangentSettings : PersistentStateComponent<TangentSettings.State> {

    class State {
        /** Executable used to launch the language server. */
        @JvmField
        var command: String = DEFAULT_COMMAND

        /** Arguments, as a single command-line string (parsed with shell-like quoting). */
        @JvmField
        var args: String = DEFAULT_ARGS
    }

    private var state = State()

    override fun getState(): State = state

    override fun loadState(state: State) {
        this.state = state
    }

    var command: String
        get() = state.command.ifBlank { DEFAULT_COMMAND }
        set(value) {
            state.command = value.trim()
        }

    var args: String
        get() = state.args
        set(value) {
            state.args = value.trim()
        }

    /** The full argv used by [TangentLspServerDescriptor.createCommandLine]. */
    fun commandLine(): List<String> = buildList {
        add(command)
        addAll(ParametersListUtil.parse(args))
    }

    companion object {
        const val DEFAULT_COMMAND: String = "dotnet"
        const val DEFAULT_ARGS: String = "tangent serve"

        fun getInstance(): TangentSettings = service()
    }
}

/**
 * Settings | Tools | Tangent. Applying restarts any running Tangent language server so
 * that a changed command takes effect immediately.
 */
internal class TangentConfigurable : Configurable {

    private val commandField = JBTextField()
    private val argsField = JBTextField()

    override fun getDisplayName(): String = "Tangent"

    override fun createComponent(): JComponent =
        FormBuilder.createFormBuilder()
            .addLabeledComponent("Server command:", commandField)
            .addLabeledComponent("Arguments:", argsField)
            .addComponent(JBLabel("Started over stdio for .tan files. Default: dotnet tangent serve"))
            .addComponentFillVertically(JPanel(), 0)
            .panel

    override fun isModified(): Boolean {
        val settings = TangentSettings.getInstance()
        return commandField.text.trim() != settings.command || argsField.text.trim() != settings.args
    }

    override fun reset() {
        val settings = TangentSettings.getInstance()
        commandField.text = settings.command
        argsField.text = settings.args
    }

    override fun apply() {
        val settings = TangentSettings.getInstance()
        settings.command = commandField.text
        settings.args = argsField.text

        for (project in ProjectManager.getInstance().openProjects) {
            if (project.isDisposed) continue
            LspServerManager.getInstance(project)
                .stopAndRestartIfNeeded(TangentLspServerSupportProvider::class.java)
        }
    }
}
