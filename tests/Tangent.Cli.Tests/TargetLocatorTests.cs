using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>Resolving the <c>path</c> argument and finding the <c>.tangent</c> folder.</summary>
public sealed class TargetLocatorTests
{
    [Fact]
    public void FindsTheSingleSolutionInADirectory()
    {
        Target? target = TargetLocator.Resolve(Cli.OrdersSample, out string? error);

        Assert.Null(error);
        Assert.NotNull(target);
        Assert.True(target!.IsSolution);
        Assert.EndsWith("Orders.slnx", target.Path, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(Cli.OrdersSample), target.Root);
        Assert.EndsWith(".tangent", target.TangentFolder!, StringComparison.Ordinal);
        Assert.Equal(6, target.ContractFiles.Count);
    }

    [Fact]
    public void AcceptsASolutionFileDirectly()
    {
        Target? target = TargetLocator.Resolve(Path.Combine(Cli.OrdersSample, "Orders.slnx"), out string? error);

        Assert.Null(error);
        Assert.True(target!.IsSolution);
    }

    [Fact]
    public void AcceptsASingleProject()
    {
        string project = Path.Combine(Cli.OrdersSample, "src", "Orders.Domain", "Orders.Domain.csproj");
        Target? target = TargetLocator.Resolve(project, out string? error);

        Assert.Null(error);
        Assert.False(target!.IsSolution);

        // The .tangent folder lives three directories up, and the search has to walk to it.
        Assert.Equal(Path.Combine(Cli.OrdersSample, ".tangent"), target.TangentFolder);
    }

    [Fact]
    public void ReportsADirectoryWithNothingInIt()
    {
        Assert.Null(TargetLocator.Resolve(Path.Combine(Cli.RepositoryRoot, "docs"), out string? error));
        Assert.Contains("no .sln, .slnx or .csproj file", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAMissingPath()
    {
        Assert.Null(TargetLocator.Resolve(Path.Combine(Cli.RepositoryRoot, "nope"), out string? error));
        Assert.Contains("does not exist", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheProjectsOutOfAnSlnx()
    {
        IReadOnlyList<string> projects = TargetLocator.ProjectsOf(Path.Combine(Cli.OrdersSample, "Orders.slnx"));

        Assert.Equal(5, projects.Count);
        Assert.Contains(projects, p => p.EndsWith("Orders.Api.csproj", StringComparison.Ordinal));
        Assert.Contains(projects, p => p.EndsWith("Tangent.Attributes.csproj", StringComparison.Ordinal));
    }
}
