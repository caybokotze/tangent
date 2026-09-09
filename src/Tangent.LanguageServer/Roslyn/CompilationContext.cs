using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tangent.LanguageServer.Roslyn;

/// <summary>One C# type bound to a contract by a <c>[Tangent("...")]</c> attribute.</summary>
/// <param name="FilePath">The C# file the attribute was written in.</param>
/// <param name="Start">Character offset of the attribute argument.</param>
/// <param name="Length">Length of the attribute argument.</param>
/// <param name="Line">Zero-based line of the attribute argument.</param>
/// <param name="Character">Zero-based character of the attribute argument.</param>
/// <param name="EndLine">Zero-based line of the end of the attribute argument.</param>
/// <param name="EndCharacter">Zero-based character of the end of the attribute argument.</param>
internal readonly record struct AttributeBinding(
    string FilePath,
    int Start,
    int Length,
    int Line,
    int Character,
    int EndLine,
    int EndCharacter);

/// <summary>The loaded C# side of the workspace: compilations plus the name index built from them.</summary>
internal sealed class CompilationContext
{
    /// <summary>Creates a context.</summary>
    public CompilationContext(ImmutableArray<Compilation> compilations, TypeIndex types, string source)
    {
        Compilations = compilations;
        Types = types;
        Source = source;
    }

    /// <summary>One compilation per project.</summary>
    public ImmutableArray<Compilation> Compilations { get; }

    /// <summary>The name index.</summary>
    public TypeIndex Types { get; }

    /// <summary>The solution or project file the context was loaded from, for logging.</summary>
    public string Source { get; }

    /// <summary>
    /// Every <c>[Tangent("contract")]</c> attribute argument naming <paramref name="contract"/>.
    /// This is a syntax-only search on purpose: binding every attribute would cost far more than
    /// a "find references" request is worth.
    /// </summary>
    public ImmutableArray<AttributeBinding> FindTangentAttributes(string contract)
    {
        var results = ImmutableArray.CreateBuilder<AttributeBinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Compilation compilation in Compilations)
        {
            foreach (Microsoft.CodeAnalysis.SyntaxTree tree in compilation.SyntaxTrees)
            {
                if (string.IsNullOrEmpty(tree.FilePath)) continue;
                SyntaxNode root = tree.GetRoot();
                foreach (AttributeSyntax attribute in root.DescendantNodes().OfType<AttributeSyntax>())
                {
                    if (!IsTangentAttributeName(attribute.Name)) continue;
                    AttributeArgumentListSyntax? arguments = attribute.ArgumentList;
                    if (arguments is null || arguments.Arguments.Count == 0) continue;

                    if (arguments.Arguments[0].Expression is not LiteralExpressionSyntax literal) continue;
                    if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) continue;
                    if (!string.Equals(literal.Token.ValueText, contract, StringComparison.Ordinal)) continue;

                    Microsoft.CodeAnalysis.Text.TextSpan span = literal.Span;
                    FileLinePositionSpan lines = tree.GetLineSpan(span);
                    string key = tree.FilePath + ":" + span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!seen.Add(key)) continue;

                    results.Add(new AttributeBinding(
                        tree.FilePath,
                        span.Start,
                        span.Length,
                        lines.StartLinePosition.Line,
                        lines.StartLinePosition.Character,
                        lines.EndLinePosition.Line,
                        lines.EndLinePosition.Character));
                }
            }
        }

        return results.ToImmutable();
    }

    private static bool IsTangentAttributeName(NameSyntax name)
    {
        string text = name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => name.ToString(),
        };

        return text is "Tangent" or "TangentAttribute";
    }
}
