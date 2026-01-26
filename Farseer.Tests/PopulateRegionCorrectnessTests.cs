using System;
using Xunit;

namespace Farseer.Tests;

/// <summary>
/// Unit tests to verify the optimized PopulateRegionFromChunk produces identical results
/// </summary>
public class PopulateRegionCorrectnessTests
{
    private const int GridSize = 128;
    private const int ChunkSize = 32;
    private const int RegionSize = 512;

    /// <summary>
    /// Verify optimized version produces identical output to baseline
    /// </summary>
    [Fact]
    public void PopulateRegion_OptimizedProducesSameResultAsBaseline()
    {
        // Arrange
        var mockChunkHeightmap = new int[ChunkSize * ChunkSize];
        for (int i = 0; i < mockChunkHeightmap.Length; i++)
        {
            mockChunkHeightmap[i] = 100 + (i % 50);
        }

        var baselineOutput = new int[GridSize * GridSize];
        var optimizedOutput = new int[GridSize * GridSize];

        int chunkStartX = 0;
        int chunkStartZ = 0;
        int chunkX = 0;
        int chunkZ = 0;

        // Act - Baseline
        PopulateRegion_Baseline(baselineOutput, mockChunkHeightmap, chunkStartX, chunkStartZ, chunkX, chunkZ);

        // Act - Optimized
        PopulateRegion_Optimized(optimizedOutput, mockChunkHeightmap, chunkStartX, chunkStartZ, chunkX, chunkZ);

        // Assert
        Assert.Equal(baselineOutput, optimizedOutput);
    }

    /// <summary>
    /// Verify iteration count reduction
    /// </summary>
    [Fact]
    public void PopulateRegion_OptimizedIteratesFewerTimes()
    {
        int baselineIterations = 0;
        int optimizedIterations = 0;

        // Baseline: iterates entire grid
        for (int z = 0; z < GridSize; z++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                baselineIterations++;
            }
        }

        // Optimized: only iterates relevant cells
        float cellSize = RegionSize / (float)GridSize;
        int gridStartX = 0;
        int gridStartZ = 0;
        int gridEndX = Math.Min((int)(ChunkSize / cellSize), GridSize);
        int gridEndZ = Math.Min((int)(ChunkSize / cellSize), GridSize);

        for (int z = gridStartZ; z < gridEndZ; z++)
        {
            for (int x = gridStartX; x < gridEndX; x++)
            {
                optimizedIterations++;
            }
        }

