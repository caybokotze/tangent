using Tangent.Core.Ir;
using Tangent.Core.Text;
using Tangent.Graph;

namespace Tangent.Cli;

/// <summary>
/// Why a finding is a violation, for <c>tangent check --explain</c>: the rule as written, where it is
/// declared, what makes it apply to the type, and the line of code that breaks it.
/// </summary>
/// <param name="Rule">The rule rendered back to DSL, inside the block that scopes it.</param>
/// <param name="Declared">The <c>.tan</c> file (and line, for contract rules) that declares the rule, or null.</param>
/// <param name="AppliesTo">The <c>match</c> clause or <c>layer</c> line that puts the type under the rule, or null.</param>
/// <param name="Source">The offending line with its indentation removed, or null when it cannot be read.</param>
/// <param name="SourceIndent">How many characters of indentation <paramref name="Source"/> lost.</param>
public sealed record Explanation(string Rule, string? Declared, string? AppliesTo, string? Source, int SourceIndent);

/// <summary>Builds an <see cref="Explanation"/> per finding from the bound model and the source files.</summary>
public sealed class Explainer
{
    private readonly ArchitectureModel _model;
    private readonly string _root;
    private readonly Dictionary<string, Rule> _rules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]?> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Indexes every rule in <paramref name="model"/> by id.</summary>
    /// <param name="model">The bound model.</param>
    /// <param name="root">The directory paths are printed relative to.</param>
    public Explainer(ArchitectureModel model, string root)
    {
        _model = model;
        _root = root;

        for (int i = 0; i < model.Contracts.Length; i++)
        {
            foreach (Rule rule in model.Contracts[i].Rules)
            {
                // Inherited rules keep the parent's id; the parent's own copy is the one it declared.
                if (rule.OriginContract == i) _rules.TryAdd(rule.Id.Value, rule);
            }
        }
    }

    /// <summary>Explains <paramref name="finding"/>, or returns null when it belongs to no rule (a model or binding diagnostic).</summary>
    public Explanation? Explain(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (finding.RuleId.Length == 0) return null;

        string rule;
        string? declared;
        string? appliesTo = null;

        if (_rules.TryGetValue(finding.RuleId, out Rule found))
        {
            rule = IrText.DescribeScopedRule(_model, found);
            declared = Location(found.Span, withLine: true);

            int contract = _model.FindContract(finding.Contract);
            if (contract >= 0 && _model.Contracts[contract].Match is MatchClause match)
            {
                appliesTo = "contract " + finding.Contract + " matches " + IrText.DescribeMatch(_model, match);
            }
        }
        else if (finding.Diagnostic.Properties.TryGetValue("fromLayer", out string? fromLayer) &&
                 fromLayer is not null &&
                 _model.FindLayer(fromLayer) is var layer and >= 0)
        {
            rule = string.Equals(finding.Code, "TNG0205", StringComparison.Ordinal)
                ? IrText.DescribeAllowList(_model)
                : IrText.DescribeLayerDependencies(_model, layer);

            // The -> lines carry no span of their own, so point at the file rather than at the wrong line.
            Layer declaration = _model.Layers[layer];
            declared = Location(declaration.DeclarationSpan, withLine: false);
            appliesTo = "layer " + fromLayer + " = " + _model.Globs[declaration.GlobIndex].Text;
        }
        else
        {
            return null;
        }

        (string? source, int indent) = SourceLine(finding);
        return new Explanation(rule, declared, appliesTo, source, indent);
    }

    private string? Location(SourceSpan span, bool withLine)
    {
        string? path = _model.FilePathOf(span);
        if (path is null) return null;
        string relative = GraphBuilder.Relative(_root, path);
        return withLine ? relative + ":" + span.Line : relative;
    }

    private (string? Text, int Indent) SourceLine(Finding finding)
    {
        if (finding.File.Length == 0) return (null, 0);

        if (!_files.TryGetValue(finding.File, out string[]? lines))
        {
            try
            {
                lines = File.ReadAllLines(finding.File);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lines = null;
            }

            _files[finding.File] = lines;
        }

        if (lines is null || finding.Line < 1 || finding.Line > lines.Length) return (null, 0);

        // Tabs become single spaces so that a caret under the text still lines up column for column.
        string line = lines[finding.Line - 1].Replace('\t', ' ');
        int indent = line.Length - line.TrimStart(' ').Length;
        return (line.Trim(' '), indent);
    }
}
