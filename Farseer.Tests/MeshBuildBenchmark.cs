using System.Buffers;
using BenchmarkDotNet.Attributes;

namespace Farseer.Tests;

/// <summary>
/// Benchmark to measure mesh array pooling impact.
/// Run with: dotnet run -c Release --project Farseer.Tests
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MeshBuildBenchmark
{
    private const int GridSize = 128;

    // Array sizes matching actual mesh builds
    private const int VertexCount = (GridSize + 1) * (GridSize + 1);  // 16,641
    private const int XyzLength = VertexCount * 3;                      // 49,923 floats (~200KB)
    private const int IndicesCount = GridSize * GridSize * 6;           // 98,304 ints (~393KB)

    /// <summary>
    /// Simulates building multiple meshes without pooling (old behavior).
    /// Each build allocates ~600KB that becomes garbage.
    /// </summary>
    [Benchmark(Baseline = true)]
    [Arguments(1)]
    [Arguments(10)]
    [Arguments(50)]
    public void BuildMeshes_NoPooling(int meshCount)
    {
        for (int i = 0; i < meshCount; i++)
        {
            var xyz = new float[XyzLength];
            var indices = new int[IndicesCount];

            // Simulate filling arrays (prevents dead code elimination)
            xyz[0] = i;
            indices[0] = i;

            // Arrays become garbage after this scope
        }
    }

    /// <summary>
    /// Simulates building multiple meshes with ArrayPool (new behavior).
    /// Arrays are reused, zero allocations after warmup.
    /// </summary>
    [Benchmark]
    [Arguments(1)]
    [Arguments(10)]
    [Arguments(50)]
    public void BuildMeshes_WithPooling(int meshCount)
    {
        for (int i = 0; i < meshCount; i++)
        {
            var xyz = ArrayPool<float>.Shared.Rent(XyzLength);
            var indices = ArrayPool<int>.Shared.Rent(IndicesCount);

            // Simulate filling arrays
            xyz[0] = i;
            indices[0] = i;

            // Return to pool - no garbage created
            ArrayPool<float>.Shared.Return(xyz);
            ArrayPool<int>.Shared.Return(indices);
        }
    }
}
