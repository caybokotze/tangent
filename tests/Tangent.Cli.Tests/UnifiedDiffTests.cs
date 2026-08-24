using Tangent.Cli.Output;
using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>The unified-diff renderer, away from Roslyn.</summary>
public sealed class UnifiedDiffTests
{
    [Fact]
    public void ReturnsNothingWhenTheTextIsUnchanged()
    {
        Assert.Equal(string.Empty, UnifiedDiff.Render("a.cs", "one\ntwo\n", "one\ntwo\n"));
    }

    [Fact]
    public void RendersHeadersAndOneHunk()
    {
        string before = string.Join('\n', Enumerable.Range(1, 10).Select(i => "line " + i)) + "\n";
        string after = before.Replace("line 5", "LINE 5", StringComparison.Ordinal);

        string diff = UnifiedDiff.Render("src/a.cs", before, after);

        Assert.StartsWith("--- a/src/a.cs\n+++ b/src/a.cs\n@@ -2,7 +2,7 @@\n", diff, StringComparison.Ordinal);
        Assert.Contains("-line 5\n", diff, StringComparison.Ordinal);
        Assert.Contains("+LINE 5\n", diff, StringComparison.Ordinal);
        Assert.Contains(" line 2\n", diff, StringComparison.Ordinal);   // three lines of context either side
        Assert.Contains(" line 8\n", diff, StringComparison.Ordinal);
        Assert.DoesNotContain(" line 1\n", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitsDistantChangesIntoSeparateHunks()
    {
        string before = string.Join('\n', Enumerable.Range(1, 40).Select(i => "line " + i));
        string after = before
            .Replace("line 3\n", "line 3 changed\n", StringComparison.Ordinal)
            .Replace("line 30\n", "line 30 changed\n", StringComparison.Ordinal);

        string diff = UnifiedDiff.Render("a.cs", before, after);

        Assert.Equal(2, diff.Split("@@ -").Length - 1);
    }

    [Fact]
    public void MergesChangesThatAreCloseTogether()
    {
        string before = string.Join('\n', Enumerable.Range(1, 20).Select(i => "line " + i));
        string after = before
            .Replace("line 8\n", "line 8 changed\n", StringComparison.Ordinal)
            .Replace("line 10\n", "line 10 changed\n", StringComparison.Ordinal);

        string diff = UnifiedDiff.Render("a.cs", before, after);

        Assert.Equal(1, diff.Split("@@ -").Length - 1);
    }

    [Fact]
    public void HandlesInsertionsAtTheTopOfAFile()
    {
        string diff = UnifiedDiff.Render("a.cs", "b\nc\n", "a\nb\nc\n");

        Assert.Contains("@@ -1,2 +1,3 @@\n+a\n b\n c\n", diff, StringComparison.Ordinal);
    }
}
