using System;
using System.Collections.Immutable;
using System.Text;
using Tangent.Core.Text;

namespace Tangent.Core.Matching;

/// <summary>One alternative inside an attribute group, for example <c>Route(*)</c>.</summary>
public readonly struct AttributeAlternative
{
    /// <summary>The attribute name as written, with or without the <c>Attribute</c> suffix.</summary>
    public StringId Name { get; }

    /// <summary>True when the source wrote <c>(*)</c>, meaning "with any arguments".</summary>
    public bool AnyArgs { get; }

    /// <summary>Creates an alternative.</summary>
    public AttributeAlternative(StringId name, bool anyArgs)
    {
        Name = name;
        AnyArgs = anyArgs;
    }
}

/// <summary>
/// A compiled <c>[A | B]</c> attribute group. At least one alternative must be present on the selected entity.
/// Matching is by simple name, with or without the <c>Attribute</c> suffix on either side.
/// </summary>
public sealed class AttributeGroup
{
    private const string AttributeSuffix = "Attribute";

    private readonly StringTable _strings;

    internal AttributeGroup(StringTable strings, ImmutableArray<AttributeAlternative> alternatives)
    {
        _strings = strings;
        Alternatives = alternatives;
    }

    /// <summary>The alternatives; any one of them satisfies the group.</summary>
    public ImmutableArray<AttributeAlternative> Alternatives { get; }

    /// <summary>Creates a group from alternatives.</summary>
    public static AttributeGroup Create(StringTable strings, ImmutableArray<AttributeAlternative> alternatives)
    {
        if (strings is null) throw new ArgumentNullException(nameof(strings));
        return new AttributeGroup(strings, alternatives);
    }

    /// <summary>True when <paramref name="attributeName"/> satisfies any alternative in this group.</summary>
    public bool Matches(ReadOnlySpan<char> attributeName)
    {
        int lastDot = attributeName.LastIndexOf('.');
        if (lastDot >= 0) attributeName = attributeName.Slice(lastDot + 1);

        for (int i = 0; i < Alternatives.Length; i++)
        {
            if (NameEquals(attributeName, _strings.GetSpan(Alternatives[i].Name))) return true;
        }

        return false;
    }

    /// <summary>True when <paramref name="attributeName"/> satisfies any alternative in this group.</summary>
    public bool Matches(string attributeName) => attributeName != null && Matches(attributeName.AsSpan());

    /// <summary>True when the two names are equal ignoring an <c>Attribute</c> suffix on either side.</summary>
    public static bool NameEquals(ReadOnlySpan<char> actual, ReadOnlySpan<char> expected)
    {
        if (actual.SequenceEqual(expected)) return true;
        ReadOnlySpan<char> suffix = AttributeSuffix.AsSpan();
        if (actual.Length == expected.Length + suffix.Length &&
            actual.Slice(0, expected.Length).SequenceEqual(expected) &&
            actual.Slice(expected.Length).SequenceEqual(suffix))
        {
            return true;
        }

        if (expected.Length == actual.Length + suffix.Length &&
            expected.Slice(0, actual.Length).SequenceEqual(actual) &&
            expected.Slice(actual.Length).SequenceEqual(suffix))
        {
            return true;
        }

        return false;
    }

    /// <summary>Renders the group back to its source form, for example <c>[HttpGet | HttpPost]</c>.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < Alternatives.Length; i++)
        {
            if (i > 0) sb.Append(" | ");
            sb.Append(_strings.GetString(Alternatives[i].Name));
            if (Alternatives[i].AnyArgs) sb.Append("(*)");
        }

        sb.Append(']');
        return sb.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => Describe();
}
