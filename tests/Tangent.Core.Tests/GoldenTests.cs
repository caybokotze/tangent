using System.Collections.Immutable;
using Tangent.Core.Binding;
using Tangent.Core.Serialization;
using Tangent.Core.Syntax;
using Xunit;

namespace Tangent.Core.Tests;

/// <summary>Golden tests over <c>cases/</c>: tokens, AST, IR JSON and diagnostics.</summary>
public sealed class GoldenTests
{
    [Theory]
    [MemberData(nameof(GoldenCases.Names), MemberType = typeof(GoldenCases))]
    public void Case_matches_goldens(string name)
    {
        GoldenCase testCase = GoldenCases.Load(name);
        (ImmutableArray<SyntaxTree> trees, BindResult result) = GoldenCases.Run(testCase);

        GoldenCases.Verify(testCase, "tokens.txt", GoldenCases.RenderTokens(trees));
        GoldenCases.Verify(testCase, "ast.txt", GoldenCases.RenderAst(trees));
        GoldenCases.Verify(testCase, "ir.json", IrJsonWriter.Write(result.Model));
        GoldenCases.Verify(testCase, "diagnostics.txt", GoldenCases.RenderDiagnostics(result.Diagnostics));
    }

    [Fact]
    public void Every_case_has_a_tan_file()
    {
        Assert.NotEmpty(GoldenCases.All());
        foreach (GoldenCase testCase in GoldenCases.All())
        {
            Assert.NotEmpty(testCase.Files);
        }
    }

    [Fact]
    public void Reference_examples_bind_cleanly_together()
    {
        GoldenCase testCase = GoldenCases.Load("orders");
        (_, BindResult result) = GoldenCases.Run(testCase);

        Assert.NotNull(result.Model);
        Assert.False(result.HasErrors, GoldenCases.RenderDiagnostics(result.Diagnostics));
        Assert.Equal(4, result.Model!.Layers.Length);
        Assert.Equal(6, result.Model.Contracts.Length);
        Assert.Single(result.Model.Bindings);
        Assert.Equal(2, result.Model.Ignores.Length);
    }
}
