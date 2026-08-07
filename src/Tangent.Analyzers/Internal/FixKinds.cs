namespace Tangent.Analyzers.Internal;

/// <summary>
/// The tokens stored in the <c>"fix"</c> diagnostic property. A diagnostic without one has no
/// mechanical fix; <see cref="TangentCodeFixProvider"/> switches on these and never re-parses messages.
/// </summary>
internal static class FixKinds
{
    /// <summary>Add a missing attribute to the type or member.</summary>
    internal const string AddAttribute = "add-attribute";

    /// <summary>Add (or replace) the base type in the base list.</summary>
    internal const string AddBaseType = "add-base-type";

    /// <summary>Append an interface to the base list.</summary>
    internal const string AddInterface = "add-interface";

    /// <summary>Add a modifier such as <c>sealed</c> or <c>readonly</c>.</summary>
    internal const string AddModifier = "add-modifier";

    /// <summary>Remove a modifier; removing an accessibility modifier replaces it with <c>private</c>.</summary>
    internal const string RemoveModifier = "remove-modifier";

    /// <summary>Turn a public setter into <c>init</c> (C# 9+) or <c>private set</c>.</summary>
    internal const string SetterToInit = "setter-to-init";

    /// <summary>Remove a forbidden <c>using</c> directive.</summary>
    internal const string RemoveUsing = "remove-using";
}
