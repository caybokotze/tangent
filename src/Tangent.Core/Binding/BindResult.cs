using System.Collections.Immutable;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;

namespace Tangent.Core.Binding;

/// <summary>The outcome of binding a set of syntax trees into an <see cref="ArchitectureModel"/>.</summary>
public sealed class BindResult
{
    /// <summary>Creates a result.</summary>
    public BindResult(ArchitectureModel? model, ImmutableArray<TangentDiagnostic> diagnostics)
    {
        Model = model;
        Diagnostics = diagnostics.IsDefault ? ImmutableArray<TangentDiagnostic>.Empty : diagnostics;
        bool errors = false;
        for (int i = 0; i < Diagnostics.Length; i++)
        {
            if (Diagnostics[i].Severity == DiagnosticSeverity.Error)
            {
                errors = true;
                break;
            }
        }

        HasErrors = errors;
    }

    /// <summary>
    /// The bound model. The binder is best-effort: a model is produced even when errors were reported, so the
    /// language server can keep working. Check <see cref="HasErrors"/> before trusting it for enforcement.
    /// </summary>
    public ArchitectureModel? Model { get; }

    /// <summary>Every parse, lex and bind diagnostic, in file then position order.</summary>
    public ImmutableArray<TangentDiagnostic> Diagnostics { get; }

    /// <summary>True when at least one diagnostic has error severity.</summary>
    public bool HasErrors { get; }
}
