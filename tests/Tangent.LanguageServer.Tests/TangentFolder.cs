namespace Tangent.LanguageServer.Tests;

/// <summary>
/// A throwaway workspace on disk: a temp directory holding a <c>.tangent</c> folder. There is no
/// solution anywhere near it, so every test exercises the "no compilation" path, which is the one
/// that must never regress.
/// </summary>
internal sealed class TangentFolder : IDisposable
{
    private TangentFolder(string root)
    {
        Root = root;
        Folder = Path.Combine(root, ".tangent");
    }

    /// <summary>The workspace root passed to <c>initialize</c>.</summary>
    public string Root { get; }

    /// <summary>The <c>.tangent</c> directory.</summary>
    public string Folder { get; }

    /// <summary>The absolute path of a file in the folder.</summary>
    public string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>The contents of a file in the folder.</summary>
    public string TextOf(string fileName) => File.ReadAllText(PathOf(fileName));

    /// <summary>Creates a folder containing the given files plus any extras.</summary>
    public static TangentFolder Create(params (string Name, string Text)[] extraFiles)
    {
        string root = Path.Combine(Path.GetTempPath(), "tangent-lsp-" + Guid.NewGuid().ToString("N"));
        var folder = new TangentFolder(root);
        Directory.CreateDirectory(folder.Folder);

        foreach ((string name, string text) in Defaults.Concat(extraFiles))
        {
            File.WriteAllText(folder.PathOf(name), text);
        }

        return folder;
    }

    /// <summary>The reference model from <c>samples/Orders/.tangent</c>, trimmed to what the tests need.</summary>
    public static IReadOnlyList<(string Name, string Text)> Defaults { get; } = new[]
    {
        ("project.tan", ProjectTan),
        ("entities.tan", EntitiesTan),
        ("controllers.tan", ControllersTan),
        ("bindings.tan", BindingsTan),
    };

    /// <summary>The layer declarations and the dependency matrix.</summary>
    public const string ProjectTan = """
project "Orders" {
    root: "src/"

    layer Api            = Orders.Api.**
    layer Application    = Orders.Application.**
    layer Domain         = Orders.Domain.**
    layer Infrastructure = Orders.Infrastructure.**

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing
}
""";

    /// <summary>Two contracts, the second extending the first.</summary>
    public const string EntitiesTan = """
// Entities are constructed through factories.

contract Entity {
    match: class in Orders.Domain.Entities.**

    inherits Entity<*> | AggregateRoot<*>

    properties {
        forbid public set
    }
    forbid public fields
    ctor { forbid public }

    forbid using System.Data.**, Microsoft.**
}

contract AggregateRoot extends Entity {
    match: class in Orders.Domain.Entities.** where inherits AggregateRoot<*>
    methods where public { forbid returns void }
}
""";

    /// <summary>A contract that names a layer in its match clause.</summary>
    public const string ControllersTan = """
contract Controller {
    match: class in Api where name ends with "Controller"

    inherits ApiControllerBase
    sealed
    attributes [ApiController], [Route(*)]

    ctor {
        require public
        injects only IMediator, ILogger<*>
    }

    forbid using Orders.Infrastructure.**
}
""";

    /// <summary>Explicit bindings and exemptions, which reference contracts by name.</summary>
    public const string BindingsTan = """
bind Orders.Api.Controllers.HealthController to Controller except [ProducesResponseType]

exempt Orders.Infrastructure.Repositories.LegacyReportRepository reason "migrating in Q4 2026"
exempt Orders.Domain.Entities.AuditEntry from Entity reason "append-only log"
""";

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory left behind is not worth failing a test over.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}
