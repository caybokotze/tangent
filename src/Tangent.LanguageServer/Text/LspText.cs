using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Tangent.Core.Text;

namespace Tangent.LanguageServer.Text;

/// <summary>Conversions between Tangent's offset-based spans and LSP's zero-based positions.</summary>
internal static class LspText
{
    /// <summary>The LSP position of a character offset.</summary>
    public static LspPosition ToPosition(LineIndex index, int offset)
    {
        (int line, int character) = index.PositionOf(offset);
        return new LspPosition(line, character);
    }

    /// <summary>The LSP range covering <paramref name="length"/> characters from <paramref name="start"/>.</summary>
    public static LspRange ToRange(LineIndex index, int start, int length)
    {
        if (length < 0) length = 0;
        return new LspRange(ToPosition(index, start), ToPosition(index, start + length));
    }

    /// <summary>The LSP range of a Core source span.</summary>
    public static LspRange ToRange(LineIndex index, SourceSpan span)
    {
        if (!span.IsValid) return new LspRange(new LspPosition(0, 0), new LspPosition(0, 0));
        int length = span.Length > 0 ? span.Length : 1;
        return ToRange(index, span.Start, length);
    }

    /// <summary>A canonical absolute path used as the key for a document.</summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (System.IO.PathTooLongException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
    }

    /// <summary>The document URI for a file system path.</summary>
    public static DocumentUri ToUri(string path) => DocumentUri.FromFileSystemPath(NormalizePath(path));

    /// <summary>The file system path for a document URI, or null when it is not a file.</summary>
    public static string? FromUri(DocumentUri? uri)
    {
        if (uri is null) return null;
        try
        {
            string path = uri.GetFileSystemPath();
            return string.IsNullOrEmpty(path) ? null : NormalizePath(path);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    /// <summary>True when the path names a Tangent source file.</summary>
    public static bool IsTangentFile(string? path) =>
        path is not null && path.EndsWith(".tan", StringComparison.OrdinalIgnoreCase);
}