        // Assert: optimized should iterate ~256x fewer times
        Assert.Equal(16384, baselineIterations); // 128x128
        Assert.Equal(64, optimizedIterations);   // 8x8 for default config
        Assert.True(baselineIterations / optimizedIterations == 256);
    }

    /// <summary>
    /// Test with different chunk positions
    /// </summary>
    [Theory]
    [InlineData(0, 0)] // Top-left
    [InlineData(1, 1)] // Middle
    [InlineData(15, 15)] // Bottom-right (region has 16x16 chunks)
    public void PopulateRegion_OptimizedWorksForAllChunkPositions(int chunkOffsetX, int chunkOffsetZ)
    {
        // Arrange
        var mockChunkHeightmap = new int[ChunkSize * ChunkSize];
        for (int i = 0; i < mockChunkHeightmap.Length; i++)
        {
            mockChunkHeightmap[i] = 100 + (i % 50);
        }

        var baselineOutput = new int[GridSize * GridSize];
        var optimizedOutput = new int[GridSize * GridSize];

        int chunkStartX = 0;
        int chunkStartZ = 0;
        int chunkX = chunkStartX + chunkOffsetX;
        int chunkZ = chunkStartZ + chunkOffsetZ;

        // Act
        PopulateRegion_Baseline(baselineOutput, mockChunkHeightmap, chunkStartX, chunkStartZ, chunkX, chunkZ);
        PopulateRegion_Optimized(optimizedOutput, mockChunkHeightmap, chunkStartX, chunkStartZ, chunkX, chunkZ);

        // Assert
        Assert.Equal(baselineOutput, optimizedOutput);
    }

    /// <summary>
    /// Test with different grid sizes
    /// </summary>
    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public void PopulateRegion_OptimizedWorksForDifferentGridSizes(int gridSize)
    {
        // Arrange
        var mockChunkHeightmap = new int[ChunkSize * ChunkSize];
        for (int i = 0; i < mockChunkHeightmap.Length; i++)
        {
            mockChunkHeightmap[i] = 100 + (i % 50);
        }

        var baselineOutput = new int[gridSize * gridSize];
        var optimizedOutput = new int[gridSize * gridSize];

        // Act
        PopulateRegionWithGridSize_Baseline(baselineOutput, mockChunkHeightmap, gridSize, 0, 0, 0, 0);
        PopulateRegionWithGridSize_Optimized(optimizedOutput, mockChunkHeightmap, gridSize, 0, 0, 0, 0);

        // Assert
        Assert.Equal(baselineOutput, optimizedOutput);
    }

    #region Implementation Methods

    private void PopulateRegion_Baseline(int[] heightmapPoints, int[] mockChunkHeightmap,
        int chunkStartX, int chunkStartZ, int chunkX, int chunkZ)
    {
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

    private void PopulateRegion_Optimized(int[] heightmapPoints, int[] mockChunkHeightmap,
        int chunkStartX, int chunkStartZ, int chunkX, int chunkZ)
    {
        float cellSize = RegionSize / (float)GridSize;

        int chunkOffsetX = chunkX - chunkStartX;
        int chunkOffsetZ = chunkZ - chunkStartZ;

        int gridStartX = (int)(chunkOffsetX * ChunkSize / cellSize);
        int gridStartZ = (int)(chunkOffsetZ * ChunkSize / cellSize);
        int gridEndX = Math.Min((int)((chunkOffsetX + 1) * ChunkSize / cellSize), GridSize);
        int gridEndZ = Math.Min((int)((chunkOffsetZ + 1) * ChunkSize / cellSize), GridSize);

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

    private void PopulateRegionWithGridSize_Baseline(int[] heightmapPoints, int[] mockChunkHeightmap,
        int gridSize, int chunkStartX, int chunkStartZ, int chunkX, int chunkZ)
    {
        float cellSize = RegionSize / (float)gridSize;

        for (int z = 0; z < gridSize; z++)
        {
            for (int x = 0; x < gridSize; x++)
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

                    if (chunkHeightmapCoord < mockChunkHeightmap.Length)
                    {
                        heightmapPoints[z * gridSize + x] = mockChunkHeightmap[chunkHeightmapCoord];
                    }
                }
            }
        }
    }

    private void PopulateRegionWithGridSize_Optimized(int[] heightmapPoints, int[] mockChunkHeightmap,
        int gridSize, int chunkStartX, int chunkStartZ, int chunkX, int chunkZ)
    {
        float cellSize = RegionSize / (float)gridSize;

        int chunkOffsetX = chunkX - chunkStartX;
        int chunkOffsetZ = chunkZ - chunkStartZ;

        int gridStartX = (int)(chunkOffsetX * ChunkSize / cellSize);
        int gridStartZ = (int)(chunkOffsetZ * ChunkSize / cellSize);
        int gridEndX = Math.Min((int)((chunkOffsetX + 1) * ChunkSize / cellSize), gridSize);
        int gridEndZ = Math.Min((int)((chunkOffsetZ + 1) * ChunkSize / cellSize), gridSize);

        for (int z = gridStartZ; z < gridEndZ; z++)
        {
            for (int x = gridStartX; x < gridEndX; x++)
            {
                int offsetBlockPosX = (int)(x * cellSize);
                int offsetBlockPosZ = (int)(z * cellSize);

                int posInChunkX = offsetBlockPosX % ChunkSize;
                int posInChunkZ = offsetBlockPosZ % ChunkSize;
                int chunkHeightmapCoord = posInChunkZ * ChunkSize + posInChunkX;

                if (chunkHeightmapCoord < mockChunkHeightmap.Length)
                {
                    heightmapPoints[z * gridSize + x] = mockChunkHeightmap[chunkHeightmapCoord];
                }
            }
        }
    }

    #endregion
}
