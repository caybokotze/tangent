using System;
using System.Globalization;
using Tangent.Core.Ir;

namespace Tangent.Core.Diagnostics;

/// <summary>The immutable definition of one Tangent diagnostic: id, message template, severity and category.</summary>
public sealed class DiagnosticDescriptor
{
    /// <summary>Creates a descriptor.</summary>
    public DiagnosticDescriptor(
        string id,
        string title,
        string messageTemplate,
        DiagnosticSeverity defaultSeverity,
        DiagnosticCategory category,
        RuleTier? tier = null)
    {
        Id = id;
        Title = title;
        MessageTemplate = messageTemplate;
        DefaultSeverity = defaultSeverity;
        Category = category;
        Tier = tier;
    }

    /// <summary>The diagnostic id, for example <c>TNG0104</c>.</summary>
    public string Id { get; }

    /// <summary>A short human title.</summary>
    public string Title { get; }

    /// <summary>A <see cref="string.Format(IFormatProvider, string, object[])"/> template with <c>{0}</c>-style holes.</summary>
    public string MessageTemplate { get; }

    /// <summary>The severity used when no override applies.</summary>
    public DiagnosticSeverity DefaultSeverity { get; }

    /// <summary>The producing area of Tangent.</summary>
    public DiagnosticCategory Category { get; }

    /// <summary>The evaluation tier this diagnostic belongs to, when it reports a rule violation.</summary>
    public RuleTier? Tier { get; }

    /// <summary>Formats <see cref="MessageTemplate"/>; never throws on argument mismatch.</summary>
    public string Format(params object?[]? arguments)
    {
        if (arguments is null || arguments.Length == 0) return MessageTemplate;
        try
        {
            return string.Format(CultureInfo.InvariantCulture, MessageTemplate, arguments);
        }
        catch (FormatException)
        {
            return MessageTemplate;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Id + ": " + Title;
}
