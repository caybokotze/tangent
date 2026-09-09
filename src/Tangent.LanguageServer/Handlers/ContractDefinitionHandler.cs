using MediatR;
using OmniSharp.Extensions.JsonRpc;
using Tangent.LanguageServer.Features;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Handlers;

/// <summary>Parameters of the custom <c>tangent/contractDefinition</c> request.</summary>
[Method(ContractDefinitionHandler.MethodName, Direction.ClientToServer)]
public sealed record ContractDefinitionParams : IRequest<ContractDefinitionResponse?>
{
    /// <summary>The contract name as written in <c>[Tangent("...")]</c>.</summary>
    public string Contract { get; init; } = string.Empty;
}

/// <summary>
/// The result of <c>tangent/contractDefinition</c>: an LSP <c>Location</c>, or <c>null</c> when
/// the contract is unknown. It is serialised exactly like a <c>Location</c>, which is what the
/// VS Code client's <c>RequestType&lt;…, Location | null, …&gt;</c> expects.
/// </summary>
public sealed record ContractDefinitionResponse
{
    /// <summary>The URI of the <c>.tan</c> file declaring the contract.</summary>
    public OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri Uri { get; init; } = default!;

    /// <summary>The range covering the contract name in <c>contract &lt;name&gt;</c>.</summary>
    public LspRange Range { get; init; } = default!;
}

/// <summary>
/// Resolves a contract name to its <c>contract &lt;name&gt;</c> declaration. The VS Code extension
/// registers a C# definition provider that delegates to this when the cursor sits inside the string
/// argument of a <c>[Tangent("...")]</c> attribute.
/// </summary>
[Method(MethodName, Direction.ClientToServer)]
internal sealed class ContractDefinitionHandler : IJsonRpcRequestHandler<ContractDefinitionParams, ContractDefinitionResponse?>
{
    /// <summary>The JSON-RPC method name.</summary>
    public const string MethodName = "tangent/contractDefinition";

    private readonly TangentWorkspace _workspace;

    /// <summary>Creates the handler.</summary>
    public ContractDefinitionHandler(TangentWorkspace workspace) => _workspace = workspace;

    /// <inheritdoc/>
    public Task<ContractDefinitionResponse?> Handle(ContractDefinitionParams request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Contract)) return Task.FromResult<ContractDefinitionResponse?>(null);

        WorkspaceSnapshot snapshot = _workspace.GetSnapshot();
        LspLocation? location = Navigation.ContractDeclaration(snapshot, request.Contract);
        if (location is null) return Task.FromResult<ContractDefinitionResponse?>(null);

        return Task.FromResult<ContractDefinitionResponse?>(new ContractDefinitionResponse
        {
            Uri = location.Uri,
            Range = location.Range,
        });
    }
}
