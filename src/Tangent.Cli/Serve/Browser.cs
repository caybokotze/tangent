using System.Diagnostics;

namespace Tangent.Cli.Serve;

/// <summary>Opens an address in the default browser, for <c>tangent graph --open</c>.</summary>
internal static class Browser
{
    /// <summary>Opens <paramref name="url"/>. Returns an error message, or null when the browser started.</summary>
    internal static string? Open(string url)
    {
        try
        {
            // UseShellExecute hands the address to the shell on Windows, to open on macOS and to xdg-open on Linux.
            using Process? process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return exception.Message;
        }
    }
}
