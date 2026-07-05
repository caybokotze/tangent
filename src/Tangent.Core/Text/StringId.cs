using System;
using System.Globalization;

namespace Tangent.Core.Text;

/// <summary>A handle to a string interned in a <see cref="StringTable"/>; compares as an integer.</summary>
public readonly struct StringId : IEquatable<StringId>
{
    /// <summary>The sentinel handle meaning "no string".</summary>
    public static readonly StringId Invalid = new StringId(-1);

    /// <summary>The raw index of the interned string, or -1 when invalid.</summary>
    public int Value { get; }

    /// <summary>Wraps a raw index.</summary>
    public StringId(int value) => Value = value;

    /// <summary>True when this handle refers to an interned string.</summary>
    public bool IsValid => Value >= 0;

    /// <inheritdoc/>
    public bool Equals(StringId other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StringId other && Value == other.Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(StringId left, StringId right) => left.Value == right.Value;

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(StringId left, StringId right) => left.Value != right.Value;
}
