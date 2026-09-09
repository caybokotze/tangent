using System.Text.Json;
using Xunit;

namespace Tangent.LanguageServer.Tests;

/// <summary>Request/response tests driving the real server over an in-memory transport.</summary>
public sealed class LanguageServerTests
{
    private const string BrokenTan = "contract Broken {\n    sealed\n";

    private const string UndeclaredLayerTan = """
project "Broken" {
    layer Api = Broken.Api.**
    Api -> Missing
}
""";

    [Fact]
    public async Task Initialize_AdvertisesTheCapabilitiesTheClientNeeds()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);

        JsonElement result = await client.InitializeAsync(folder.Root);
        JsonElement capabilities = result.GetProperty("capabilities");

        Assert.True(capabilities.TryGetProperty("textDocumentSync", out _));
        Assert.True(capabilities.TryGetProperty("completionProvider", out _));
        Assert.True(capabilities.TryGetProperty("hoverProvider", out _));
        Assert.True(capabilities.TryGetProperty("definitionProvider", out _));
        Assert.True(capabilities.TryGetProperty("referencesProvider", out _));

        JsonElement semanticTokens = capabilities.GetProperty("semanticTokensProvider");
        JsonElement tokenTypes = semanticTokens.GetProperty("legend").GetProperty("tokenTypes");
        Assert.Contains("keyword", tokenTypes.EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("namespace", tokenTypes.EnumerateArray().Select(t => t.GetString()));

        Assert.Equal("Tangent Language Server", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Diagnostics_ReportAMissingBraceAsTng1000()
    {
        using TangentFolder folder = TangentFolder.Create(("broken.tan", BrokenTan));
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        JsonElement diagnostics = await client.WaitForDiagnosticsAsync(
            "broken.tan",
            list => list.EnumerateArray().Any(d => d.GetProperty("code").GetString() == "TNG1000"));

        JsonElement parseError = diagnostics.EnumerateArray()
            .First(d => d.GetProperty("code").GetString() == "TNG1000");

        Assert.Equal(1, parseError.GetProperty("severity").GetInt32());
        Assert.Equal("tangent", parseError.GetProperty("source").GetString());
        Assert.Contains("expected '}'", parseError.GetProperty("message").GetString(), StringComparison.Ordinal);

        // The file has two lines of text and ends at offset 29, so the error sits at the very end.
        JsonElement start = parseError.GetProperty("range").GetProperty("start");
        Assert.Equal(2, start.GetProperty("line").GetInt32());
        Assert.Equal(0, start.GetProperty("character").GetInt32());
    }

    [Fact]
    public async Task Diagnostics_ReportAnUndeclaredLayerAsTng1005()
    {
        using TangentFolder folder = TangentFolder.Create(("undeclared.tan", UndeclaredLayerTan));
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        JsonElement diagnostics = await client.WaitForDiagnosticsAsync(
            "undeclared.tan",
            list => list.EnumerateArray().Any(d => d.GetProperty("code").GetString() == "TNG1005"));

        JsonElement undeclared = diagnostics.EnumerateArray()
            .First(d => d.GetProperty("code").GetString() == "TNG1005");

        Assert.Contains("Missing", undeclared.GetProperty("message").GetString(), StringComparison.Ordinal);

        JsonElement start = undeclared.GetProperty("range").GetProperty("start");
        Assert.Equal(2, start.GetProperty("line").GetInt32());
        Assert.Equal(UndeclaredLayerTan.Split('\n')[2].IndexOf("Missing", StringComparison.Ordinal),
            start.GetProperty("character").GetInt32());
    }

    [Fact]
    public async Task Diagnostics_AreRepublishedForFilesTheEditorDoesNotHaveOpen()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);
        await client.WaitForDiagnosticsAsync("bindings.tan");

        // Removing `contract Entity` breaks `extends Entity` here and `from Entity` in bindings.tan,
        // which nobody has open.
        string path = folder.PathOf("entities.tan");
        await client.OpenAsync(path, folder.TextOf("entities.tan"));
        await client.ChangeAsync(path, "contract AggregateRoot extends Entity {\n}\n");

        JsonElement diagnostics = await client.WaitForDiagnosticsAsync(
            "bindings.tan",
            list => list.EnumerateArray().Any(d => d.GetProperty("code").GetString() == "TNG1001"));

        Assert.Contains(
            diagnostics.EnumerateArray(),
            d => d.GetProperty("message").GetString()!.Contains("Entity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_AfterIn_ListsDeclaredLayers()
    {
        const string scratch = "contract Scratch {\n    match: class in \n}\n";
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("scratch.tan");
        await client.OpenAsync(path, scratch);

        JsonElement result = await client.RequestAsync("textDocument/completion", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            position = new { line = 1, character = "    match: class in ".Length },
        });

        List<string?> labels = Items(result).Select(i => i.GetProperty("label").GetString()).ToList();
        Assert.Contains("Api", labels);
        Assert.Contains("Domain", labels);
        Assert.Contains("Infrastructure", labels);
        Assert.DoesNotContain("require", labels);
    }

    [Fact]
    public async Task Completion_AtTopLevel_ListsDeclarationKeywords()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("scratch.tan");
        await client.OpenAsync(path, "\n");

        JsonElement result = await client.RequestAsync("textDocument/completion", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            position = new { line = 0, character = 0 },
        });

        List<string?> labels = Items(result).Select(i => i.GetProperty("label").GetString()).ToList();
        Assert.Contains("contract", labels);
        Assert.Contains("bind", labels);
        Assert.Contains("exempt", labels);
        Assert.Contains("project", labels);
    }

    [Fact]
    public async Task Completion_AfterExtends_ListsContracts()
    {
        const string scratch = "contract Scratch extends ";
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("scratch.tan");
        await client.OpenAsync(path, scratch);

        JsonElement result = await client.RequestAsync("textDocument/completion", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            position = new { line = 0, character = scratch.Length },
        });

        List<string?> labels = Items(result).Select(i => i.GetProperty("label").GetString()).ToList();
        Assert.Contains("Entity", labels);
        Assert.Contains("Controller", labels);
    }

    [Fact]
    public async Task Hover_OnAContractName_SummarisesTheContract()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("entities.tan");
        string text = folder.TextOf("entities.tan");
        await client.OpenAsync(path, text);

        (int line, int character) = Locate(text, "contract AggregateRoot", "AggregateRoot");
        JsonElement result = await client.RequestAsync("textDocument/hover", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            position = new { line, character },
        });

        string value = result.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("**contract** `AggregateRoot`", value, StringComparison.Ordinal);
        Assert.Contains("extends `Entity`", value, StringComparison.Ordinal);
        Assert.Contains("rule", value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_OnALayerName_ShowsItsGlobAndAllowedDependencies()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("project.tan");
        string text = folder.TextOf("project.tan");
        await client.OpenAsync(path, text);

        (int line, int character) = Locate(text, "layer Api ", "Api");
        JsonElement result = await client.RequestAsync("textDocument/hover", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            position = new { line, character },
        });

        string value = result.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("**layer** `Api`", value, StringComparison.Ordinal);
        Assert.Contains("Orders.Api.**", value, StringComparison.Ordinal);
        Assert.Contains("Application", value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Definition_FromExtendsEntity_JumpsToContractEntity()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("entities.tan");
        string text = folder.TextOf("entities.tan");
        await client.OpenAsync(path, text);

        (int line, int character) = Locate(text, "extends Entity", "Entity");
        JsonElement result = await client.RequestAsync("textDocument/definition", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            position = new { line, character },
        });

        JsonElement location = result.ValueKind == JsonValueKind.Array ? result[0] : result;
        Assert.EndsWith("entities.tan", location.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);

        (int expectedLine, int expectedCharacter) = Locate(text, "contract Entity", "Entity");
        JsonElement start = location.GetProperty("range").GetProperty("start");
        Assert.Equal(expectedLine, start.GetProperty("line").GetInt32());
        Assert.Equal(expectedCharacter, start.GetProperty("character").GetInt32());
    }

    [Fact]
    public async Task Definition_FromALayerReference_JumpsToTheLayerDeclaration()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("controllers.tan");
        string text = folder.TextOf("controllers.tan");
        await client.OpenAsync(path, text);

        (int line, int character) = Locate(text, "class in Api", "Api");
        JsonElement result = await client.RequestAsync("textDocument/definition", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            position = new { line, character },
        });

        JsonElement location = result.ValueKind == JsonValueKind.Array ? result[0] : result;
        Assert.EndsWith("project.tan", location.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task References_ForAContract_FindExtendsAndExempt()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("entities.tan");
        string text = folder.TextOf("entities.tan");
        await client.OpenAsync(path, text);

        (int line, int character) = Locate(text, "contract Entity", "Entity");
        JsonElement result = await client.RequestAsync("textDocument/references", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            position = new { line, character },
            context = new { includeDeclaration = true },
        });

        List<string> uris = result.EnumerateArray()
            .Select(l => l.GetProperty("uri").GetString()!)
            .ToList();

        Assert.Equal(3, uris.Count);
        Assert.Equal(2, uris.Count(u => u.EndsWith("entities.tan", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(uris, u => u.EndsWith("bindings.tan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContractDefinition_ReturnsTheDeclarationLocation()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        JsonElement result = await client.RequestAsync("tangent/contractDefinition", new { contract = "Controller" });

        Assert.EndsWith("controllers.tan", result.GetProperty("uri").GetString(), StringComparison.OrdinalIgnoreCase);

        (int line, int character) = Locate(TangentFolder.ControllersTan, "contract Controller", "Controller");
        JsonElement start = result.GetProperty("range").GetProperty("start");
        Assert.Equal(line, start.GetProperty("line").GetInt32());
        Assert.Equal(character, start.GetProperty("character").GetInt32());
    }

    [Fact]
    public async Task ContractDefinition_ReturnsNullForAnUnknownContract()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        JsonElement result = await client.RequestAsync("tangent/contractDefinition", new { contract = "NoSuchThing" });

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
    }

    [Fact]
    public async Task SemanticTokens_ReturnALegendConsistentArray()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        JsonElement initialize = await client.InitializeAsync(folder.Root);

        JsonElement legend = initialize
            .GetProperty("capabilities")
            .GetProperty("semanticTokensProvider")
            .GetProperty("legend");
        int typeCount = legend.GetProperty("tokenTypes").GetArrayLength();
        int modifierCount = legend.GetProperty("tokenModifiers").GetArrayLength();

        string path = folder.PathOf("entities.tan");
        await client.OpenAsync(path, folder.TextOf("entities.tan"));

        JsonElement result = await client.RequestAsync("textDocument/semanticTokens/full", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
        });

        int[] data = result.GetProperty("data").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        Assert.NotEmpty(data);
        Assert.Equal(0, data.Length % 5);

        int line = 0;
        int character = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i];
            character = data[i] == 0 ? character + data[i + 1] : data[i + 1];
            Assert.True(data[i + 2] > 0, "every token must have a positive length");
            Assert.InRange(data[i + 3], 0, typeCount - 1);
            Assert.InRange(data[i + 4], 0, (1 << modifierCount) - 1);
            Assert.True(character >= 0);
        }

        Assert.True(line > 0, "tokens should span more than one line");
    }

    [Fact]
    public async Task SemanticTokens_MarkContractDeclarationsAsDeclaredClasses()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        JsonElement initialize = await client.InitializeAsync(folder.Root);

        JsonElement legend = initialize
            .GetProperty("capabilities")
            .GetProperty("semanticTokensProvider")
            .GetProperty("legend");
        string[] types = legend.GetProperty("tokenTypes").EnumerateArray().Select(t => t.GetString()!).ToArray();
        string[] modifiers = legend.GetProperty("tokenModifiers").EnumerateArray().Select(t => t.GetString()!).ToArray();

        string path = folder.PathOf("entities.tan");
        string text = folder.TextOf("entities.tan");
        await client.OpenAsync(path, text);

        JsonElement result = await client.RequestAsync("textDocument/semanticTokens/full", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
        });

        List<(int Line, int Character, int Length, string Type, string[] Modifiers)> tokens =
            Decode(result.GetProperty("data").EnumerateArray().Select(v => v.GetInt32()).ToArray(), types, modifiers);

        (int line, int character) = Locate(text, "contract Entity", "Entity");
        (int Line, int Character, int Length, string Type, string[] Modifiers) declaration =
            tokens.Single(t => t.Line == line && t.Character == character);

        Assert.Equal("class", declaration.Type);
        Assert.Contains("declaration", declaration.Modifiers);
        Assert.Equal("Entity".Length, declaration.Length);

        (int keywordLine, int keywordCharacter) = Locate(text, "contract Entity", "contract");
        Assert.Equal("keyword", tokens.Single(t => t.Line == keywordLine && t.Character == keywordCharacter).Type);

        // The fixture opens with a `//` comment.
        Assert.Equal("comment", tokens[0].Type);

        // `Entity<*>` in `inherits Entity<*>` is a type pattern, not a contract reference.
        (int patternLine, int patternCharacter) = Locate(text, "inherits Entity<*>", "Entity");
        (int Line, int Character, int Length, string Type, string[] Modifiers) pattern =
            tokens.Single(t => t.Line == patternLine && t.Character == patternCharacter);
        Assert.Equal("type", pattern.Type);
        Assert.Empty(pattern.Modifiers);
    }

    [Fact]
    public async Task SemanticTokens_SupportRangeRequests()
    {
        using TangentFolder folder = TangentFolder.Create();
        await using var client = new LspTestClient(folder.Root);
        await client.InitializeAsync(folder.Root);

        string path = folder.PathOf("entities.tan");
        await client.OpenAsync(path, folder.TextOf("entities.tan"));

        JsonElement result = await client.RequestAsync("textDocument/semanticTokens/range", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri },
            range = new
            {
                start = new { line = 0, character = 0 },
                end = new { line = 5, character = 0 },
            },
        });

        int[] data = result.GetProperty("data").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        Assert.NotEmpty(data);
        Assert.Equal(0, data.Length % 5);
    }

    /// <summary>Expands the LSP delta encoding back into absolute positions and legend names.</summary>
    private static List<(int Line, int Character, int Length, string Type, string[] Modifiers)> Decode(
        int[] data,
        string[] types,
        string[] modifiers)
    {
        var tokens = new List<(int, int, int, string, string[])>(data.Length / 5);
        int line = 0;
        int character = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i];
            character = data[i] == 0 ? character + data[i + 1] : data[i + 1];
            var names = new List<string>();
            for (int bit = 0; bit < modifiers.Length; bit++)
            {
                if ((data[i + 4] & (1 << bit)) != 0) names.Add(modifiers[bit]);
            }

            tokens.Add((line, character, data[i + 2], types[data[i + 3]], names.ToArray()));
        }

        return tokens;
    }

    private static IEnumerable<JsonElement> Items(JsonElement completionResult)
    {
        JsonElement items = completionResult.ValueKind == JsonValueKind.Array
            ? completionResult
            : completionResult.GetProperty("items");
        return items.EnumerateArray();
    }

    /// <summary>The zero-based position of <paramref name="word"/> inside the first line matching <paramref name="anchor"/>.</summary>
    private static (int Line, int Character) Locate(string text, string anchor, string word)
    {
        int anchorIndex = text.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(anchorIndex >= 0, $"'{anchor}' not found in the fixture");
        int wordIndex = text.IndexOf(word, anchorIndex, StringComparison.Ordinal);
        Assert.True(wordIndex >= 0, $"'{word}' not found after '{anchor}'");

        int line = 0;
        int lineStart = 0;
        for (int i = 0; i < wordIndex; i++)
        {
            if (text[i] != '\n') continue;
            line++;
            lineStart = i + 1;
        }

        return (line, wordIndex - lineStart);
    }
}
