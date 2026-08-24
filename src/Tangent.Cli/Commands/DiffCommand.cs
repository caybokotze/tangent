using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using Tangent.Analyzers;
using Tangent.Cli.Output;
using Tangent.Graph;

namespace Tangent.Cli.Commands;

/// <summary>
/// <c>tangent diff</c>: what the mechanical fixes would do, as a unified diff. Reference (Tier 1) and
/// flow (Tier 2) violations carry no fix by design, so this only ever touches the structural ones.
/// </summary>
public static class DiffCommand
{
    /// <summary>Runs the command.</summary>
    public static async Task<int> RunAsync(
        string? path,
        bool apply,
        IReadOnlyList<string> projects,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        Target? target = TargetLocator.Resolve(path, out string? error);
        if (target is null)
        {
            console.WriteError("error: " + error);
            return ExitCodes.LoadFailure;
        }

        using AnalysisResult analysis = await AnalysisRunner
            .RunAsync(target, projects, exporter: null, includeUnbound: false, console, cancellationToken)
            .ConfigureAwait(false);

        var provider = new TangentCodeFixProvider();
        var fixable = new Dictionary<DocumentId, List<Finding>>();

        foreach (Finding finding in analysis.Findings)
        {
            if (finding.Fix is null || finding.DocumentId is null) continue;
            if (!provider.FixableDiagnosticIds.Contains(finding.Code)) continue;

            if (!fixable.TryGetValue(finding.DocumentId, out List<Finding>? list))
            {
                list = [];
                fixable[finding.DocumentId] = list;
            }

            list.Add(finding);
        }

        if (fixable.Count == 0)
        {
            console.WriteLine("Nothing to fix: no violation has a mechanical fix.");
            return ExitCodes.Clean;
        }

        var changes = new List<(DocumentId Id, string Path, string OldText, string NewText, int Applied)>();

        foreach (DocumentId documentId in fixable.Keys
                     .OrderBy(id => analysis.AnalysisSolution.GetDocument(id)?.FilePath ?? string.Empty, StringComparer.Ordinal))
        {
            Document? document = analysis.AnalysisSolution.GetDocument(documentId);
            if (document is null) continue;

            SourceText originalText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            (Document fixedDocument, int applied) = await ApplyFixesAsync(
                document, provider, fixable[documentId], console, cancellationToken).ConfigureAwait(false);

            if (applied == 0) continue;

            SourceText newText = await fixedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (newText.ContentEquals(originalText)) continue;

            changes.Add((documentId, document.FilePath ?? document.Name, originalText.ToString(), newText.ToString(), applied));
        }

        if (changes.Count == 0)
        {
            console.WriteLine("Nothing to fix: every mechanical fix is already applied.");
            return ExitCodes.Clean;
        }

        int total = 0;
        foreach ((_, string file, string oldText, string newText, int applied) in changes)
        {
            total += applied;
            string relative = GraphBuilder.Relative(analysis.Target.Root, file);
            console.WriteLine(Colorise(console, UnifiedDiff.Render(relative, oldText, newText)));
        }

        if (!apply)
        {
            console.WriteLine($"{total} {(total == 1 ? "fix" : "fixes")} in {changes.Count} {(changes.Count == 1 ? "file" : "files")}. Re-run with --apply to write them.");
            return ExitCodes.Violations;
        }

        Solution updated = analysis.OriginalSolution;
        foreach ((DocumentId id, _, _, string newText, _) in changes)
        {
            updated = updated.WithDocumentText(id, SourceText.From(newText));
        }

        if (!analysis.Workspace.TryApplyChanges(updated))
        {
            console.WriteError("error: the workspace refused the changes; nothing was written.");
            return ExitCodes.LoadFailure;
        }

        foreach ((_, string file, _, _, int applied) in changes)
        {
            console.WriteLine($"wrote {GraphBuilder.Relative(analysis.Target.Root, file)} ({applied} {(applied == 1 ? "fix" : "fixes")})");
        }

        return ExitCodes.Clean;
    }

