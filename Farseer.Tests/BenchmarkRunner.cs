using BenchmarkDotNet.Running;

namespace Farseer.Tests;

/// <summary>
/// Entry point for running benchmarks.
/// Run with: dotnet run -c Release --project Farseer.Tests
/// </summary>
public class BenchmarkProgram
{
    public static void Main(string[] args)
    {
        // Run all benchmarks or select via command line
        BenchmarkSwitcher.FromAssembly(typeof(BenchmarkProgram).Assembly).Run(args);
    }
}
