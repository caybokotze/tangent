using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Tangent.Analyzers.Tests;

/// <summary>
/// Exercises the hosting path the CLI and the graph exporter use: a <see cref="TangentAnalyzer"/>
/// constructed with a reference sink, run through <see cref="CompilationWithAnalyzers"/> with the
/// <c>.tan</c> files supplied as <see cref="AdditionalText"/>s.
/// </summary>
public sealed class ReferenceSinkTests
{
    private const string Contracts = """
project "Sample" {
    layer Api    = Sample.Api.**
    layer Domain = Sample.Domain.**

    Api    -> Domain
    Domain -> nothing
}

contract Controller {
    match: class in Api where name ends with "Controller"
    sealed
}
""";

    private const string Source = """
namespace Sample.Domain
{
    public static class Rates
    {
        public static int Value() => 1;
    }
}

namespace Sample.Api
{
    public sealed class OrdersController
    {
        public int Get() => Sample.Domain.Rates.Value();
    }
}
""";

    private sealed class RecordingSink : IReferenceSink
    {
        internal ConcurrentBag<(string Type, ImmutableArray<string> Contracts, string? Layer)> Types { get; } = new();

        internal ConcurrentBag<(string From, string To, ReferenceKind Kind, bool Violates)> References { get; } = new();

        public void OnTypeBound(INamedTypeSymbol type, ImmutableArray<string> contractNames, int? layerIndex, string? layerName) =>
            Types.Add((type.Name, contractNames, layerName));

        public void OnReference(INamedTypeSymbol from, INamedTypeSymbol to, ReferenceKind kind, Location location, bool violatesLayering) =>
            References.Add((from.Name, to.Name, kind, violatesLayering));
    }

    private static async Task<RecordingSink> RunAsync(bool includeUnbound)
    {
        ImmutableArray<MetadataReference> references =
            await Harness.References.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Sample",
            new[] { CSharpSyntaxTree.ParseText(Source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var sink = new RecordingSink();
        var analyzer = new TangentAnalyzer(sink, includeUnbound);
        var options = new AnalyzerOptions(
            ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(".tangent/contracts.tan", Contracts)));

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            options);

        await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        return sink;
    }

    [Fact]
    public async Task BoundTypesAndReferencesReachTheSink()
    {
        RecordingSink sink = await RunAsync(includeUnbound: false);

        (string Type, ImmutableArray<string> Contracts, string? Layer) controller =
            sink.Types.Single(t => t.Type == "OrdersController");

        Assert.Equal(new[] { "Controller" }, controller.Contracts);
        Assert.Equal("Api", controller.Layer);

        Assert.Contains(sink.References, r => r is { From: "OrdersController", To: "Rates", Violates: false });
        Assert.DoesNotContain(sink.Types, t => t.Type == "Rates");
    }

    [Fact]
    public async Task IncludeUnboundAddsTypesBoundToNothing()
    {
        RecordingSink sink = await RunAsync(includeUnbound: true);

        (string Type, ImmutableArray<string> Contracts, string? Layer) rates =
            sink.Types.Single(t => t.Type == "Rates");

        Assert.Empty(rates.Contracts);
        Assert.Equal("Domain", rates.Layer);
    }
}
