using BenchmarkDotNet.Running;

namespace Tangent.Benchmarks;

/// <summary>
/// Entry point for <c>Tangent.Benchmarks</c>.
/// <para>
/// <c>dotnet run -c Release --project tests/Tangent.Benchmarks -- --filter '*'</c> runs everything that is
/// not opted out of the default set. Every benchmark uses a short job unless <c>--long</c> is passed (or
/// <c>TANGENT_BENCH_LONG=1</c> is set), in which case BenchmarkDotNet's default job is used.
/// </para>
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        args = BenchmarkMode.Configure(args);
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
