# Farseer Performance Review

This document identifies potential performance issues and optimization opportunities in the Farseer mod for Vintage Story.

## Executive Summary

Farseer is generally well-architected with good separation of concerns between server and client. Recent commits have addressed some performance concerns (shader state management). However, several areas could benefit from optimization, particularly around algorithmic efficiency and memory allocation patterns.

---

## Critical Issues

### 1. ~~Inefficient Heightmap Population Loop~~ (FarRegionGen.cs:225-278) ✅ FIXED

**Severity: Medium-High** | **Status: RESOLVED**

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

**Impact**: Reduces iterations by ~256x for typical configurations (from 16,384 to ~64 per chunk with default settings).

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
- Use a `Dictionary<long, Packet>` for O(1) region lookups
- Modify Targets in-place using `List<IServerPlayer>` instead of arrays
- Consider using `HashSet<IServerPlayer>` for O(1) target removal

---

### 3. Linear Search in Region Generation Queue (FarRegionGen.cs:195, 213)

**Severity: Low-Medium**

The `regionGenerationQueue.Find()` performs linear search for every chunk callback:

```csharp
var inProgressRegion = regionGenerationQueue.Find(region => region.RegionIdx == regionOfChunkIdx);
```

**Problem**: Called for every chunk loaded or peeked. With many regions queued, this becomes O(n*m) where n is queue size and m is chunks per region.

**Recommendation**: Use a `Dictionary<long, InProgressRegion>` alongside the list for O(1) lookups while maintaining ordering through the list.

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

**Problem**: When multiple adjacent regions load in quick succession, this can cause 4 mesh rebuilds per new region (the region itself + 3 neighbors). Each rebuild allocates new arrays and uploads to GPU.

**Recommendation**:
- Mark neighbors as "dirty" instead of immediate rebuild
- Batch dirty region rebuilds in a single frame
- Consider deferring rebuilds until the next frame to coalesce multiple updates

---

### 5. Memory Allocations Per Mesh Build (FarRegionRenderer.cs:96-161)

**Severity: Medium**

Every `BuildRegion` call allocates new arrays:

```csharp
mesh.xyz = new float[vertexCount * 3];        // ~192KB for 128x128 grid
mesh.Indices = new int[indicesCount];          // ~384KB for 128x128 grid
```

**Problem**: With cascading rebuilds and player movement, this can cause significant GC pressure.

**Recommendation**:
- Pool and reuse mesh data arrays
- Pre-allocate arrays based on configured grid size at startup

---

## Moderate Issues

### 6. SpiralWalker Distance Check (FarseerServer.cs:238)

**Severity: Low**

The spiral walker calculates Euclidean distance using `sqrt`:

```csharp
if (coord.Len() <= farViewDistanceInRegions)  // Len() calls GameMath.Sqrt()
```

**Recommendation**: Compare squared distances to avoid the sqrt operation:

```csharp
if (coord.X * coord.X + coord.Z * coord.Z <= farViewDistanceInRegions * farViewDistanceInRegions)
```

---

### 7. LINQ Usage in Hot Paths (Multiple files)

**Severity: Low-Medium**

Several hot paths use LINQ which creates iterator allocations:

- `FarRegionGen.cs:54`: `regionGenerationQueue.Any(r => r.RegionIdx == regionIdx)`
- `FarseerServer.cs:161`: `GetRegionsNoLongerInView(...).Where(player.RegionsLoaded.Contains).ToArray()`
- `FarseerServer.cs:172-176`: `modPlayers.Values.Where(...)`

**Recommendation**: Replace with explicit loops in frequently-called methods to avoid allocations.

---

### 8. Repeated Dictionary Lookups (FarseerServer.cs:138-151)

**Severity: Low**

The region priority combination logic does multiple dictionary operations:

```csharp
if (regionPrioritiesCombined.TryGetValue(pair.Key, out int existingPrio))
{
    if (pair.Value < existingPrio)
    {
        regionPrioritiesCombined[pair.Key] = pair.Value;  // Second lookup
    }
}
```

**Recommendation**: Use `CollectionsMarshal.GetValueRefOrAddDefault` or similar patterns to avoid double lookups.

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

## Recommendations Summary

| Priority | Issue | File | Status | Estimated Impact |
|----------|-------|------|--------|------------------|
| ~~High~~ | ~~Inefficient heightmap loop~~ | ~~FarRegionGen.cs:225-278~~ | ✅ **FIXED** | ~256x fewer iterations |
| High | Linear queue scans | BatchedPacketBuffer.cs | Open | O(n) -> O(1) lookups |
| Medium | Cascading mesh rebuilds | FarRegionRenderer.cs:180-200 | Open | Reduce GPU uploads |
| Medium | Mesh array allocations | FarRegionRenderer.cs:96-161 | Open | Reduce GC pressure |
| Medium | Linear queue Find() | FarRegionGen.cs:195,213 | Open | O(n) -> O(1) lookups |
| Low | SpiralWalker sqrt | SpiralWalker.cs:9-12 | Open | Avoid sqrt per coord |
| Low | LINQ in hot paths | Multiple | Open | Reduce allocations |

---

## Testing Recommendations

To validate these issues and measure improvements:

1. **Profiler Integration**: The existing frame profiler (`farseer-render` marker) can be extended to cover mesh building
2. **Memory Profiling**: Monitor GC allocations during player movement across region boundaries
3. **Server Metrics**: Track time spent in `PopulateRegionFromChunk` with varying grid sizes
4. **Stress Testing**: Test with maximum view distance (16384 blocks) and multiple players

---

*Review conducted: 2026-01-24*
