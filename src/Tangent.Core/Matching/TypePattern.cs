using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Tangent.Core.Text;

namespace Tangent.Core.Matching;

/// <summary>
/// One compiled type pattern: a name (possibly dotted, possibly a template), an arity and per-argument
/// sub-patterns. Patterns live in a <see cref="TypePatternTable"/> and refer to each other by index.
/// </summary>
public readonly struct TypePattern
{
    /// <summary>The interned pattern name, or <see cref="StringId.Invalid"/> for a bare <c>*</c>.</summary>
    public StringId Name { get; }

    /// <summary>Index of the template that produces the name, or -1 when <see cref="Name"/> is literal.</summary>
    public int TemplateIndex { get; }

    /// <summary>True when this pattern is a bare <c>*</c> and matches any type.</summary>
    public bool IsWildcard { get; }

    /// <summary>Offset of this pattern's arguments in <see cref="TypePatternTable.Arguments"/>.</summary>
    public int ArgumentStart { get; }

    /// <summary>The number of generic arguments the pattern requires. Zero means a non-generic type.</summary>
    public int Arity { get; }

    /// <summary>Creates a pattern.</summary>
    public TypePattern(StringId name, int templateIndex, bool isWildcard, int argumentStart, int arity)
    {
        Name = name;
        TemplateIndex = templateIndex;
        IsWildcard = isWildcard;
        ArgumentStart = argumentStart;
        Arity = arity;
    }
}

/// <summary>
/// Every compiled type pattern for one model, plus the alternation groups predicates point at.
/// An analyzer walks <see cref="Patterns"/> and <see cref="Arguments"/> against its own symbols;
/// <see cref="MatchesGroup(int, System.ReadOnlySpan{char})"/> exists for tests, goldens and the CLI.
/// </summary>
public sealed class TypePatternTable
{
    private readonly StringTable _strings;

    internal TypePatternTable(
        StringTable strings,
        ImmutableArray<TypePattern> patterns,
        ImmutableArray<int> arguments,
        ImmutableArray<ImmutableArray<int>> groups,
        ImmutableArray<Template> templates)
    {
        _strings = strings;
        Patterns = patterns;
        Arguments = arguments;
        Groups = groups;
        Templates = templates;
    }

    /// <summary>All compiled patterns.</summary>
    public ImmutableArray<TypePattern> Patterns { get; }

    /// <summary>The flat argument table; a pattern's arguments are the indices at [ArgumentStart, +Arity).</summary>
    public ImmutableArray<int> Arguments { get; }

    /// <summary>Alternation groups: each is a list of pattern indices, any one of which may match.</summary>
    public ImmutableArray<ImmutableArray<int>> Groups { get; }

    /// <summary>The templates referenced by <see cref="TypePattern.TemplateIndex"/>.</summary>
    public ImmutableArray<Template> Templates { get; }

    /// <summary>The string table names were interned into.</summary>
    public StringTable Strings => _strings;

    /// <summary>The pattern indices in a group.</summary>
    public ImmutableArray<int> Group(int groupIndex) => Groups[groupIndex];

    /// <summary>The index of the i'th argument of a pattern.</summary>
    public int Argument(in TypePattern pattern, int ordinal) => Arguments[pattern.ArgumentStart + ordinal];

    /// <summary>True when any pattern in the group matches the C#-style display name.</summary>
    public bool MatchesGroup(int groupIndex, ReadOnlySpan<char> displayName)
    {
        ImmutableArray<int> group = Groups[groupIndex];
        for (int i = 0; i < group.Length; i++)
        {
            if (Matches(group[i], displayName)) return true;
        }

        return false;
    }

    /// <summary>True when any pattern in the group matches the C#-style display name.</summary>
    public bool MatchesGroup(int groupIndex, string displayName) =>
        displayName != null && MatchesGroup(groupIndex, displayName.AsSpan());

    /// <summary>
    /// True when the pattern matches a C#-style display name such as <c>Task&lt;ActionResult&lt;OrderDto&gt;&gt;</c>.
    /// A pattern name with no dot matches the simple name in any namespace; a dotted pattern must match
    /// the namespace-qualified name suffix at a segment boundary.
    /// </summary>
    public bool Matches(int patternIndex, ReadOnlySpan<char> displayName)
    {
        TypePattern pattern = Patterns[patternIndex];
        if (pattern.IsWildcard) return true;

        displayName = displayName.Trim();
        int open = FindGenericOpen(displayName);
        ReadOnlySpan<char> baseName = open < 0 ? displayName : displayName.Slice(0, open);
        ReadOnlySpan<char> argumentText = open < 0
            ? ReadOnlySpan<char>.Empty
            : displayName.Slice(open + 1, displayName.Length - open - 2);

        // A display name may carry an arity suffix (`IRepository`1`).
        int backtick = baseName.IndexOf('`');
        if (backtick >= 0) baseName = baseName.Slice(0, backtick);

        if (!NameMatches(pattern, baseName)) return false;

        int actualArity = open < 0 ? 0 : CountTopLevelArguments(argumentText);
        if (actualArity != pattern.Arity) return false;
        if (actualArity == 0) return true;

        int start = 0;
        for (int i = 0; i < actualArity; i++)
        {
            int length = TopLevelArgumentLength(argumentText, start);
            ReadOnlySpan<char> argument = argumentText.Slice(start, length);
            if (!Matches(Arguments[pattern.ArgumentStart + i], argument)) return false;
            start += length + 1;
        }

        return true;
    }

