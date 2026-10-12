using System.Text.Json.Serialization;

namespace Tangent.Graph;

/// <summary>A named region of the codebase with a declared dependency direction.</summary>
/// <param name="Id">The layer name.</param>
/// <param name="Namespaces">The namespace globs that make up the layer.</param>
/// <param name="AllowedDependencies">Ids of the layers this layer may depend on, excluding itself.</param>
public sealed record GraphLayer(
    string Id,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<string> AllowedDependencies);

/// <summary>A contract declared in the <c>.tangent</c> folder.</summary>
/// <param name="Id">The contract name.</param>
/// <param name="File">The declaring <c>.tan</c> file, relative to the solution root.</param>
/// <param name="Line">The 1-based line the contract is declared on.</param>
/// <param name="Extends">The contract this one extends, or null.</param>
/// <param name="RuleCount">How many rules the contract declares itself (inherited rules excluded).</param>
public sealed record GraphContract(
    string Id,
    string File,
    int Line,
    string? Extends,
    int RuleCount);

/// <summary>A public member of a type.</summary>
/// <param name="Name">The member's name; <c>.ctor</c> for a constructor.</param>
/// <param name="Kind">One of <c>method</c>, <c>constructor</c>, <c>property</c>, <c>field</c>, <c>event</c>.</param>
/// <param name="Signature">A C#-like signature rendered with a <c>SymbolDisplayFormat</c>.</param>
public sealed record GraphMember(string Name, string Kind, string Signature);

/// <summary>A C# type in the graph.</summary>
/// <param name="Id">The fully qualified metadata name, for example <c>Orders.Api.Controllers.OrdersController</c>.</param>
/// <param name="Name">The simple name.</param>
/// <param name="Kind">One of <c>class</c>, <c>record</c>, <c>struct</c>, <c>interface</c>, <c>enum</c>.</param>
/// <param name="Layer">The layer the type's namespace falls in, or null.</param>
/// <param name="Contracts">The contracts the type is bound to; empty when it is bound to none.</param>
/// <param name="File">The declaring file, relative to the solution root.</param>
/// <param name="Line">The 1-based line the type is declared on.</param>
/// <param name="Violations">How many violations were reported against this type.</param>
/// <param name="Exemptions">How many exemptions were applied to this type.</param>
/// <param name="Members">The type's public members.</param>
/// <param name="FileId">The id of <paramref name="File"/> in the file table, or -1.</param>
public sealed record GraphNode(
    string Id,
    string Name,
    string Kind,
    string? Layer,
    IReadOnlyList<string> Contracts,
    string File,
    int Line,
    int Violations,
    int Exemptions,
    IReadOnlyList<GraphMember> Members,
    int FileId);

/// <summary>
/// One source file in the file table (TANGENT-PLAN.md §11.4). The viewer addresses files by
/// <paramref name="Id"/> only, so no API ever has to accept a path.
/// </summary>
/// <param name="Id">The file's index in the table.</param>
/// <param name="Path">The path relative to the solution root.</param>
/// <param name="Kind"><c>csharp</c> or <c>contract</c> (a <c>.tan</c> file).</param>
/// <param name="Project">The project that compiles the file, or null for a contract file.</param>
/// <param name="Lines">The number of lines in the file.</param>
/// <param name="Status">
/// For C# files: <c>tracked</c> when every type it declares is bound to a contract, <c>partial</c> when
/// some are, <c>untracked</c> when none are or it declares no type. <c>contract</c> for <c>.tan</c> files.
/// </param>
/// <param name="Types">The ids of the types the file declares, in declaration order.</param>
/// <param name="Violations">How many violations point into the file.</param>
/// <param name="Text">The whole file, for contract files exported with <c>--include-source</c>; otherwise null.</param>
public sealed record GraphFile(
    int Id,
    string Path,
    string Kind,
    string? Project,
    int Lines,
    string Status,
    IReadOnlyList<string> Types,
    int Violations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Text);

