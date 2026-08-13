using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Tangent.Generators;
using Xunit;

namespace Tangent.Analyzers.Tests;

/// <summary>An <see cref="AdditionalText"/> backed by a string, for driving the generator and ContractFiles.</summary>
internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    internal InMemoryAdditionalText(string path, string text)
    {
        Path = path;
        _text = SourceText.From(text);
    }

    public override string Path { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}

public sealed class GeneratorTests
{
    private static string? Run(params AdditionalText[] additionalFiles)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Test",
            new[] { CSharpSyntaxTree.ParseText("namespace Sample { }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new ContractConstantsGenerator().AsSourceGenerator() },
            additionalTexts: additionalFiles);

        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.Empty(result.Diagnostics);
        ImmutableArray<SyntaxTree> generated = result.GeneratedTrees;
        return generated.Length == 0 ? null : generated.Single().ToString();
    }

    [Fact]
    public void EmitsOneConstantPerContractSorted()
    {
        const string contracts = """
contract Repository {
    match: class in Sample.Infrastructure.**
}

contract Controller {
    match: class in Sample.Api.**
}
""";

        string? generated = Run(new InMemoryAdditionalText(".tangent/contracts.tan", contracts));

        Assert.NotNull(generated);
        Assert.Contains("namespace Tangent", generated);
        Assert.Contains("internal static class Contracts", generated);
        Assert.Contains("public const string Controller = \"Controller\";", generated);
        Assert.Contains("public const string Repository = \"Repository\";", generated);
        Assert.True(
            generated!.IndexOf("Controller =", System.StringComparison.Ordinal) <
            generated.IndexOf("Repository =", System.StringComparison.Ordinal),
            "constants must be emitted in sorted order");
    }

    [Fact]
    public void EmitsNothingWithoutContractFiles()
    {
        Assert.Null(Run());
    }

    [Fact]
    public void IgnoresNonContractAdditionalFiles()
    {
        Assert.Null(Run(new InMemoryAdditionalText("readme.md", "contract Controller { }")));
    }
}

public sealed class ContractFilesTests
{
    [Fact]
    public void RecognisesTanFilesByExtension()
    {
        var file = new InMemoryAdditionalText(".tangent/project.tan", "");
        Assert.True(ContractFiles.IsContractFile(file, EmptyOptionsProvider.Instance));
    }

    [Fact]
    public void RejectsOtherFiles()
    {
        var file = new InMemoryAdditionalText("readme.md", "");
        Assert.False(ContractFiles.IsContractFile(file, EmptyOptionsProvider.Instance));
    }

    [Fact]
    public void CacheKeyIsOrderIndependentAndContentSensitive()
    {
        var a = new InMemoryAdditionalText("a.tan", "contract A { }");
        var b = new InMemoryAdditionalText("b.tan", "contract B { }");
        var c = new InMemoryAdditionalText("b.tan", "contract C { }");

        string first = ContractFiles.ComputeCacheKey(ImmutableArray.Create<AdditionalText>(a, b));
        string second = ContractFiles.ComputeCacheKey(ImmutableArray.Create<AdditionalText>(b, a));
        string third = ContractFiles.ComputeCacheKey(ImmutableArray.Create<AdditionalText>(a, c));

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
    }

    [Fact]
    public void LoadModelBindsEveryFileAsOneUnit()
    {
        var project = new InMemoryAdditionalText(".tangent/project.tan", """
project "Sample" {
    layer Api = Sample.Api.**
    Api -> nothing
}
""");

        var controllers = new InMemoryAdditionalText(".tangent/controllers.tan", """
contract Controller {
    match: class in Api where name ends with "Controller"
    sealed
}
""");

        Core.Binding.BindResult result = ContractFiles.LoadModel(
            ImmutableArray.Create<AdditionalText>(project, controllers));

        Assert.NotNull(result.Model);
        Assert.False(result.HasErrors);
        Assert.Single(result.Model!.Layers);
        Assert.Single(result.Model.Contracts);
    }

    [Fact]
    public void LoadModelIsCachedByContent()
    {
        var file = new InMemoryAdditionalText(".tangent/c.tan", "contract A { match: class in X.** }");
        Core.Binding.BindResult first = ContractFiles.LoadModel(ImmutableArray.Create<AdditionalText>(file));
        Core.Binding.BindResult second = ContractFiles.LoadModel(
            ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(".tangent/other.tan", "contract A { match: class in X.** }")));

        Assert.Same(first, second);
    }

    private sealed class EmptyOptionsProvider : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
    {
        internal static readonly EmptyOptionsProvider Instance = new EmptyOptionsProvider();

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions => EmptyOptions.Instance;

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions.Instance;

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions.Instance;

        private sealed class EmptyOptions : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
        {
            internal static readonly EmptyOptions Instance = new EmptyOptions();

            public override bool TryGetValue(string key, out string value)
            {
                value = null!;
                return false;
            }
        }
    }
}
