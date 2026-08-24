using System.Text;

namespace Tangent.Cli.Output;

/// <summary>
/// Renders a unified diff between two versions of a file, with three lines of context — the format
/// <c>git apply</c> and every review tool understands.
/// </summary>
public static class UnifiedDiff
{
    /// <summary>How many unchanged lines are shown either side of a change.</summary>
    public const int Context = 3;

    /// <summary>Renders the diff, or "" when the two texts are identical.</summary>
    /// <param name="path">The path shown in the <c>---</c> / <c>+++</c> header, with forward slashes.</param>
    /// <param name="oldText">The text before the change.</param>
    /// <param name="newText">The text after it.</param>
    public static string Render(string path, string oldText, string newText)
    {
        if (string.Equals(oldText, newText, StringComparison.Ordinal)) return string.Empty;

        string[] before = SplitLines(oldText);
        string[] after = SplitLines(newText);

        List<Op> operations = Diff(before, after);
        List<Hunk> hunks = Group(operations);
        if (hunks.Count == 0) return string.Empty;

        var builder = new StringBuilder();
        builder.Append("--- a/").Append(path).Append('\n');
        builder.Append("+++ b/").Append(path).Append('\n');

        foreach (Hunk hunk in hunks)
        {
            builder.Append("@@ -").Append(hunk.OldStart).Append(',').Append(hunk.OldCount)
                   .Append(" +").Append(hunk.NewStart).Append(',').Append(hunk.NewCount)
                   .Append(" @@\n");

            for (int i = hunk.First; i <= hunk.Last; i++)
            {
                Op op = operations[i];
                builder.Append(op.Kind switch { OpKind.Add => '+', OpKind.Remove => '-', _ => ' ' })
                       .Append(op.Text)
                       .Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string[] SplitLines(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        // A file that ends in a newline splits to a trailing "" that is not a line. Drop it so the diff
        // does not end in a phantom context line.
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private enum OpKind { Keep, Remove, Add }

    private readonly record struct Op(OpKind Kind, string Text);

    private sealed record Hunk(int First, int Last, int OldStart, int OldCount, int NewStart, int NewCount);

    private static List<Op> Diff(string[] before, string[] after)
    {
        var operations = new List<Op>(before.Length + after.Length);

        int prefix = 0;
        while (prefix < before.Length && prefix < after.Length &&
               string.Equals(before[prefix], after[prefix], StringComparison.Ordinal))
        {
            operations.Add(new Op(OpKind.Keep, before[prefix]));
            prefix++;
        }

        int suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix &&
               string.Equals(before[^(suffix + 1)], after[^(suffix + 1)], StringComparison.Ordinal))
        {
            suffix++;
        }

        int oldLength = before.Length - prefix - suffix;
        int newLength = after.Length - prefix - suffix;

        // Longest common subsequence over the part that actually differs. Contract fixes touch a handful
        // of lines, so the quadratic table never gets large in practice.
        var lengths = new int[oldLength + 1, newLength + 1];
        for (int i = oldLength - 1; i >= 0; i--)
        {
            for (int j = newLength - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(before[prefix + i], after[prefix + j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        int x = 0;
        int y = 0;
        while (x < oldLength && y < newLength)
        {
            if (string.Equals(before[prefix + x], after[prefix + y], StringComparison.Ordinal))
            {
                operations.Add(new Op(OpKind.Keep, before[prefix + x]));
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                operations.Add(new Op(OpKind.Remove, before[prefix + x]));
                x++;
            }
            else
            {
                operations.Add(new Op(OpKind.Add, after[prefix + y]));
                y++;
            }
        }

        while (x < oldLength) operations.Add(new Op(OpKind.Remove, before[prefix + x++]));
        while (y < newLength) operations.Add(new Op(OpKind.Add, after[prefix + y++]));

        for (int i = 0; i < suffix; i++) operations.Add(new Op(OpKind.Keep, before[before.Length - suffix + i]));

        return operations;
    }

    private static List<Hunk> Group(List<Op> operations)
    {
        var oldLines = new int[operations.Count];
        var newLines = new int[operations.Count];
        int oldLine = 1;
        int newLine = 1;

        for (int i = 0; i < operations.Count; i++)
        {
            oldLines[i] = oldLine;
            newLines[i] = newLine;
            switch (operations[i].Kind)
            {
                case OpKind.Keep: oldLine++; newLine++; break;
                case OpKind.Remove: oldLine++; break;
                default: newLine++; break;
            }
        }

        // Every run of changes, padded with Context lines either side, then merged where the padding
        // of two runs meets.
        var ranges = new List<(int First, int Last)>();
        int index = 0;
        while (index < operations.Count)
        {
            if (operations[index].Kind == OpKind.Keep) { index++; continue; }

            int start = index;
            while (index < operations.Count && operations[index].Kind != OpKind.Keep) index++;

            int first = Math.Max(0, start - Context);
            int last = Math.Min(operations.Count - 1, index - 1 + Context);

            if (ranges.Count > 0 && first <= ranges[^1].Last + 1)
            {
                ranges[^1] = (ranges[^1].First, Math.Max(ranges[^1].Last, last));
            }
            else
            {
                ranges.Add((first, last));
            }
        }

        var hunks = new List<Hunk>(ranges.Count);
        foreach ((int first, int last) in ranges)
        {
            int oldCount = 0;
            int newCount = 0;
            for (int i = first; i <= last; i++)
            {
                if (operations[i].Kind != OpKind.Add) oldCount++;
                if (operations[i].Kind != OpKind.Remove) newCount++;
            }

            hunks.Add(new Hunk(first, last, oldLines[first], oldCount, newLines[first], newCount));
        }

        return hunks;
    }
}
