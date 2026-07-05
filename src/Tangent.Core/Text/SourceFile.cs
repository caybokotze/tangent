using System;

namespace Tangent.Core.Text;

/// <summary>One <c>.tan</c> file: its path and its full text.</summary>
public readonly struct SourceFile : IEquatable<SourceFile>
{
    /// <summary>The path shown in diagnostics. Any stable identifier will do.</summary>
    public string Path { get; }

    /// <summary>The complete text of the file.</summary>
    public string Text { get; }

    /// <summary>Creates a source file.</summary>
    public SourceFile(string path, string text)
    {
        Path = path ?? string.Empty;
        Text = text ?? string.Empty;
    }

    /// <summary>The file text as a span.</summary>
    public ReadOnlySpan<char> Span => Text.AsSpan();

    /// <inheritdoc/>
    public bool Equals(SourceFile other) =>
        string.Equals(Path, other.Path, StringComparison.Ordinal) &&
        string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SourceFile other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked { return (Path.GetHashCode() * 397) ^ Text.Length; }
    }

    /// <inheritdoc/>
    public override string ToString() => Path;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(SourceFile left, SourceFile right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(SourceFile left, SourceFile right) => !left.Equals(right);
}
