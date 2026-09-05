using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Tangent.Benchmarks;

/// <summary>
/// Chooses between the short job (the default, so a full sweep finishes in a couple of minutes) and
/// BenchmarkDotNet's default job (<c>--long</c>, for numbers worth committing).
/// </summary>
internal static class BenchmarkMode
{
    private static bool _long;

    /// <summary>True when the run was asked for full-length jobs.</summary>
    internal static bool IsLong => _long;

    /// <summary>
    /// Consumes the Tangent-specific switches and returns the remaining arguments for
    /// <see cref="BenchmarkDotNet.Running.BenchmarkSwitcher"/>, which rejects anything it does not know.
    /// </summary>
    internal static string[] Configure(string[] args)
    {
        _long = string.Equals(Environment.GetEnvironmentVariable("TANGENT_BENCH_LONG"), "1", StringComparison.Ordinal);

        if (args is null || args.Length == 0) return Array.Empty<string>();

        var kept = new System.Collections.Generic.List<string>(args.Length);
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--long", StringComparison.OrdinalIgnoreCase))
            {
                _long = true;
                continue;
            }

            kept.Add(arg);
        }

        return kept.ToArray();
    }

    /// <summary>The job every benchmark class runs under.</summary>
    internal static Job Job => _long ? Job.Default : Job.ShortRun;
}

/// <summary>The shared configuration: one job, chosen by <see cref="BenchmarkMode"/>.</summary>
public sealed class TangentConfig : ManualConfig
{
    /// <summary>Creates the configuration.</summary>
    public TangentConfig() => AddJob(BenchmarkMode.Job);
}
