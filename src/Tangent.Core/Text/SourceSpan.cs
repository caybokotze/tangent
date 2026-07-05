using System;
using System.Globalization;

namespace Tangent.Core.Text;

/// <summary>A range of characters in one source file, with the 1-based line and column of its start.</summary>
public readonly struct SourceSpan : IEquatable<SourceSpan>
{
    /// <summary>A span that points nowhere.</summary>
    public static readonly SourceSpan None = new SourceSpan(-1, 0, 0, 0, 0);

    /// <summary>Index of the file in the compilation unit's file list, or -1 when unknown.</summary>
    public int FileIndex { get; }

    /// <summary>Character offset of the first character.</summary>
    public int Start { get; }

    /// <summary>Number of characters covered.</summary>
    public int Length { get; }

    /// <summary>1-based line of <see cref="Start"/>.</summary>
    public int Line { get; }

    /// <summary>1-based column of <see cref="Start"/>.</summary>
    public int Column { get; }

    /// <summary>Creates a span.</summary>
    public SourceSpan(int fileIndex, int start, int length, int line, int column)
    {
        FileIndex = fileIndex;
        Start = start;
        Length = length;
        Line = line;
        Column = column;
    }

    /// <summary>The offset one past the last character.</summary>
    public int End => Start + Length;

    /// <summary>True when this span refers to a real file position.</summary>
    public bool IsValid => FileIndex >= 0;

    /// <summary>Returns a span covering both this span and <paramref name="other"/>, which must be in the same file.</summary>
    public SourceSpan To(SourceSpan other)
    {
        if (!IsValid) return other;
        if (!other.IsValid) return this;
        int start = Start < other.Start ? Start : other.Start;
        int end = End > other.End ? End : other.End;
        return new SourceSpan(FileIndex, start, end - start, Line, Column);
    }

    /// <inheritdoc/>
    public bool Equals(SourceSpan other) =>
        FileIndex == other.FileIndex && Start == other.Start && Length == other.Length;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SourceSpan other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked { return (((FileIndex * 397) ^ Start) * 397) ^ Length; }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0},{1})", Line, Column);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(SourceSpan left, SourceSpan right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(SourceSpan left, SourceSpan right) => !left.Equals(right);
}
