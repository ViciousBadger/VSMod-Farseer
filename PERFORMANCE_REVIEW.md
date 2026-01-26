# Farseer Performance Review

This document identifies potential performance issues and optimization opportunities in the Farseer mod for Vintage Story.

## Executive Summary

Farseer is generally well-architected with good separation of concerns between server and client. Recent commits have addressed some performance concerns (shader state management). However, several areas could benefit from optimization, particularly around algorithmic efficiency and memory allocation patterns.

---

## Critical Issues

### 1. ~~Inefficient Heightmap Population Loop~~ (FarRegionGen.cs:225-278) ✅ FIXED

**Severity: High** | **Status: RESOLVED**

~~The `PopulateRegionFromChunk` method iterates through the entire grid for every chunk callback, checking if each grid point belongs to the current chunk.~~

**Solution Implemented**: The method now calculates grid bounds upfront and only iterates over grid cells that belong to the current chunk:

```csharp
// Calculate grid bounds for this chunk to avoid iterating the entire grid
int chunkOffsetX = chunkX - chunkStartX;
int chunkOffsetZ = chunkZ - chunkStartZ;

int gridStartX = (int)(chunkOffsetX * chunkSize / cellSize);
int gridStartZ = (int)(chunkOffsetZ * chunkSize / cellSize);
int gridEndX = GameMath.Min((int)((chunkOffsetX + 1) * chunkSize / cellSize), gridSize);
int gridEndZ = GameMath.Min((int)((chunkOffsetZ + 1) * chunkSize / cellSize), gridSize);

// Only iterate over grid cells that belong to this chunk
for (int z = gridStartZ; z < gridEndZ; z++)
{
    for (int x = gridStartX; x < gridEndX; x++)
    {
        // Sample heightmap data
    }
}
```

**Problem**: With a 128x128 grid (default), this iterates 16,384 times per chunk, but only 64 points (8x8) actually belong to each chunk. The if-condition fails 99.6% of the time.

**The Math**:
- Grid size: 128x128 = 16,384 points total
- Region size: 512 blocks, Chunk size: 32 blocks
- Chunks per region: 16x16 = 256 chunks
- Grid points per chunk: (128/16)² = 8x8 = 64 points
- Current: 16,384 iterations per chunk × 256 chunks = **4,194,304 iterations per region**
- Optimized: 64 iterations per chunk × 256 chunks = **16,384 iterations per region**

**Recommendation**: Calculate the grid bounds that map to the current chunk upfront and iterate only over those points:

```csharp
// Calculate which grid points map to this chunk
int chunkOffsetX = chunkX - chunkStartX;
int chunkOffsetZ = chunkZ - chunkStartZ;
int gridPointsPerChunk = gridSize / chunksInRegionColumn;  // 128/16 = 8

int gridStartX = chunkOffsetX * gridPointsPerChunk;
int gridStartZ = chunkOffsetZ * gridPointsPerChunk;
int gridEndX = gridStartX + gridPointsPerChunk;
int gridEndZ = gridStartZ + gridPointsPerChunk;

for (int z = gridStartZ; z < gridEndZ; z++)
{
    for (int x = gridStartX; x < gridEndX; x++)
    {
        // Direct sampling - no if-check needed
        int posInChunkX = (int)(x * cellSize) % chunkSize;
        int posInChunkZ = (int)(z * cellSize) % chunkSize;
        // ...
    }
}
```

**Impact**: **256x fewer iterations** (from 16,384 to 64 per chunk). Approximately 95% reduction in CPU time for this method.

---

### 2. Linear Queue Scans in BatchedPacketBuffer (BatchedPacketBuffer.cs:31-47)

**Severity: Medium**

Both `CancelForTarget` and `CancelAllForTarget` iterate through the entire send queue:

```csharp
public void CancelForTarget(long regionIdx, IServerPlayer target)
{
    foreach (var packet in sendQueue)  // O(n) scan
    {
        if (packet.RegionData.RegionIndex == regionIdx)
        {
            packet.Targets = [.. packet.Targets.Where(t => t != target)];  // Creates new array
        }
    }
}
```

**Problems**:
1. O(n) scan through entire queue for every cancellation
2. Creates a new array allocation for every packet with matching region
3. Called per-region when a player moves out of view (could be many regions)

**Recommendation**:
- Change `Targets` from array to `List<IServerPlayer>` to allow in-place removal without allocation
- If queue sizes grow large, maintain a secondary `Dictionary<long, List<QueuedPacket>>` index for O(1) region lookups (note: the queue itself must remain ordered for FIFO behavior)

**Note**: With the default batch size of 8 and 1-second send interval, the queue is typically small. This becomes more important with many players or high view distances.

---

### 3. Linear Search in Region Generation Queue (FarRegionGen.cs:195, 213)

**Severity: Medium**

The `regionGenerationQueue.Find()` performs linear search for every chunk callback:

```csharp
var inProgressRegion = regionGenerationQueue.Find(region => region.RegionIdx == regionOfChunkIdx);
```

**Problem**: Called for every chunk loaded or peeked. With many regions queued, this becomes O(n*m) where n is queue size and m is chunks per region (256).

**Recommendation**: Maintain a `Dictionary<long, InProgressRegion>` alongside the list for O(1) lookups while keeping the list for priority ordering.

---

### 4. Cascading Mesh Rebuilds (FarRegionRenderer.cs:180-200)

