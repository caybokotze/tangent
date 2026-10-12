namespace Tangent.Cli;

/// <summary>The process exit codes every Tangent command shares (see docs/cli.md).</summary>
public static class ExitCodes
{
    /// <summary>Nothing to report.</summary>
    public const int Clean = 0;

    /// <summary>Violations at or above <c>--fail-on</c>.</summary>
    public const int Violations = 1;

    /// <summary>The <c>.tan</c> model itself has errors (a TNG1xxx error), so no enforcement happened.</summary>
    public const int ModelError = 2;

    /// <summary>The solution, project or <c>.tangent</c> folder could not be loaded.</summary>
    public const int LoadFailure = 3;
}
