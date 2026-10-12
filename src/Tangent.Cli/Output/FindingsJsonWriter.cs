using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tangent.Graph;

namespace Tangent.Cli.Output;

/// <summary>The <c>--json</c> format: a plain array, one object per finding, deterministically ordered.</summary>
public static class FindingsJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>One row of the JSON report.</summary>
    public sealed record Row(
        string Code,
        string Severity,
        string Message,
        string File,
        int Line,
        int Col,
        int EndLine,
        int EndCol,
        string Contract,
        string RuleId,
        string Member,
        string Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ExplainRow? Explain = null);

    /// <summary>The <c>--explain</c> part of a row: the rule text, its declaration, what applies it, and the offending line.</summary>
    public sealed record ExplainRow(string Rule, string? Declared, string? AppliesTo, string? Source);

    /// <summary>Renders the array. With an <paramref name="explainer"/>, each row also carries <c>explain</c>.</summary>
    public static string Write(IReadOnlyList<Finding> findings, string root, Explainer? explainer = null)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var rows = new List<Row>(findings.Count);
        foreach (Finding finding in findings)
        {
            rows.Add(new Row(
                finding.Code,
                finding.Severity,
                finding.Message,
                GraphBuilder.Relative(root, finding.File),
                finding.Line,
                finding.Col,
                finding.EndLine,
                finding.EndCol,
                finding.Contract,
                finding.RuleId,
                finding.Member,
                finding.Project,
                explainer?.Explain(finding) is { } explanation
                    ? new ExplainRow(explanation.Rule, explanation.Declared, explanation.AppliesTo, explanation.Source)
                    : null));
        }

        rows.Sort(static (a, b) =>
        {
            int byFile = string.CompareOrdinal(a.File, b.File);
            if (byFile != 0) return byFile;
            int byLine = a.Line.CompareTo(b.Line);
            if (byLine != 0) return byLine;
            int byCol = a.Col.CompareTo(b.Col);
            if (byCol != 0) return byCol;
            int byCode = string.CompareOrdinal(a.Code, b.Code);
            return byCode != 0 ? byCode : string.CompareOrdinal(a.Message, b.Message);
        });

        return JsonSerializer.Serialize(rows, Options).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }
}
