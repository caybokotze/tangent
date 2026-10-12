using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Tangent.Analyzers;
using Tangent.Graph;

namespace Tangent.Cli.Output;

/// <summary>
/// Hand-written SARIF 2.1.0, the shape GitHub code scanning and Azure DevOps ingest. Tangent does not
/// take a dependency on the SARIF SDK: the document is small, fixed and easier to keep honest this way.
/// </summary>
public static class SarifWriter
{
    private const string SchemaUri = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json";

    /// <summary>Renders the log.</summary>
    /// <param name="findings">The findings to report, ignored violations included or not as the caller chooses.</param>
    /// <param name="root">The directory result URIs are relative to; emitted as the <c>SRCROOT</c> base id.</param>
    public static string Write(IReadOnlyList<Finding> findings, string root, Explainer? explainer = null)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", SchemaUri);
            writer.WriteString("version", "2.1.0");

            writer.WriteStartArray("runs");
            writer.WriteStartObject();

            WriteTool(writer);
            WriteOriginalUriBaseIds(writer, root);
            WriteResults(writer, findings, root, explainer);

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void WriteTool(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("tool");
        writer.WriteStartObject("driver");
        writer.WriteString("name", "Tangent");
        writer.WriteString("semanticVersion", Version);
        writer.WriteString("version", Version);
        writer.WriteString("informationUri", "https://github.com/caybokotze/tangent");

        writer.WriteStartArray("rules");
        foreach (DiagnosticDescriptor descriptor in TangentDiagnostics.All)
        {
            writer.WriteStartObject();
            writer.WriteString("id", descriptor.Id);
            writer.WriteString("name", Identifier(descriptor.Title.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            writer.WriteStartObject("shortDescription");
            writer.WriteString("text", descriptor.Title.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteEndObject();

            string description = descriptor.Description.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (description.Length > 0)
            {
                writer.WriteStartObject("fullDescription");
                writer.WriteString("text", description);
                writer.WriteEndObject();
            }

            writer.WriteStartObject("defaultConfiguration");
            writer.WriteString("level", Level(descriptor.DefaultSeverity));
            writer.WriteEndObject();

            if (!string.IsNullOrEmpty(descriptor.HelpLinkUri)) writer.WriteString("helpUri", descriptor.HelpLinkUri);

            writer.WriteStartObject("properties");
            writer.WriteString("category", descriptor.Category);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteOriginalUriBaseIds(Utf8JsonWriter writer, string root)
    {
        writer.WriteStartObject("originalUriBaseIds");
        writer.WriteStartObject("SRCROOT");
        writer.WriteString("uri", DirectoryUri(root));
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteResults(Utf8JsonWriter writer, IReadOnlyList<Finding> findings, string root, Explainer? explainer)
    {
        writer.WriteStartArray("results");

        foreach (Finding finding in findings)
        {
            writer.WriteStartObject();
            writer.WriteString("ruleId", finding.Code);
            writer.WriteString("level", Level(finding.Severity));
            if (finding.Severity == "hidden") writer.WriteString("kind", "informational");

            writer.WriteStartObject("message");
            writer.WriteString("text", finding.Message);
            writer.WriteEndObject();

            if (finding.File.Length > 0)
            {
                writer.WriteStartArray("locations");
                writer.WriteStartObject();
                writer.WriteStartObject("physicalLocation");

                writer.WriteStartObject("artifactLocation");
                writer.WriteString("uri", EscapeUri(GraphBuilder.Relative(root, finding.File)));
                writer.WriteString("uriBaseId", "SRCROOT");
                writer.WriteEndObject();

                writer.WriteStartObject("region");
                writer.WriteNumber("startLine", finding.Line);
                writer.WriteNumber("startColumn", finding.Col);
                writer.WriteNumber("endLine", finding.EndLine);
                writer.WriteNumber("endColumn", finding.EndCol);
                writer.WriteEndObject();

                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteStartObject("properties");
            foreach (KeyValuePair<string, string?> property in finding.Diagnostic.Properties
                         .OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WriteString(property.Key, property.Value ?? string.Empty);
            }

            writer.WriteString("project", finding.Project);

            if (explainer?.Explain(finding) is { } explanation)
            {
                writer.WriteString("ruleText", explanation.Rule);
                if (explanation.Declared is not null) writer.WriteString("ruleDeclared", explanation.Declared);
                if (explanation.AppliesTo is not null) writer.WriteString("appliesTo", explanation.AppliesTo);
            }

            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>The assembly's informational version, which is what the SARIF driver reports.</summary>
    public static string Version { get; } =
        typeof(SarifWriter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } informational
            ? Trim(informational)
            : typeof(SarifWriter).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static string Trim(string informational)
    {
        int plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    private static string Level(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "note",
    };

    private static string Level(string severity) => severity switch
    {
        "error" => "error",
        "warning" => "warning",
        _ => "note",
    };

    private static string Identifier(string title)
    {
        var builder = new StringBuilder(title.Length);
        bool upper = true;
        foreach (char c in title)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true;
            }
        }

        return builder.Length == 0 ? "Rule" : builder.ToString();
    }

    private static string DirectoryUri(string root)
    {
        string full = Path.GetFullPath(root);
        if (!full.EndsWith(Path.DirectorySeparatorChar) && !full.EndsWith('/')) full += Path.DirectorySeparatorChar;
        return new Uri(full).AbsoluteUri;
    }

    private static string EscapeUri(string relative) =>
        string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
}
