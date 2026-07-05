namespace Tangent.Core.Diagnostics;

/// <summary>How loudly a Tangent diagnostic is reported by default.</summary>
public enum DiagnosticSeverity : byte
{
    /// <summary>Not surfaced unless explicitly requested.</summary>
    Hidden = 0,

    /// <summary>Informational.</summary>
    Info = 1,

    /// <summary>A warning; does not fail a build by default.</summary>
    Warning = 2,

    /// <summary>An error.</summary>
    Error = 3,
}

/// <summary>Which part of Tangent produces a diagnostic.</summary>
public enum DiagnosticCategory : byte
{
    /// <summary>Contract binding problems found by the analyzer (TNG00xx).</summary>
    Binding = 0,

    /// <summary>Structural (tier 0) conformance violations (TNG01xx).</summary>
    Structural = 1,

    /// <summary>Reference and layering (tier 1) violations (TNG02xx).</summary>
    Reference = 2,

    /// <summary>Flow (tier 2) violations (TNG03xx).</summary>
    Flow = 3,

    /// <summary>Problems with the <c>.tan</c> model itself (TNG10xx).</summary>
    Model = 4,

    /// <summary>Exemption reporting (TNG9000).</summary>
    Exemption = 5,
}
