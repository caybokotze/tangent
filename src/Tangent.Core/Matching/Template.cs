using System;
using System.Collections.Immutable;
using System.Text;
using Tangent.Core.Text;

namespace Tangent.Core.Matching;

/// <summary>The kind of one piece of a compiled template.</summary>
public enum TemplatePartKind : byte
{
    /// <summary>Literal text.</summary>
    Literal = 0,

    /// <summary><c>{Name}</c>: the simple name of the bound type.</summary>
    Name = 1,

    /// <summary><c>{Namespace}</c>: the containing namespace of the bound type.</summary>
    Namespace = 2,

    /// <summary><c>{Name:strip Suffix}</c>: the simple name with a suffix removed.</summary>
    NameStrip = 3,
}

/// <summary>One piece of a compiled template.</summary>
public readonly struct TemplatePart
{
    /// <summary>What this piece contributes.</summary>
    public TemplatePartKind Kind { get; }

    /// <summary>Literal text, or the suffix for <see cref="TemplatePartKind.NameStrip"/>.</summary>
    public StringId Text { get; }

    /// <summary>Creates a part.</summary>
    public TemplatePart(TemplatePartKind kind, StringId text)
    {
        Kind = kind;
        Text = text;
    }
}

/// <summary>A compiled name template such as <c>I{Name}</c> or <c>{Name:strip Service}Handler</c>.</summary>
public sealed class Template
{
    private readonly StringTable _strings;

    private Template(StringTable strings, StringId source, ImmutableArray<TemplatePart> parts)
    {
        _strings = strings;
        Source = source;
        Parts = parts;
    }

    /// <summary>The template text, interned.</summary>
    public StringId Source { get; }

    /// <summary>The compiled pieces, in order.</summary>
    public ImmutableArray<TemplatePart> Parts { get; }

    /// <summary>The template text.</summary>
    public string Text => _strings.GetString(Source);

    /// <summary>True when the template contains no holes and expands to itself.</summary>
    public bool IsLiteral
    {
        get
        {
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Parts[i].Kind != TemplatePartKind.Literal) return false;
            }

            return true;
        }
    }

    /// <summary>Compiles a template. Unrecognised holes are kept as literal text.</summary>
    public static Template Parse(ReadOnlySpan<char> text, StringTable strings)
    {
        if (strings is null) throw new ArgumentNullException(nameof(strings));
        StringId source = strings.Intern(text);
        var parts = ImmutableArray.CreateBuilder<TemplatePart>();

        int index = 0;
        int literalStart = 0;
        while (index < text.Length)
        {
            if (text[index] != '{')
            {
                index++;
                continue;
            }

            int close = -1;
            for (int i = index + 1; i < text.Length; i++)
            {
                if (text[i] == '}') { close = i; break; }
            }

            if (close < 0) break;

            ReadOnlySpan<char> inner = text.Slice(index + 1, close - index - 1);
            TemplatePartKind kind;
            StringId payload = StringId.Invalid;
            if (inner.SequenceEqual("Name".AsSpan()))
            {
                kind = TemplatePartKind.Name;
            }
            else if (inner.SequenceEqual("Namespace".AsSpan()))
            {
                kind = TemplatePartKind.Namespace;
            }
            else if (TryParseStrip(inner, strings, out payload))
            {
                kind = TemplatePartKind.NameStrip;
            }
            else
            {
                index = close + 1;
                continue; // unrecognised hole: leave it in the literal run
            }

            if (index > literalStart)
            {
                parts.Add(new TemplatePart(TemplatePartKind.Literal, strings.Intern(text.Slice(literalStart, index - literalStart))));
            }

            parts.Add(new TemplatePart(kind, payload));
            index = close + 1;
            literalStart = index;
        }

        if (literalStart < text.Length)
        {
            parts.Add(new TemplatePart(TemplatePartKind.Literal, strings.Intern(text.Slice(literalStart))));
        }

        return new Template(strings, source, parts.ToImmutable());
    }

    private static bool TryParseStrip(ReadOnlySpan<char> inner, StringTable strings, out StringId suffix)
    {
        suffix = StringId.Invalid;
        int colon = inner.IndexOf(':');
        if (colon < 0) return false;
        if (!inner.Slice(0, colon).SequenceEqual("Name".AsSpan())) return false;

        ReadOnlySpan<char> rest = inner.Slice(colon + 1).Trim();
        if (rest.Length < 6 || !rest.Slice(0, 5).SequenceEqual("strip".AsSpan())) return false;
        ReadOnlySpan<char> value = rest.Slice(5).Trim();
        if (value.Length == 0) return false;
        suffix = strings.Intern(value);
        return true;
    }

    /// <summary>
    /// Expands the template for one bound type. Returns false when <paramref name="destination"/> is too small;
    /// <paramref name="written"/> then holds the number of characters required.
    /// </summary>
    public bool Expand(ReadOnlySpan<char> typeName, ReadOnlySpan<char> ns, Span<char> destination, out int written)
    {
        int total = 0;
        bool fits = true;
        for (int i = 0; i < Parts.Length; i++)
        {
            TemplatePart part = Parts[i];
            ReadOnlySpan<char> piece;
            switch (part.Kind)
            {
                case TemplatePartKind.Name:
                    piece = typeName;
                    break;
                case TemplatePartKind.Namespace:
                    piece = ns;
                    break;
                case TemplatePartKind.NameStrip:
                {
                    ReadOnlySpan<char> suffix = _strings.GetSpan(part.Text);
                    piece = typeName.Length >= suffix.Length && suffix.Length > 0 &&
                            typeName.Slice(typeName.Length - suffix.Length).SequenceEqual(suffix)
                        ? typeName.Slice(0, typeName.Length - suffix.Length)
                        : typeName;
                    break;
                }

                default:
                    piece = _strings.GetSpan(part.Text);
                    break;
            }

            if (fits && total + piece.Length <= destination.Length)
            {
                piece.CopyTo(destination.Slice(total));
            }
            else
            {
                fits = false;
            }

            total += piece.Length;
        }

        written = total;
        return fits;
    }

    /// <summary>Expands the template into a new string.</summary>
    public string Expand(string typeName, string ns)
    {
        var sb = new StringBuilder();
        ReadOnlySpan<char> name = (typeName ?? string.Empty).AsSpan();
        ReadOnlySpan<char> space = (ns ?? string.Empty).AsSpan();
        for (int i = 0; i < Parts.Length; i++)
        {
            TemplatePart part = Parts[i];
            switch (part.Kind)
            {
                case TemplatePartKind.Name:
                    AppendSpan(sb, name);
                    break;
                case TemplatePartKind.Namespace:
                    AppendSpan(sb, space);
                    break;
                case TemplatePartKind.NameStrip:
                {
                    ReadOnlySpan<char> suffix = _strings.GetSpan(part.Text);
                    AppendSpan(sb, name.Length >= suffix.Length && suffix.Length > 0 &&
                                   name.Slice(name.Length - suffix.Length).SequenceEqual(suffix)
                        ? name.Slice(0, name.Length - suffix.Length)
                        : name);
                    break;
                }

                default:
                    sb.Append(_strings.GetString(part.Text));
                    break;
            }
        }

        return sb.ToString();
    }

    private static void AppendSpan(StringBuilder sb, ReadOnlySpan<char> value)
    {
        for (int i = 0; i < value.Length; i++) sb.Append(value[i]);
    }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
