using Microsoft.CodeAnalysis;

namespace Tangent.Cli;

/// <summary>
/// One Tangent diagnostic, flattened out of Roslyn's <see cref="Diagnostic"/> together with everything
/// the analyzer's property bag carries. Every output format — console, JSON, SARIF, the graph — reads
/// this and never re-parses a message.
/// </summary>
public sealed class Finding
{
    internal Finding(Diagnostic diagnostic, string projectName, DocumentId? documentId, string nodeId)
    {
        Diagnostic = diagnostic;
        Project = projectName;
        DocumentId = documentId;
        NodeId = nodeId;

        Code = diagnostic.Id;
        Message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Severity = SeverityName(diagnostic.Severity);

        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        File = diagnostic.Location.IsInSource || span.Path.Length > 0 ? span.Path : string.Empty;
        Line = span.StartLinePosition.Line + 1;
        Col = span.StartLinePosition.Character + 1;
        EndLine = span.EndLinePosition.Line + 1;
        EndCol = span.EndLinePosition.Character + 1;

        Contract = Property(diagnostic, "contract");
        RuleId = Property(diagnostic, "ruleId");
        Member = Property(diagnostic, "member");
        Tier = Property(diagnostic, "tier");
        Fix = diagnostic.Properties.TryGetValue("fix", out string? fix) ? fix : null;
        FixArg = diagnostic.Properties.TryGetValue("fixArg", out string? fixArg) ? fixArg : null;
    }

    /// <summary>The Roslyn diagnostic this was built from; <c>tangent diff</c> hands it to the code fix provider.</summary>
    public Diagnostic Diagnostic { get; }

    /// <summary>The diagnostic id, for example <c>TNG0104</c>.</summary>
    public string Code { get; }

    /// <summary>
    /// <c>error</c>, <c>warning</c>, <c>info</c> or <c>hidden</c>. <c>--strict</c> promotes warnings to
    /// errors here, so every output format sees the same severity.
    /// </summary>
    public string Severity { get; private set; }

    /// <summary>Promotes a warning to an error. Used by <c>--strict</c>.</summary>
    public void Promote()
    {
        if (Severity == "warning") Severity = "error";
    }

    /// <summary>The full message.</summary>
    public string Message { get; }

    /// <summary>The absolute path of the file this points at, or "" when there is none.</summary>
    public string File { get; }

    /// <summary>1-based start line.</summary>
    public int Line { get; }

    /// <summary>1-based start column.</summary>
    public int Col { get; }

    /// <summary>1-based end line.</summary>
    public int EndLine { get; }

    /// <summary>1-based end column.</summary>
    public int EndCol { get; }

    /// <summary>The contract name, or "" for layer, model and binding diagnostics.</summary>
    public string Contract { get; }

    /// <summary>The stable rule id, or "".</summary>
    public string RuleId { get; }

    /// <summary>The member name, or "".</summary>
    public string Member { get; }

    /// <summary>The rule tier, or the pseudo-tier <c>binding</c> / <c>exemption</c> / <c>model</c>.</summary>
    public string Tier { get; }

    /// <summary>The mechanical fix kind, or null when there is none.</summary>
    public string? Fix { get; }

    /// <summary>The fix's argument, or null.</summary>
    public string? FixArg { get; }

    /// <summary>The name of the project the diagnostic was produced in.</summary>
    public string Project { get; }

    /// <summary>The document the diagnostic points into, or null (a <c>.tan</c> file, for example).</summary>
    public DocumentId? DocumentId { get; }

    /// <summary>The id of the type the diagnostic was reported inside, or "".</summary>
    public string NodeId { get; }

    /// <summary>True when the exemption note TNG9000 is what produced this.</summary>
    public bool IsExemption => string.Equals(Code, "TNG9000", StringComparison.Ordinal);

    /// <summary>True when this is a model diagnostic (TNG1xxx) reported against the <c>.tan</c> files.</summary>
    public bool IsModelDiagnostic => Code.Length == 7 && Code.StartsWith("TNG1", StringComparison.Ordinal);

    /// <summary>The heading this finding is grouped under in the console report.</summary>
    public string Group
    {
        get
        {
            if (Contract.Length > 0) return Contract;
            if (string.Equals(Code, "TNG0202", StringComparison.Ordinal)) return "Layers";
            if (IsModelDiagnostic) return "Model";
            if (Code.StartsWith("TNG000", StringComparison.Ordinal)) return "Binding";
            return "Tangent";
        }
    }

    private static string Property(Diagnostic diagnostic, string key) =>
        diagnostic.Properties.TryGetValue(key, out string? value) && value is not null ? value : string.Empty;

    internal static string SeverityName(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Info => "info",
        _ => "hidden",
    };
}
