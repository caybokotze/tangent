using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using CoreCatalog = Tangent.Core.Diagnostics.DiagnosticCatalog;
using CoreDescriptor = Tangent.Core.Diagnostics.DiagnosticDescriptor;
using CoreSeverity = Tangent.Core.Diagnostics.DiagnosticSeverity;

namespace Tangent.Analyzers;

/// <summary>
/// Every Roslyn <see cref="DiagnosticDescriptor"/> Tangent can report, built once from the Core
/// <see cref="CoreCatalog"/> so the analyzer, the CLI and the language server never diverge on ids,
/// titles, message templates or default severities.
/// </summary>
public static class TangentDiagnostics
{
    /// <summary>The analyzer category every Tangent diagnostic is reported under.</summary>
    public const string Category = "Tangent";

    private const string HelpLinkPrefix =
        "https://github.com/tangent-dotnet/tangent/blob/main/docs/diagnostics.md#";

    private static readonly Dictionary<string, DiagnosticDescriptor> ById;

    static TangentDiagnostics()
    {
        ById = Build(out ImmutableArray<DiagnosticDescriptor> all);
        All = all;
    }

    /// <summary>Every descriptor, in catalogue order.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; }

    /// <summary>The descriptor for a diagnostic code such as <c>TNG0104</c>.</summary>
    /// <exception cref="ArgumentException">The code is not in the catalogue.</exception>
    public static DiagnosticDescriptor Get(string code)
    {
        if (code is null) throw new ArgumentNullException(nameof(code));
        if (ById.TryGetValue(code, out DiagnosticDescriptor descriptor)) return descriptor;
        throw new ArgumentException("Unknown Tangent diagnostic code '" + code + "'.", nameof(code));
    }

    /// <summary>The descriptor for a code, or null when the code is unknown.</summary>
    public static DiagnosticDescriptor? TryGet(string? code)
    {
        if (code is null) return null;
        return ById.TryGetValue(code, out DiagnosticDescriptor descriptor) ? descriptor : null;
    }

    /// <summary>Maps a Core severity onto Roslyn's.</summary>
    public static DiagnosticSeverity ToRoslyn(CoreSeverity severity)
    {
        switch (severity)
        {
            case CoreSeverity.Error: return DiagnosticSeverity.Error;
            case CoreSeverity.Warning: return DiagnosticSeverity.Warning;
            case CoreSeverity.Info: return DiagnosticSeverity.Info;
            default: return DiagnosticSeverity.Hidden;
        }
    }

    private static Dictionary<string, DiagnosticDescriptor> Build(out ImmutableArray<DiagnosticDescriptor> all)
    {
        ImmutableArray<CoreDescriptor> source = CoreCatalog.All;
        var builder = ImmutableArray.CreateBuilder<DiagnosticDescriptor>(source.Length);
        var map = new Dictionary<string, DiagnosticDescriptor>(source.Length, StringComparer.Ordinal);

        for (int i = 0; i < source.Length; i++)
        {
            CoreDescriptor core = source[i];

            // The message is always fully composed by the analyzer (contract, member, expectation, actual,
            // rule id and .tan location) so that consumers never have to re-parse it; the Roslyn template is
            // therefore a single hole. Core's own template is still what shapes the text — see MessageFactory.
            var descriptor = new DiagnosticDescriptor(
                id: core.Id,
                title: core.Title,
                messageFormat: "{0}",
                category: Category,
                defaultSeverity: ToRoslyn(core.DefaultSeverity),
                isEnabledByDefault: true,
                description: core.MessageTemplate,
                helpLinkUri: HelpLinkPrefix + core.Id.ToLowerInvariant());

            builder.Add(descriptor);
            map[core.Id] = descriptor;
        }

        all = builder.MoveToImmutable();
        return map;
    }
}
