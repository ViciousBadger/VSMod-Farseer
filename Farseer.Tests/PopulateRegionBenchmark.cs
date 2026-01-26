using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Farseer.Tests;

/// <summary>
/// Benchmark to measure PopulateRegionFromChunk optimization impact.
/// Run with: dotnet run -c Release --project Farseer.Tests
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PopulateRegionBenchmark
{
    private const int GridSize = 128;
    private const int ChunkSize = 32;
    private const int RegionSize = 512;

    private int[] heightmapPoints;
    private int[] mockChunkHeightmap;

    [GlobalSetup]
    public void Setup()
    {
        heightmapPoints = new int[GridSize * GridSize];
        mockChunkHeightmap = new int[ChunkSize * ChunkSize];

        // Fill with mock data
        for (int i = 0; i < mockChunkHeightmap.Length; i++)
        {
            mockChunkHeightmap[i] = 100 + (i % 50);
        }
    }

    /// <summary>
    /// Old implementation: iterates entire grid, checks if point belongs to chunk
    /// </summary>
    [Benchmark(Baseline = true)]
    public void PopulateRegion_Baseline()
    {
        int chunkStartX = 0;
        int chunkStartZ = 0;
        int chunkX = 0;
        int chunkZ = 0;
        float cellSize = RegionSize / (float)GridSize;

        for (int z = 0; z < GridSize; z++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int offsetBlockPosX = (int)(x * cellSize);
                int offsetBlockPosZ = (int)(z * cellSize);

                int targetChunkX = chunkStartX + offsetBlockPosX / ChunkSize;
                int targetChunkZ = chunkStartZ + offsetBlockPosZ / ChunkSize;

                if (targetChunkX == chunkX && targetChunkZ == chunkZ)
                {
                    int posInChunkX = offsetBlockPosX % ChunkSize;
                    int posInChunkZ = offsetBlockPosZ % ChunkSize;
                    int chunkHeightmapCoord = posInChunkZ * ChunkSize + posInChunkX;

                    heightmapPoints[z * GridSize + x] = mockChunkHeightmap[chunkHeightmapCoord];
                }
            }
        }
    }

    /// <summary>
    /// Optimized implementation: calculates grid bounds upfront, iterates only relevant cells
    /// </summary>
    [Benchmark]
    public void PopulateRegion_Optimized()
    {
        int chunkStartX = 0;
        int chunkStartZ = 0;
        int chunkX = 0;
        int chunkZ = 0;
        float cellSize = RegionSize / (float)GridSize;

        // Calculate grid bounds for this chunk
        int chunkOffsetX = chunkX - chunkStartX;
        int chunkOffsetZ = chunkZ - chunkStartZ;

        int gridStartX = (int)(chunkOffsetX * ChunkSize / cellSize);
        int gridStartZ = (int)(chunkOffsetZ * ChunkSize / cellSize);
        int gridEndX = Math.Min((int)((chunkOffsetX + 1) * ChunkSize / cellSize), GridSize);
        int gridEndZ = Math.Min((int)((chunkOffsetZ + 1) * ChunkSize / cellSize), GridSize);

        // Only iterate relevant grid cells
        for (int z = gridStartZ; z < gridEndZ; z++)
        {
            for (int x = gridStartX; x < gridEndX; x++)
            {
                int offsetBlockPosX = (int)(x * cellSize);
                int offsetBlockPosZ = (int)(z * cellSize);

                int posInChunkX = offsetBlockPosX % ChunkSize;
                int posInChunkZ = offsetBlockPosZ % ChunkSize;
                int chunkHeightmapCoord = posInChunkZ * ChunkSize + posInChunkX;

                heightmapPoints[z * GridSize + x] = mockChunkHeightmap[chunkHeightmapCoord];
            }
        }
    }
}
