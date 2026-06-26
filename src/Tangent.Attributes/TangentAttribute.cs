using System;
using System.Diagnostics;

namespace Tangent;

/// <summary>
/// Binds the decorated type to a named Tangent contract declared in a <c>.tan</c> file.
/// Explicit binding wins over <c>bind</c> statements and over the contract's <c>match</c> clause.
/// </summary>
/// <remarks>
/// The attribute is <see cref="ConditionalAttribute">conditional</see> on <c>TANGENT_ATTRIBUTES</c>:
/// the compiler strips it from the emitted assembly unless that symbol is defined.
/// The analyzer still sees it because analyzers run on source before conditional elision.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum,
    AllowMultiple = true,
    Inherited = false)]
[Conditional("TANGENT_ATTRIBUTES")]
public sealed class TangentAttribute : Attribute
{
    public TangentAttribute(string contract)
    {
        Contract = contract;
    }

    /// <summary>The name of the contract, exactly as declared in the <c>.tan</c> file.</summary>
    public string Contract { get; }
}
