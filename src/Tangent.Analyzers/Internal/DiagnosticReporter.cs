using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tangent.Analyzers.Internal;

/// <summary>
/// A stack-only sink that forwards diagnostics to whichever analysis context is in scope, so the
/// reference walk can be shared between the symbol action and the operation actions without closures.
/// </summary>
internal readonly ref struct DiagnosticReporter
{
    private const int FromSymbol = 0;
    private const int FromOperation = 1;
    private const int FromOperationBlock = 2;

    private readonly SymbolAnalysisContext _symbol;
    private readonly OperationAnalysisContext _operation;
    private readonly OperationBlockAnalysisContext _block;
    private readonly int _source;

    internal DiagnosticReporter(SymbolAnalysisContext context)
    {
        _symbol = context;
        _operation = default;
        _block = default;
        _source = FromSymbol;
    }

    internal DiagnosticReporter(OperationAnalysisContext context)
    {
        _symbol = default;
        _operation = context;
        _block = default;
        _source = FromOperation;
    }

    internal DiagnosticReporter(OperationBlockAnalysisContext context)
    {
        _symbol = default;
        _operation = default;
        _block = context;
        _source = FromOperationBlock;
    }

    /// <summary>Forwards a diagnostic to the underlying context.</summary>
    internal void Report(Diagnostic diagnostic)
    {
        switch (_source)
        {
            case FromOperation: _operation.ReportDiagnostic(diagnostic); break;
            case FromOperationBlock: _block.ReportDiagnostic(diagnostic); break;
            default: _symbol.ReportDiagnostic(diagnostic); break;
        }
    }
}
