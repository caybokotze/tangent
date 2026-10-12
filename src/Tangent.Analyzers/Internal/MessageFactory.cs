using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Tangent.Core.Ir;
using CoreDescriptor = Tangent.Core.Diagnostics.DiagnosticDescriptor;

namespace Tangent.Analyzers.Internal;

/// <summary>
/// Builds diagnostic messages and the property bag every Tangent diagnostic carries. The message always
/// follows the plan's shape — <c>{Contract}: {member} {expected}, found {actual}. (rule {RuleId}, {file}:{line})</c>
/// — and is composed from the Core catalogue's own template so the wording lives in exactly one place.
/// </summary>
internal static class MessageFactory
{
    internal const string ContractProperty = "contract";
    internal const string RuleProperty = "ruleId";
    internal const string MemberProperty = "member";
    internal const string TierProperty = "tier";
    internal const string FixProperty = "fix";
    internal const string FixArgProperty = "fixArg";
    internal const string FromLayerProperty = "fromLayer";
    internal const string ToLayerProperty = "toLayer";
    internal const string SymbolProperty = "symbol";

    private const string TypeWord = ": type ";

    /// <summary>Formats a rule violation and appends the rule id and its <c>.tan</c> location.</summary>
    internal static string Violation(
        CoreDescriptor descriptor,
        ArchitectureModel model,
        in Rule rule,
        string memberDescription,
        params object?[] arguments)
    {
        string text = descriptor.Format(arguments);

        // Core's templates are written for the type-level selector ("{0}: type must be sealed"). When the
        // rule selects a member, name the member instead of the word "type".
        if (memberDescription.Length > 0)
        {
            int index = text.IndexOf(TypeWord, StringComparison.Ordinal);
            if (index >= 0)
            {
                text = text.Substring(0, index + 2) + memberDescription + " " +
                       text.Substring(index + TypeWord.Length);
            }
        }

        return text + ". (rule " + rule.Id.Value + ", " + Origin(model, rule.Span) + ")";
    }

    /// <summary>Renders a rule's origin as <c>file:line</c>.</summary>
    internal static string Origin(ArchitectureModel model, Tangent.Core.Text.SourceSpan span)
    {
        string path = model.FilePathOf(span) ?? "<contract>";
        return FileName(path) + ":" + span.Line.ToString(CultureInfo.InvariantCulture);
    }

    private static string FileName(string path)
    {
        for (int i = path.Length - 1; i >= 0; i--)
        {
            if (path[i] == '/' || path[i] == '\\') return path.Substring(i + 1);
        }

        return path;
    }

    /// <summary>TNG0001, with a "did you mean" suggestion when a close contract name exists.</summary>
    internal static string UnknownContract(ArchitectureModel model, string name)
    {
        string message = Tangent.Core.Diagnostics.DiagnosticCatalog.UnknownContractAttribute.Format(name);
        string? suggestion = ClosestContract(model, name);
        return suggestion is null ? message : message + ". Did you mean '" + suggestion + "'?";
    }

    /// <summary>TNG0002.</summary>
    internal static string UnknownRule(string ruleId) =>
        Tangent.Core.Diagnostics.DiagnosticCatalog.UnknownRuleInIgnore.Format(ruleId);

    /// <summary>The declared contract name closest to <paramref name="name"/>, or null when none is close.</summary>
    internal static string? ClosestContract(ArchitectureModel model, string name)
    {
        string? best = null;
        int bestDistance = int.MaxValue;
        ImmutableArray<Contract> contracts = model.Contracts;
        for (int i = 0; i < contracts.Length; i++)
        {
            string candidate = model.Text(contracts[i].Name);
            int distance = Distance(name, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        int threshold = Math.Max(2, name.Length / 3);
        return bestDistance <= threshold ? best : null;
    }

    private static int Distance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        Span<int> previous = stackalloc int[64];
        Span<int> current = stackalloc int[64];
        if (b.Length + 1 > previous.Length) return Math.Abs(a.Length - b.Length) + 8;

        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int deletion = previous[j] + 1;
                int insertion = current[j - 1] + 1;
                int substitution = previous[j - 1] + cost;
                int min = deletion < insertion ? deletion : insertion;
                current[j] = min < substitution ? min : substitution;
            }

            current.Slice(0, b.Length + 1).CopyTo(previous);
        }

        return previous[b.Length];
    }

    /// <summary>The property bag carried by every rule-violation diagnostic.</summary>
    internal static ImmutableDictionary<string, string?> Properties(
        string contract,
        string ruleId,
        string member,
        RuleTier tier,
        string? fix = null,
        string? fixArgument = null)
    {
        ImmutableDictionary<string, string?>.Builder builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        builder[ContractProperty] = contract;
        builder[RuleProperty] = ruleId;
        builder[MemberProperty] = member;
        builder[TierProperty] = IrText.TierName(tier);
        if (fix is not null) builder[FixProperty] = fix;
        if (fixArgument is not null) builder[FixArgProperty] = fixArgument;
        return builder.ToImmutable();
    }

    /// <summary>The property bag for a diagnostic that is not tied to a rule.</summary>
    internal static ImmutableDictionary<string, string?> Properties(
        string contract,
        string ruleId,
        string member,
        string tier)
    {
        ImmutableDictionary<string, string?>.Builder builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        builder[ContractProperty] = contract;
        builder[RuleProperty] = ruleId;
        builder[MemberProperty] = member;
        builder[TierProperty] = tier;
        return builder.ToImmutable();
    }

    /// <summary>Creates a diagnostic whose message is already fully composed.</summary>
    internal static Diagnostic Create(
        string code,
        Location location,
        string message,
        ImmutableDictionary<string, string?> properties) =>
        Diagnostic.Create(TangentDiagnostics.Get(code), location, properties, message);
}
