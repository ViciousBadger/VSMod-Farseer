using BenchmarkDotNet.Running;

namespace Farseer.Tests;

/// <summary>
/// Entry point for running benchmarks.
/// Run with: dotnet run -c Release
/// </summary>
public class BenchmarkProgram
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<PopulateRegionBenchmark>();
    }
}
