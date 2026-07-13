using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using Tangent.Core.Text;

namespace Tangent.Core.Matching;

/// <summary>The kind of one segment of a compiled namespace glob.</summary>
public enum GlobSegmentKind : byte
{
    /// <summary>An exact segment.</summary>
    Literal = 0,

    /// <summary><c>*</c>: exactly one segment, any text.</summary>
    Star = 1,

    /// <summary><c>**</c>: zero or more segments.</summary>
    DoubleStar = 2,
}

/// <summary>One segment of a compiled glob.</summary>
public readonly struct GlobSegment
{
    /// <summary>What this segment matches.</summary>
    public GlobSegmentKind Kind { get; }

    /// <summary>The interned literal text; only meaningful for <see cref="GlobSegmentKind.Literal"/>.</summary>
    public StringId Text { get; }

    /// <summary>Creates a segment.</summary>
    public GlobSegment(GlobSegmentKind kind, StringId text)
    {
        Kind = kind;
        Text = text;
    }
}

/// <summary>A compiled dotted-name glob such as <c>Orders.Domain.**</c>. Matching is allocation-free.</summary>
public sealed class Glob
{
    private const int StackSegments = 64;

    private readonly StringTable _strings;
    private readonly ImmutableArray<GlobSegment> _segments;

    private Glob(StringTable strings, StringId pattern, ImmutableArray<GlobSegment> segments)
    {
        _strings = strings;
        Pattern = pattern;
        _segments = segments;
    }

    /// <summary>The original glob text, interned.</summary>
    public StringId Pattern { get; }

    /// <summary>The compiled segments, in order.</summary>
    public ImmutableArray<GlobSegment> Segments => _segments;

    /// <summary>The glob text.</summary>
    public string Text => _strings.GetString(Pattern);

    /// <summary>Compiles a glob. Empty input produces a glob that matches nothing.</summary>
    public static Glob Compile(ReadOnlySpan<char> pattern, StringTable strings)
    {
        if (strings is null) throw new ArgumentNullException(nameof(strings));
        StringId id = strings.Intern(pattern);
        var builder = ImmutableArray.CreateBuilder<GlobSegment>();
        int index = 0;
        while (index <= pattern.Length)
        {
            int dot = pattern.Slice(index).IndexOf('.');
            int end = dot < 0 ? pattern.Length : index + dot;
            ReadOnlySpan<char> segment = pattern.Slice(index, end - index);
            if (segment.Length == 2 && segment[0] == '*' && segment[1] == '*')
            {
                builder.Add(new GlobSegment(GlobSegmentKind.DoubleStar, StringId.Invalid));
            }
            else if (segment.Length == 1 && segment[0] == '*')
            {
                builder.Add(new GlobSegment(GlobSegmentKind.Star, StringId.Invalid));
            }
            else if (segment.Length > 0)
            {
                builder.Add(new GlobSegment(GlobSegmentKind.Literal, strings.Intern(segment)));
            }

            if (dot < 0) break;
            index = end + 1;
        }

        return new Glob(strings, id, builder.ToImmutable());
    }

    /// <summary>
    /// True when <paramref name="dottedName"/> matches this glob. <c>**</c> matches zero or more segments, so
    /// <c>Orders.Domain.**</c> matches both <c>Orders.Domain</c> and <c>Orders.Domain.Entities.Order</c>.
    /// </summary>
    public bool Matches(ReadOnlySpan<char> dottedName)
    {
        if (_segments.Length == 0) return false;

        int count = CountSegments(dottedName);
        if (count <= StackSegments)
        {
            Span<int> starts = stackalloc int[StackSegments];
            SplitSegments(dottedName, starts, count);
            return MatchCore(dottedName, starts, count);
        }

        int[] rented = ArrayPool<int>.Shared.Rent(count);
        try
        {
            SplitSegments(dottedName, rented.AsSpan(), count);
            return MatchCore(dottedName, rented.AsSpan(), count);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rented);
        }
    }

    /// <summary>True when <paramref name="dottedName"/> matches this glob.</summary>
    public bool Matches(string dottedName) => dottedName != null && Matches(dottedName.AsSpan());

    private bool MatchCore(ReadOnlySpan<char> name, Span<int> starts, int count)
    {
        int segment = 0;
        int part = 0;
        int starSegment = -1;
        int starPart = -1;

        while (part < count)
        {
            if (segment < _segments.Length)
            {
                GlobSegmentKind kind = _segments[segment].Kind;
                if (kind == GlobSegmentKind.DoubleStar)
                {
                    starSegment = segment;
                    starPart = part;
                    segment++;
                    continue;
                }

                if (kind == GlobSegmentKind.Star || SegmentEquals(name, starts, count, part, _segments[segment].Text))
                {
                    segment++;
                    part++;
                    continue;
                }
            }

            if (starSegment < 0) return false;
            starPart++;
            segment = starSegment + 1;
            part = starPart;
        }

        while (segment < _segments.Length && _segments[segment].Kind == GlobSegmentKind.DoubleStar) segment++;
        return segment == _segments.Length;
    }

    private bool SegmentEquals(ReadOnlySpan<char> name, Span<int> starts, int count, int part, StringId literal)
    {
        int start = starts[part];
        int end = part + 1 < count ? starts[part + 1] - 1 : name.Length;
        return name.Slice(start, end - start).SequenceEqual(_strings.GetSpan(literal));
    }

    private static int CountSegments(ReadOnlySpan<char> name)
    {
        if (name.Length == 0) return 0;
        int count = 1;
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '.') count++;
        }

        return count;
    }

    private static void SplitSegments(ReadOnlySpan<char> name, Span<int> starts, int count)
    {
        if (count == 0) return;
        starts[0] = 0;
        int next = 1;
        for (int i = 0; i < name.Length && next < count; i++)
        {
            if (name[i] == '.') starts[next++] = i + 1;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Text;
}

/// <summary>Compiles globs once and hands out stable indices for them.</summary>
public sealed class GlobTable
{
    private readonly StringTable _strings;
    private readonly List<Glob> _globs = new List<Glob>();
    private readonly Dictionary<int, int> _byPattern = new Dictionary<int, int>();

    /// <summary>Creates a table that interns into <paramref name="strings"/>.</summary>
    public GlobTable(StringTable strings)
    {
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
    }

    /// <summary>The number of distinct globs.</summary>
    public int Count => _globs.Count;

    /// <summary>Returns the index of a glob, compiling it the first time it is seen.</summary>
    public int GetOrAdd(ReadOnlySpan<char> pattern)
    {
        StringId id = _strings.Intern(pattern);
        if (_byPattern.TryGetValue(id.Value, out int index)) return index;
        index = _globs.Count;
        _globs.Add(Glob.Compile(pattern, _strings));
        _byPattern[id.Value] = index;
        return index;
    }

    /// <summary>Returns the index of a glob given its interned text.</summary>
    public int GetOrAdd(StringId pattern) => GetOrAdd(_strings.GetSpan(pattern));

    /// <summary>Gets a compiled glob by index.</summary>
    public Glob this[int index] => _globs[index];

    /// <summary>Freezes the table into an immutable array.</summary>
    public ImmutableArray<Glob> ToImmutable() => _globs.ToImmutableArray();
}
