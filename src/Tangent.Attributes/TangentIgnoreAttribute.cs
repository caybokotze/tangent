using System;
using System.Diagnostics;

namespace Tangent;

/// <summary>
/// Ignores the Tangent violations of the decorated type or member. A reason is mandatory and is
/// surfaced by <c>tangent check --show-ignored</c>.
/// </summary>
/// <remarks>
/// On a type, the violations of the whole type are ignored; on a member, only the violations of that
/// member, so the rest of the type is still checked. Without <see cref="Contract"/> or <see cref="Rule"/>
/// the ignore applies to every contract and rule. Set <see cref="Contract"/> to ignore one contract only,
/// or <see cref="Rule"/> to ignore one rule id only (for example <c>"Controller/methods/attributes#1"</c>).
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum |
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Field |
    AttributeTargets.Event,
    AllowMultiple = true,
    Inherited = false)]
[Conditional("TANGENT_ATTRIBUTES")]
public sealed class TangentIgnoreAttribute : Attribute
{
    public TangentIgnoreAttribute(string reason)
    {
        Reason = reason;
    }

    /// <summary>Why the violations are ignored. Reported, never hidden.</summary>
    public string Reason { get; }

    /// <summary>Ignore this contract only.</summary>
    public string? Contract { get; init; }

    /// <summary>Ignore this rule id only.</summary>
    public string? Rule { get; init; }
}
