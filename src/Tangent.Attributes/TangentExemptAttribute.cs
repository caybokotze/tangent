using System;
using System.Diagnostics;

namespace Tangent;

/// <summary>
/// Exempts the decorated type or member from Tangent checking. A reason is mandatory and is
/// surfaced by <c>tangent check --show-exemptions</c>.
/// </summary>
/// <remarks>
/// Without <see cref="Contract"/> or <see cref="Rule"/> the exemption applies to every contract and rule.
/// Set <see cref="Contract"/> to exempt from one contract only, or <see cref="Rule"/> to exempt from one
/// rule id only (for example <c>"Controller/methods/attributes#1"</c>).
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum |
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Field |
    AttributeTargets.Event,
    AllowMultiple = true,
    Inherited = false)]
[Conditional("TANGENT_ATTRIBUTES")]
public sealed class TangentExemptAttribute : Attribute
{
    public TangentExemptAttribute(string reason)
    {
        Reason = reason;
    }

    /// <summary>Why this exemption exists. Reported, never hidden.</summary>
    public string Reason { get; }

    /// <summary>Exempt from this contract only.</summary>
    public string? Contract { get; init; }

    /// <summary>Exempt from this rule id only.</summary>
    public string? Rule { get; init; }
}