    /// <summary>True when the pattern matches the display name.</summary>
    public bool Matches(int patternIndex, string displayName) =>
        displayName != null && Matches(patternIndex, displayName.AsSpan());

    private bool NameMatches(in TypePattern pattern, ReadOnlySpan<char> baseName)
    {
        ReadOnlySpan<char> expected = _strings.GetSpan(pattern.Name);
        if (expected.Length == 0) return false;

        if (expected.IndexOf('.') < 0)
        {
            int lastDot = baseName.LastIndexOf('.');
            ReadOnlySpan<char> simple = lastDot < 0 ? baseName : baseName.Slice(lastDot + 1);
            return simple.SequenceEqual(expected);
        }

        if (baseName.Length < expected.Length) return false;
        if (!baseName.Slice(baseName.Length - expected.Length).SequenceEqual(expected)) return false;
        return baseName.Length == expected.Length || baseName[baseName.Length - expected.Length - 1] == '.';
    }

    /// <summary>Renders a pattern back to its source form.</summary>
    public string Describe(int patternIndex)
    {
        var sb = new StringBuilder();
        Describe(patternIndex, sb);
        return sb.ToString();
    }

    /// <summary>Renders a whole alternation group, joined with <c>|</c>.</summary>
    public string DescribeGroup(int groupIndex)
    {
        var sb = new StringBuilder();
        ImmutableArray<int> group = Groups[groupIndex];
        for (int i = 0; i < group.Length; i++)
        {
            if (i > 0) sb.Append(" | ");
            Describe(group[i], sb);
        }

        return sb.ToString();
    }

    private void Describe(int patternIndex, StringBuilder sb)
    {
        TypePattern pattern = Patterns[patternIndex];
        if (pattern.IsWildcard)
        {
            sb.Append('*');
            return;
        }

        sb.Append(_strings.GetString(pattern.Name));
        if (pattern.Arity == 0) return;
        sb.Append('<');
        for (int i = 0; i < pattern.Arity; i++)
        {
            if (i > 0) sb.Append(", ");
            Describe(Arguments[pattern.ArgumentStart + i], sb);
        }

        sb.Append('>');
    }

    private static int FindGenericOpen(ReadOnlySpan<char> displayName)
    {
        if (displayName.Length == 0 || displayName[displayName.Length - 1] != '>') return -1;
        int depth = 0;
        for (int i = displayName.Length - 1; i >= 0; i--)
        {
            char c = displayName[i];
            if (c == '>') depth++;
            else if (c == '<')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private static int CountTopLevelArguments(ReadOnlySpan<char> text)
    {
        if (text.Length == 0) return 0;
        int count = 1;
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0) count++;
        }

        return count;
    }

    private static int TopLevelArgumentLength(ReadOnlySpan<char> text, int start)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0) return i - start;
        }

        return text.Length - start;
    }
}

/// <summary>Builds a <see cref="TypePatternTable"/> while the binder compiles a model.</summary>
public sealed class TypePatternBuilder
{
    private readonly StringTable _strings;
    private readonly List<TypePattern> _patterns = new List<TypePattern>();
    private readonly List<int> _arguments = new List<int>();
    private readonly List<ImmutableArray<int>> _groups = new List<ImmutableArray<int>>();
    private readonly Dictionary<string, int> _groupCache = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Creates a builder interning into <paramref name="strings"/>.</summary>
    public TypePatternBuilder(StringTable strings)
    {
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
    }

    /// <summary>The string table in use.</summary>
    public StringTable Strings => _strings;

    /// <summary>Adds a bare <c>*</c> pattern.</summary>
    public int AddWildcard()
    {
        _patterns.Add(new TypePattern(StringId.Invalid, -1, true, 0, 0));
        return _patterns.Count - 1;
    }

    /// <summary>Adds a named pattern with already-added argument pattern indices.</summary>
    public int AddNamed(StringId name, int templateIndex, ReadOnlySpan<int> argumentIndices)
    {
        int start = _arguments.Count;
        for (int i = 0; i < argumentIndices.Length; i++) _arguments.Add(argumentIndices[i]);
        _patterns.Add(new TypePattern(name, templateIndex, false, start, argumentIndices.Length));
        return _patterns.Count - 1;
    }

    /// <summary>Adds (or reuses) an alternation group of pattern indices.</summary>
    public int AddGroup(ReadOnlySpan<int> patternIndices)
    {
        var builder = ImmutableArray.CreateBuilder<int>(patternIndices.Length);
        var key = new StringBuilder();
        for (int i = 0; i < patternIndices.Length; i++)
        {
            builder.Add(patternIndices[i]);
            key.Append(patternIndices[i]).Append(',');
        }

        string cacheKey = key.ToString();
        if (_groupCache.TryGetValue(cacheKey, out int existing)) return existing;
        int index = _groups.Count;
        _groups.Add(builder.ToImmutable());
        _groupCache[cacheKey] = index;
        return index;
    }

    /// <summary>Freezes the builder into a table, sharing the model's template list.</summary>
    public TypePatternTable ToTable(ImmutableArray<Template> templates) => new TypePatternTable(
        _strings,
        _patterns.ToImmutableArray(),
        _arguments.ToImmutableArray(),
        _groups.ToImmutableArray(),
        templates);
}
