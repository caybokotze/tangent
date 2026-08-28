using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tangent.Graph;

/// <summary>Serialises a <see cref="GraphDocument"/> as the camelCase, indented JSON the viewer reads.</summary>
public static class GraphJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Renders the document. Line endings are LF so the file diffs cleanly on every platform.</summary>
    public static string Write(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string json = JsonSerializer.Serialize(document, Options);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }
}