/// <summary>Lines of source around a violation, embedded by <c>--include-source</c>.</summary>
/// <param name="StartLine">The 1-based line number of the first line in <paramref name="Lines"/>.</param>
/// <param name="Lines">The source lines, without line terminators.</param>
public sealed record GraphSnippet(int StartLine, IReadOnlyList<string> Lines);

/// <summary>An aggregated reference from one type to another.</summary>
/// <param name="From">The referencing type's id.</param>
/// <param name="To">The referenced type's id.</param>
/// <param name="Kind">Always <c>reference</c>: the edge aggregates every kind of reference in the pair.</param>
/// <param name="Count">How many references the analyzer's Tier 1 walk observed.</param>
/// <param name="ViolatesLayering">True when at least one of them crosses a forbidden layer boundary.</param>
public sealed record GraphEdge(
    string From,
    string To,
    string Kind,
    int Count,
    bool ViolatesLayering);

/// <summary>A single rule failure, as the viewer shows it.</summary>
/// <param name="FileId">The id of <paramref name="File"/> in the file table, or -1.</param>
/// <param name="Snippet">The code around the violation, with <c>--include-source</c>; otherwise null.</param>
public sealed record GraphViolation(
    string Node,
    string? Member,
    string RuleId,
    string Code,
    string Message,
    string File,
    int Line,
    int Col,
    string Severity,
    int FileId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GraphSnippet? Snippet);

/// <summary>An exemption that suppressed a rule for one type or member (a TNG9000 note).</summary>
/// <param name="Node">The id of the exempted type, or "" when unknown.</param>
/// <param name="Member">The exempted member, or null for the whole type.</param>
/// <param name="RuleId">The rule the exemption suppressed.</param>
/// <param name="Reason">The exemption's reason, or where it was declared.</param>
/// <param name="FileId">The id of <paramref name="File"/> in the file table, or -1.</param>
public sealed record GraphExemption(
    string Node,
    string? Member,
    string RuleId,
    string Reason,
    string File,
    int Line,
    int FileId);

/// <summary>The <c>.tan</c> text of one rule, so the viewer can show it next to a violation.</summary>
/// <param name="Id">The stable rule id.</param>
/// <param name="Contract">The contract the rule was written in.</param>
/// <param name="Text">The rule rendered back to DSL-like text.</param>
/// <param name="Tier">0 = symbol, 1 = reference, 2 = flow.</param>
public sealed record GraphRule(string Id, string Contract, string Text, int Tier);

/// <summary>
/// The whole <c>tangent.graph.json</c> document (TANGENT-PLAN.md §11.1). Property order here is the
/// order the writer emits, which keeps the output diffable.
/// </summary>
/// <param name="Root">
/// The absolute solution root with forward slashes, so the viewer can build <c>vscode://file/</c> links.
/// Only written with <c>--include-source</c>, because it is machine-specific.
/// </param>
public sealed record GraphDocument(
    int Version,
    string Project,
    string GeneratedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Root,
    IReadOnlyList<GraphLayer> Layers,
    IReadOnlyList<GraphContract> Contracts,
    IReadOnlyList<GraphFile> Files,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<GraphViolation> Violations,
    IReadOnlyList<GraphExemption> Exemptions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<GraphRule>? Rules);

/// <summary>
/// One diagnostic, flattened to exactly what the graph needs. The CLI builds these from Roslyn
/// diagnostics so that <see cref="Tangent.Graph"/> never has to know how the CLI reports things.
/// </summary>
/// <param name="NodeId">The id of the type the diagnostic was reported inside, or "" when unknown.</param>
/// <param name="Member">The member name from the diagnostic's property bag, or null.</param>
/// <param name="RuleId">The stable rule id from the property bag.</param>
/// <param name="Code">The diagnostic id, for example <c>TNG0104</c>.</param>
/// <param name="Message">The full message.</param>
/// <param name="File">The absolute path of the file the diagnostic points at.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Col">1-based column.</param>
/// <param name="Severity">One of <c>error</c>, <c>warning</c>, <c>info</c>, <c>hidden</c>.</param>
public sealed record GraphFinding(
    string NodeId,
    string? Member,
    string RuleId,
    string Code,
    string Message,
    string File,
    int Line,
    int Col,
    string Severity);
