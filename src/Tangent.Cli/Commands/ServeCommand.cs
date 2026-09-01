using Tangent.LanguageServer;

namespace Tangent.Cli.Commands;

/// <summary>
/// <c>tangent serve</c>: the language server, speaking LSP over stdio. Nothing else may be written to
/// standard output in this mode — a stray line corrupts the protocol stream — so every message goes to
/// standard error.
/// </summary>
public static class ServeCommand
{
    /// <summary>Runs the server until the client sends <c>exit</c> or the token fires.</summary>
    public static async Task<int> RunAsync(string? workspace, TextWriter stderr, CancellationToken cancellationToken)
    {
        string? root = string.IsNullOrWhiteSpace(workspace) ? null : Path.GetFullPath(workspace!);
        if (root is not null && !Directory.Exists(root))
        {
            return Fail(stderr, $"error: '{root}' is not a directory.");
        }

        using Stream input = Console.OpenStandardInput();
        using Stream output = Console.OpenStandardOutput();

        await TangentLanguageServerHost.RunAsync(input, output, root, cancellationToken).ConfigureAwait(false);
        return ExitCodes.Clean;
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine(message);
        return ExitCodes.LoadFailure;
    }
}
