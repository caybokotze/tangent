using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using Tangent.LanguageServer.Handlers;
using Tangent.LanguageServer.Text;
using Tangent.LanguageServer.Workspace;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace Tangent.LanguageServer;

/// <summary>
/// Hosts the Tangent language server over a pair of streams. <c>tangent serve</c> calls this with
/// standard input and output; the tests call it with an in-memory pipe.
/// </summary>
public static class TangentLanguageServerHost
{
    /// <summary>
    /// Runs the server until the client sends <c>exit</c> or <paramref name="cancellationToken"/> fires.
    /// </summary>
    /// <param name="input">The stream LSP messages arrive on.</param>
    /// <param name="output">The stream LSP messages are written to.</param>
    /// <param name="workspaceRoot">
    /// The directory to look for a <c>.tangent</c> folder in. When null the <c>rootUri</c> from
    /// <c>initialize</c> is used, and failing that the folder of the first opened <c>.tan</c> file.
    /// </param>
    /// <param name="cancellationToken">Stops the server early.</param>
    public static async Task RunAsync(
        Stream input,
        Stream output,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        using var workspace = new TangentWorkspace(workspaceRoot);

        OmniSharpLanguageServer server = await OmniSharpLanguageServer.From(
            options => Configure(options, input, output, workspace),
            cancellationToken).ConfigureAwait(false);

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((OmniSharpLanguageServer)state!).ForcefulShutdown(),
            server);

        await server.WaitForExit.ConfigureAwait(false);
    }

    private static void Configure(
        LanguageServerOptions options,
        Stream input,
        Stream output,
        TangentWorkspace workspace)
    {
        options
            .WithInput(input)
            .WithOutput(output)
            .WithServerInfo(new ServerInfo
            {
                Name = "Tangent Language Server",
                Version = typeof(TangentLanguageServerHost).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            })
            .WithServices(services => services.AddSingleton(workspace))
            .WithHandler<TangentSyncHandler>()
            .WithHandler<TangentSemanticTokensHandler>()
            .WithHandler<TangentCompletionHandler>()
            .WithHandler<TangentHoverHandler>()
            .WithHandler<TangentDefinitionHandler>()
            .WithHandler<TangentReferencesHandler>()
            .WithHandler<ContractDefinitionHandler>()
            .OnInitialize((server, request, _) =>
            {
                workspace.Attach(server);
                foreach (string root in RootsOf(request))
                {
                    workspace.SetWorkspaceRoot(root);
                    if (workspace.Folder is not null) break;
                }

                return Task.CompletedTask;
            })
            .OnStarted((server, _) =>
            {
                workspace.RefreshAndPublish();
                workspace.RequestCompilation();
                server.Window.Log(workspace.Folder is null
                    ? "Tangent: no .tangent folder found yet; it will be picked up when a .tan file is opened."
                    : "Tangent: watching " + workspace.Folder);
                return Task.CompletedTask;
            });
    }

    private static IEnumerable<string> RootsOf(InitializeParams request)
    {
        if (request.WorkspaceFolders is not null)
        {
            foreach (WorkspaceFolder folder in request.WorkspaceFolders)
            {
                string? path = LspText.FromUri(folder.Uri);
                if (path is not null) yield return path;
            }
        }

        string? fromUri = LspText.FromUri(request.RootUri);
        if (fromUri is not null) yield return fromUri;

        if (!string.IsNullOrEmpty(request.RootPath)) yield return request.RootPath;
    }
}