    /// <summary>
    /// Applies every registered fix for one document, one at a time, against the document as it stands.
    /// After each edit the spans of the fixes still to come are shifted through the text changes, which is
    /// what lets several fixes land in one file without re-running the analyzer.
    /// </summary>
    private static async Task<(Document Document, int Applied)> ApplyFixesAsync(
        Document document,
        CodeFixProvider provider,
        List<Finding> findings,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        var pending = new List<PendingFix>(findings.Count);
        foreach (Finding finding in findings) pending.Add(new PendingFix(finding, finding.Diagnostic.Location.SourceSpan, false));
        pending.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));

        Document current = document;
        int applied = 0;

        for (int i = 0; i < pending.Count; i++)
        {
            (Finding finding, TextSpan span, bool skip) = pending[i];
            if (skip) continue;                                // an earlier fix rewrote this span away

            SyntaxTree? tree = await current.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is null) break;
            if (span.End > tree.Length) continue;

            Diagnostic rebased = Diagnostic.Create(
                finding.Diagnostic.Descriptor,
                Location.Create(tree, span),
                finding.Diagnostic.Properties,
                finding.Message);

            CodeAction? action = null;
            var context = new CodeFixContext(
                current,
                rebased,
                (candidate, _) => action ??= candidate,
                cancellationToken);

            await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            if (action is null) continue;

            ImmutableArray<CodeActionOperation> operations;
            try
            {
                operations = await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                console.Trace($"fix for {finding.Code} at {finding.File}:{finding.Line} failed: {exception.Message}");
                continue;
            }

            Solution? changed = null;
            foreach (CodeActionOperation operation in operations)
            {
                if (operation is ApplyChangesOperation apply) { changed = apply.ChangedSolution; break; }
            }

            Document? next = changed?.GetDocument(current.Id);
            if (next is null) continue;

            SourceText before = await current.GetTextAsync(cancellationToken).ConfigureAwait(false);
            SourceText after = await next.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (after.ContentEquals(before)) continue;

            Shift(pending, i + 1, MinimalChanges(before, after));

            current = next;
            applied++;
        }

        return (current, applied);
    }

    /// <summary>One fix waiting its turn, with the span it now occupies in the evolving document.</summary>
    private readonly record struct PendingFix(Finding Finding, TextSpan Span, bool Skip);

    /// <summary>
    /// The smallest set of changes that turns <paramref name="before"/> into <paramref name="after"/>,
    /// computed line by line. <see cref="SourceText.GetTextChanges"/> reports the change the syntax
    /// rewriter made, which for a formatted node is one span covering everything between the first and
    /// last edit — far too coarse to shift the fixes that are still to come. This is the real diff.
    /// </summary>
    private static List<TextChange> MinimalChanges(SourceText before, SourceText after)
    {
        string[] oldLines = LinesOf(before);
        string[] newLines = LinesOf(after);

        int prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length &&
               string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
               string.Equals(oldLines[^(suffix + 1)], newLines[^(suffix + 1)], StringComparison.Ordinal))
        {
            suffix++;
        }

        int oldCount = oldLines.Length - prefix - suffix;
        int newCount = newLines.Length - prefix - suffix;

        var changes = new List<TextChange>();
        if (oldCount == 0 && newCount == 0) return changes;

        var lengths = new int[oldCount + 1, newCount + 1];
        for (int i = oldCount - 1; i >= 0; i--)
        {
            for (int j = newCount - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(oldLines[prefix + i], newLines[prefix + j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        int oldLine = prefix;
        int newLine = prefix;
        int x = 0;
        int y = 0;
        int runOldStart = -1;
        int runNewStart = -1;

        while (x < oldCount || y < newCount)
        {
            bool keep = x < oldCount && y < newCount &&
                        string.Equals(oldLines[prefix + x], newLines[prefix + y], StringComparison.Ordinal);

            if (keep)
            {
                Flush();
                oldLine++;
                newLine++;
                x++;
                y++;
                continue;
            }

            runOldStart = runOldStart < 0 ? StartOf(before, oldLine) : runOldStart;
            runNewStart = runNewStart < 0 ? StartOf(after, newLine) : runNewStart;

            bool remove = y >= newCount || (x < oldCount && lengths[x + 1, y] >= lengths[x, y + 1]);
            if (remove) { oldLine++; x++; }
            else { newLine++; y++; }
        }

        Flush();
        return changes;

        void Flush()
        {
            if (runOldStart < 0) return;

            int oldEnd = StartOf(before, oldLine);
            int newEnd = StartOf(after, newLine);
            changes.Add(new TextChange(
                TextSpan.FromBounds(runOldStart, oldEnd),
                after.ToString(TextSpan.FromBounds(runNewStart, newEnd))));

            runOldStart = -1;
            runNewStart = -1;
        }
    }

    private static string[] LinesOf(SourceText text)
    {
        var lines = new string[text.Lines.Count];
        for (int i = 0; i < lines.Length; i++) lines[i] = text.Lines[i].ToString();
        return lines;
    }

    private static int StartOf(SourceText text, int line) =>
        line >= text.Lines.Count ? text.Length : text.Lines[line].Start;

    /// <summary>Moves the spans of the fixes still to come through a set of text changes.</summary>
    private static void Shift(List<PendingFix> pending, int from, IReadOnlyList<TextChange> changes)
    {
        for (int i = from; i < pending.Count; i++)
        {
            PendingFix entry = pending[i];
            if (entry.Skip) continue;

            int delta = 0;
            bool clobbered = false;

            foreach (TextChange change in changes)
            {
                if (change.Span.End <= entry.Span.Start)
                {
                    delta += (change.NewText?.Length ?? 0) - change.Span.Length;
                }
                else if (change.Span.Start < entry.Span.End)
                {
                    clobbered = true;                          // the edit landed inside this span
                    break;
                }
            }

            pending[i] = clobbered
                ? entry with { Skip = true }
                : entry with { Span = new TextSpan(entry.Span.Start + delta, entry.Span.Length) };
        }
    }

    private static string Colorise(CliConsole console, string diff)
    {
        if (!console.Color || diff.Length == 0) return diff;

        var builder = new System.Text.StringBuilder(diff.Length + 64);
        foreach (string line in diff.Split('\n'))
        {
            if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
            {
                builder.Append(console.Paint(line, AnsiColor.Bold));
            }
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                builder.Append(console.Paint(line, AnsiColor.Cyan));
            }
            else if (line.StartsWith('+'))
            {
                builder.Append(console.Paint(line, AnsiColor.Green));
            }
            else if (line.StartsWith('-'))
            {
                builder.Append(console.Paint(line, AnsiColor.Red));
            }
            else
            {
                builder.Append(line);
            }

            builder.Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }
}
