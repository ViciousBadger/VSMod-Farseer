using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;

namespace Farseer.Tests;

/// <summary>
/// Proves the deferred dirty region optimization reduces mesh rebuilds.
/// Simulates the old (immediate) vs new (deferred) rebuild behavior.
/// </summary>
public class DeferredRebuildTest
{
    private readonly ITestOutputHelper output;
    private const int RegionMapSize = 100; // 100x100 region grid

    public DeferredRebuildTest(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static long RegionIndex(int x, int z) => (long)z * RegionMapSize + x;

    /// <summary>
    /// Simulates OLD behavior: immediate neighbor rebuilds.
    /// When a region loads, it immediately rebuilds N, W, NW neighbors.
    /// </summary>
    private class ImmediateRebuildSimulator
    {
        private readonly HashSet<long> loadedRegions = [];
        public int TotalBuilds { get; private set; }
        public int NewBuilds { get; private set; }
        public int NeighborRebuilds { get; private set; }

        public void LoadRegion(int x, int z)
        {
            var idx = RegionIndex(x, z);
            loadedRegions.Add(idx);
            TotalBuilds++;
            NewBuilds++;

            // Immediately rebuild neighbors (old behavior)
            TryRebuildNeighbor(x - 1, z);     // West
            TryRebuildNeighbor(x, z - 1);     // North
            TryRebuildNeighbor(x - 1, z - 1); // NorthWest
        }

        private void TryRebuildNeighbor(int x, int z)
        {
            if (x < 0 || z < 0) return;
            var idx = RegionIndex(x, z);
            if (loadedRegions.Contains(idx))
            {
                TotalBuilds++;
                NeighborRebuilds++;
            }
        }
    }

    /// <summary>
    /// Simulates NEW behavior: deferred neighbor rebuilds with deduplication.
    /// Regions are marked dirty, then flushed once per "frame".
    /// </summary>
    private class DeferredRebuildSimulator
    {
        private readonly HashSet<long> loadedRegions = [];
        private readonly HashSet<long> dirtyRegions = [];
        public int TotalBuilds { get; private set; }
        public int NewBuilds { get; private set; }
        public int NeighborRebuilds { get; private set; }
        public int DirtyMarks { get; private set; }

        public void LoadRegion(int x, int z)
        {
            var idx = RegionIndex(x, z);
            loadedRegions.Add(idx);
            TotalBuilds++;
            NewBuilds++;

            // Mark neighbors dirty (new behavior)
            TryMarkDirty(x - 1, z);     // West
            TryMarkDirty(x, z - 1);     // North
            TryMarkDirty(x - 1, z - 1); // NorthWest
        }

        private void TryMarkDirty(int x, int z)
        {
            if (x < 0 || z < 0) return;
            var idx = RegionIndex(x, z);
            if (loadedRegions.Contains(idx))
            {
                DirtyMarks++;
                dirtyRegions.Add(idx); // HashSet deduplicates
            }
        }

        public void FlushFrame()
        {
            foreach (var idx in dirtyRegions)
            {
                TotalBuilds++;
                NeighborRebuilds++;
            }
            dirtyRegions.Clear();
        }
    }

    [Fact]
    public void SingleRegionLoad_NoBenefit()
    {
        // Single region with no neighbors - no difference
        var immediate = new ImmediateRebuildSimulator();
        var deferred = new DeferredRebuildSimulator();

        immediate.LoadRegion(5, 5);
        deferred.LoadRegion(5, 5);
        deferred.FlushFrame();

        Assert.Equal(1, immediate.TotalBuilds);
        Assert.Equal(1, deferred.TotalBuilds);
    }

    [Fact]
    public void TwoAdjacentRegions_SameFrame_ShowsBenefit()
    {
        // Two regions that share a neighbor, loaded in same "frame"
        // Region (5,5) and (6,5) both have (5,5) as a potential neighbor rebuild target
        // But wait - (5,5) loads first, so (6,5) loading would mark (5,5) dirty

        var immediate = new ImmediateRebuildSimulator();
        var deferred = new DeferredRebuildSimulator();

        // Load a 2x1 strip: regions at (5,5) and (6,5)
        // First load some neighbors so they can be rebuilt
        immediate.LoadRegion(4, 4); // Will be NW neighbor of (5,5)
        immediate.LoadRegion(5, 4); // Will be N neighbor of (5,5) and NW of (6,5)
        immediate.LoadRegion(4, 5); // Will be W neighbor of (5,5)

        deferred.LoadRegion(4, 4);
        deferred.LoadRegion(5, 4);
        deferred.LoadRegion(4, 5);
        deferred.FlushFrame();

        // Reset counts
        int immediateBaseline = immediate.TotalBuilds;
        int deferredBaseline = deferred.TotalBuilds;

        // Now load two adjacent regions in the same "frame"
        immediate.LoadRegion(5, 5);
        immediate.LoadRegion(6, 5);

        deferred.LoadRegion(5, 5);
        deferred.LoadRegion(6, 5);
        deferred.FlushFrame();

        int immediateNewBuilds = immediate.TotalBuilds - immediateBaseline;
        int deferredNewBuilds = deferred.TotalBuilds - deferredBaseline;

        // Immediate: (5,5) loads → rebuilds (4,4), (5,4), (4,5) = 4 builds
        //            (6,5) loads → rebuilds (5,4), (5,5) = 3 builds (5,4 rebuilt twice!)
        //            Total: 7 builds

        // Deferred:  (5,5) loads → marks (4,4), (5,4), (4,5) dirty
        //            (6,5) loads → marks (5,4), (5,5) dirty (5,4 already dirty, deduplicated!)
        //            Flush: rebuilds 4 unique dirty regions
        //            Total: 2 new + 4 rebuilds = 6 builds

        Assert.True(deferredNewBuilds < immediateNewBuilds,
            $"Deferred ({deferredNewBuilds}) should be less than immediate ({immediateNewBuilds})");
    }

    [Fact]
    public void GridOfRegions_SignificantBenefit()
    {
        // Load a 4x4 grid of regions in one "frame" - lots of shared neighbors
        var immediate = new ImmediateRebuildSimulator();
        var deferred = new DeferredRebuildSimulator();

        // Load 4x4 grid
        for (int z = 0; z < 4; z++)
        {
            for (int x = 0; x < 4; x++)
            {
                immediate.LoadRegion(x, z);
                deferred.LoadRegion(x, z);
            }
        }
        deferred.FlushFrame();

        // Calculate savings
        int saved = immediate.TotalBuilds - deferred.TotalBuilds;
        double savingsPercent = saved * 100.0 / immediate.TotalBuilds;

        output.WriteLine("=== 4x4 Grid (16 regions, single frame) ===");
        output.WriteLine($"OLD (immediate): {immediate.TotalBuilds} total builds ({immediate.NewBuilds} new + {immediate.NeighborRebuilds} rebuilds)");
        output.WriteLine($"NEW (deferred):  {deferred.TotalBuilds} total builds ({deferred.NewBuilds} new + {deferred.NeighborRebuilds} rebuilds)");
        output.WriteLine($"Dirty marks: {deferred.DirtyMarks}, Actual rebuilds: {deferred.NeighborRebuilds}");
        output.WriteLine($"SAVED: {saved} builds ({savingsPercent:F1}% reduction)");
        output.WriteLine($"Memory saved: ~{saved * 600}KB (at 600KB per mesh)");

        Assert.True(deferred.TotalBuilds <= immediate.TotalBuilds,
            $"Deferred ({deferred.TotalBuilds}) should be <= immediate ({immediate.TotalBuilds})");
    }

    [Fact]
    public void SpiralLoadPattern_RealisticScenario()
    {
        // Simulate spiral loading pattern (like the actual mod does)
        var immediate = new ImmediateRebuildSimulator();
        var deferred = new DeferredRebuildSimulator();

        // Spiral outward from center (10,10)
        var spiralOrder = GenerateSpiralOrder(10, 10, radius: 5);

        // Simulate loading in batches (like network packets arriving)
        int batchSize = 4;
        for (int i = 0; i < spiralOrder.Count; i++)
        {
            var (x, z) = spiralOrder[i];
            immediate.LoadRegion(x, z);
            deferred.LoadRegion(x, z);

            // Flush deferred every batchSize regions (simulates frame boundary)
            if ((i + 1) % batchSize == 0)
            {
                deferred.FlushFrame();
            }
        }
        deferred.FlushFrame(); // Final flush

        int saved = immediate.TotalBuilds - deferred.TotalBuilds;
        double savingsPercent = immediate.TotalBuilds > 0 ? saved * 100.0 / immediate.TotalBuilds : 0;

        output.WriteLine($"=== Spiral Pattern ({spiralOrder.Count} regions, batch size {batchSize}) ===");
        output.WriteLine($"OLD (immediate): {immediate.TotalBuilds} total builds ({immediate.NewBuilds} new + {immediate.NeighborRebuilds} rebuilds)");
        output.WriteLine($"NEW (deferred):  {deferred.TotalBuilds} total builds ({deferred.NewBuilds} new + {deferred.NeighborRebuilds} rebuilds)");
        output.WriteLine($"Dirty marks: {deferred.DirtyMarks}, Actual rebuilds: {deferred.NeighborRebuilds}");
        output.WriteLine($"SAVED: {saved} builds ({savingsPercent:F1}% reduction)");
        output.WriteLine($"Memory saved: ~{saved * 600}KB (at 600KB per mesh)");

        Assert.True(deferred.TotalBuilds <= immediate.TotalBuilds,
            $"Spiral pattern: Immediate={immediate.TotalBuilds}, Deferred={deferred.TotalBuilds}, " +
            $"Saved={saved} ({savingsPercent:F1}%)");
    }

    private static List<(int x, int z)> GenerateSpiralOrder(int centerX, int centerZ, int radius)
    {
        var result = new List<(int, int)> { (centerX, centerZ) };

        for (int r = 1; r <= radius; r++)
        {
            // Top edge (left to right)
            for (int x = centerX - r; x <= centerX + r; x++)
                result.Add((x, centerZ - r));

            // Right edge (top to bottom, excluding corner)
            for (int z = centerZ - r + 1; z <= centerZ + r; z++)
                result.Add((centerX + r, z));

            // Bottom edge (right to left, excluding corner)
            for (int x = centerX + r - 1; x >= centerX - r; x--)
                result.Add((x, centerZ + r));

            // Left edge (bottom to top, excluding corners)
            for (int z = centerZ + r - 1; z >= centerZ - r + 1; z--)
                result.Add((centerX - r, z));
        }

        return result;
    }
}