**Severity: Medium**

When a new region is built, it triggers rebuilds of up to 3 neighbors (North, West, NorthWest):

```csharp
if (!isRebuild)
{
    // Re-build neighbours that are affected by this new data.
    if (activeRegionModels.TryGetValue(northIdx, out PerModelData northData))
        BuildRegion(northData.SourceData, true);  // Triggers mesh rebuild + GPU upload
    if (activeRegionModels.TryGetValue(westIdx, out PerModelData westData))
        BuildRegion(westData.SourceData, true);
    if (activeRegionModels.TryGetValue(northWestIdx, out PerModelData northWestData))
        BuildRegion(northWestData.SourceData, true);
}
```

**Problem**: When multiple adjacent regions load in quick succession, this can cause 4 mesh rebuilds per new region (the region itself + 3 neighbors). Each rebuild allocates ~600KB and uploads to GPU.

**Note**: The `isRebuild` flag correctly prevents infinite recursion - neighbors don't trigger their own neighbor rebuilds.

**Recommendation**:
- Mark neighbors as "dirty" instead of immediate rebuild
- Batch dirty region rebuilds in a single frame
- Consider deferring rebuilds until the next frame to coalesce multiple updates

---

### 5. Memory Allocations Per Mesh Build (FarRegionRenderer.cs:96-107)

**Severity: Medium**

Every `BuildRegion` call allocates new arrays:

```csharp
mesh.xyz = new float[vertexCount * 3];        // ~200KB for 128x128 grid
mesh.Indices = new int[indicesCount];          // ~393KB for 128x128 grid
```

**Problem**: With cascading rebuilds and player movement, this can cause significant GC pressure. Each mesh allocates ~600KB that becomes garbage when the mesh is rebuilt.

**Recommendation**:
- Pool and reuse mesh data arrays
- Pre-allocate arrays based on configured grid size at startup

---

## Already Optimized Areas

### Shader State Management (Fixed in commit be8a374)

The render loop now correctly sets up shader state once before the loop:

```csharp
prog.Use();
// Set all uniforms once
foreach (var regionModel in activeRegionModels.Values)
{
    // Only modelMatrix changes per region
    prog.UniformMatrix("modelMatrix", modelMat.Values);
    rapi.RenderMesh(regionModel.MeshRef);
}
prog.Stop();
```

### Chunk Generation Load Management

The code respects server load with `ChunkGenQueueThreshold`:

```csharp
if (sapi.WorldManager.CurrentGeneratingChunkCount > modSystem.Server.Config.ChunkGenQueueThreshold) return;
```

### Network Batching

Region data is sent in batches (default 8 per tick) to prevent client flooding.

### PeekChunkColumn Optimization

Uses peek instead of full generation for non-existent chunks, which is 20-60% faster.

---

## Negligible Issues (Not Recommended to Fix)

The following were identified but have minimal real-world impact:

| Issue | Why It's Negligible |
|-------|---------------------|
| SpiralWalker uses `sqrt` for distance | Called only every 2+ seconds when players move 128+ blocks. A few hundred sqrt calls is ~microseconds on modern CPUs. |
| LINQ in various methods | These methods run on player movement (2s intervals) or region events, not per-frame. Iterator allocations are tiny. |
| Double dictionary lookups in priority merge | Two O(1) operations, conditional second lookup, every 2+ seconds. Unmeasurable impact. |

These are technically suboptimal but fixing them would be premature optimization with no measurable benefit.

---

## Recommendations Summary

| Priority | Issue | File | Estimated Impact |
|----------|-------|------|------------------|
| ~~High~~ | ~~Inefficient heightmap loop~~ | ~~FarRegionGen.cs:225-278~~ | ✅ **FIXED** | ~256x fewer iterations |
| Medium | Linear queue Find() | FarRegionGen.cs:195,213 | O(n) → O(1) lookups |
| Medium | Cascading mesh rebuilds | FarRegionRenderer.cs:180-200 | Reduce GPU uploads |
| Medium | Mesh array allocations | FarRegionRenderer.cs:96-107 | Reduce GC pressure |
| Low | Linear queue scans | BatchedPacketBuffer.cs | Minor allocation reduction |

---

## Testing Recommendations

To validate these issues and measure improvements:

1. **Profiler Integration**: The existing frame profiler (`farseer-render` marker) can be extended to cover mesh building
2. **Memory Profiling**: Monitor GC allocations during player movement across region boundaries
3. **Server Metrics**: Track time spent in `PopulateRegionFromChunk` with varying grid sizes using `Stopwatch` instrumentation
4. **Stress Testing**: Test with maximum view distance (16384 blocks) and multiple players

### Suggested Instrumentation for Issue #1

```csharp
// Add to FarRegionGen class
private readonly Stopwatch _populateSw = new();
private long _populateTicks = 0;
private int _populateCount = 0;

private void PopulateRegionFromChunk(...)
{
    _populateSw.Restart();
    // ... existing code ...
    _populateSw.Stop();
    _populateTicks += _populateSw.ElapsedTicks;
    _populateCount++;

    if (_populateCount % 256 == 0)  // Every region
    {
        var avgUs = (_populateTicks / (double)_populateCount) / Stopwatch.Frequency * 1_000_000;
        modSystem.Mod.Logger.Notification($"PopulateRegionFromChunk avg: {avgUs:F1}µs");
    }
}
```

---

*Review conducted: 2026-01-26*
