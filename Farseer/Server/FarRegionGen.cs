using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Farseer;

public delegate void FarRegionGeneratedDelegate(long regionIdx, FarRegionHeightmap generatedHeightmap);

public class FarRegionGen
{
    class InProgressRegion
    {
        public long RegionIdx { get; }
        public FarRegionHeightmap Heightmap { get; }
        public HashSet<long> FinishedChunks { get; } = new();
        public int Priority { get; set; }

        public InProgressRegion(long regionIdx, int gridSize, bool storeColors)
        {
            RegionIdx = regionIdx;
            Heightmap = new FarRegionHeightmap
            {
                GridSize = gridSize,
                Points = new int[gridSize * gridSize],
                Colors = null, // Disabled for performance
            };
        }
    }

    public event FarRegionGeneratedDelegate FarRegionGenerated;

    private FarseerModSystem modSystem;
    private ICoreServerAPI sapi;

    private Dictionary<long, InProgressRegion> regionGenerationQueue = new();
    private HashSet<long> peekWaiting = new();
    private readonly object queueLock = new object();

    private int chunksInRegionColumn;
    private int chunksInRegionArea;
    private int chunkSize;
    private int regionSize;
    private int seaLevel;
    private int adaptivePeekMin;
    private int adaptivePeekMax;

    public FarRegionGen(FarseerModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
        sapi.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        sapi.Event.RegisterGameTickListener((_) => LoadNextFarChunksInQueue(), 8004);

        this.chunksInRegionColumn = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;
        this.chunksInRegionArea = this.chunksInRegionColumn * this.chunksInRegionColumn;
        this.chunkSize = sapi.WorldManager.ChunkSize;
        this.regionSize = sapi.WorldManager.RegionSize;
        this.seaLevel = sapi.World.SeaLevel;
        this.adaptivePeekMin = 8;
        this.adaptivePeekMax = 64;
    }

    public void StartGeneratingRegion(long regionIdx, int gridSize)
    {
        lock(queueLock)
        {
            if (regionGenerationQueue.ContainsKey(regionIdx)) return;

            var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
            var chunkStartX = regionPos.X * chunksInRegionColumn;
            var chunkStartZ = regionPos.Z * chunksInRegionColumn;

            bool storeBiomes = modSystem.Server.Config.StoreBiomeData;
            var newInProgressRegion = new InProgressRegion(regionIdx, gridSize, storeBiomes);

            // First, populate already loaded chunks
            for (int z = 0; z < chunksInRegionColumn; z++)
            {
                for (int x = 0; x < chunksInRegionColumn; x++)
                {
                    int targetChunkX = chunkStartX + x;
                    int targetChunkZ = chunkStartZ + z;

                    if (sapi.WorldManager.GetMapChunk(targetChunkX, targetChunkZ) is IMapChunk mapChunk)
                    {
                        PopulateRegionFromChunk(newInProgressRegion, targetChunkX, targetChunkZ, mapChunk);
                    }
                }
            }

            if (IsRegionFullyPopulated(newInProgressRegion))
            {
                //No need to enqueue if all if the region chunks were already loaded!
                FarRegionGenerated?.Invoke(newInProgressRegion.RegionIdx, newInProgressRegion.Heightmap);
            }
            else
            {
                regionGenerationQueue.Add(regionIdx, newInProgressRegion);
            }
        }
    }

    public void CancelTasksNotIn(HashSet<long> regionsToKeep)
    {
        lock(queueLock)
        {
            int n = 0;
            var toRemove = new List<long>();
            
            foreach (var pair in regionGenerationQueue)
            {
                if (!regionsToKeep.Contains(pair.Key) && pair.Value.FinishedChunks.Count == 0)
                {
                    toRemove.Add(pair.Key);
                    n++;
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                regionGenerationQueue.Remove(toRemove[i]);
            }

            if (n > 0 && !modSystem.Server.Config.DisableProgressLogging)
            {
                modSystem.Mod.Logger.Notification("Cancelling {0} far generation task(s) because no players are in range.", n);
            }
        }
    }

    public void SortTasksByPriority(Dictionary<long, int> regionPriorities)
    {
        lock(queueLock)
        {
            // Priority is stored but sorting happens on-demand in LoadNextFarChunksInQueue
            if (regionGenerationQueue.Count > 0)
            {
                foreach (var pair in regionGenerationQueue)
                {
                    if (regionPriorities.TryGetValue(pair.Key, out int priority))
                    {
                        pair.Value.Priority = priority;
                    }
                }
            }
        }
    }

    private void LoadNextFarChunksInQueue()
    {
        var profiler = sapi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter("farseer-far-loadqueue");

        InProgressRegion nextRegionInQueue = null;
        
        lock(queueLock)
        {
            if (regionGenerationQueue.Count <= 0 || sapi.WorldManager.CurrentGeneratingChunkCount > modSystem.Server.Config.ChunkGenQueueThreshold) return;

            if (!modSystem.Server.Config.DisableProgressLogging)
            {
                modSystem.Mod.Logger.Notification("Building heightmaps for {0} faraway region(s)..", regionGenerationQueue.Count);
            }

            // Get highest priority region (sort on-demand)
            int bestFinishedCount = -1;
            int bestPriority = int.MaxValue;

            foreach (var pair in regionGenerationQueue.Values)
            {
                int finishedCount = pair.FinishedChunks.Count;
                int priority = pair.Priority;

                // Prioritize half-baked regions first, then by priority
                if (finishedCount > bestFinishedCount || 
                    (finishedCount == bestFinishedCount && priority < bestPriority))
                {
                    nextRegionInQueue = pair;
                    bestFinishedCount = finishedCount;
                    bestPriority = priority;
                }
            }

            if (nextRegionInQueue == null) return;
        }

        var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(nextRegionInQueue.RegionIdx);
        var chunkStartX = regionPos.X * chunksInRegionColumn;
        var chunkStartZ = regionPos.Z * chunksInRegionColumn;

        // Throttle: Only request a limited number of chunks per tick to avoid overwhelming the system
        int chunksRequestedThisTick = 0;
        int maxChunksPerTick = ComputeAdaptivePeekBudget();
        var coordsToPeek = new List<Vec2i>(chunksInRegionArea);

        for (int z = 0; z < chunksInRegionColumn; z++)
        {
            for (int x = 0; x < chunksInRegionColumn; x++)
            {
                if (chunksRequestedThisTick >= maxChunksPerTick) return; // Stop if we hit the limit

                int targetChunkX = chunkStartX + x;
                int targetChunkZ = chunkStartZ + z;
                var targetChunkIdx = sapi.WorldManager.MapChunkIndex2D(targetChunkX, targetChunkZ);

                if (!peekWaiting.Contains(targetChunkIdx) && !nextRegionInQueue.FinishedChunks.Contains(targetChunkIdx))
                {
                    if (modSystem.Server.Config.GenRealChunks)
                    {
                        sapi.WorldManager.LoadChunkColumn(targetChunkX, targetChunkZ);
                        chunksRequestedThisTick++;
                    }
                    else
                    {
                        coordsToPeek.Add(new Vec2i(targetChunkX, targetChunkZ));
                        peekWaiting.Add(targetChunkIdx);
                        chunksRequestedThisTick++;
                    }
                }
            }
        }

        if (coordsToPeek.Count > 0)
        {
            if (profile) profiler.Mark("farseer-batch-peek-enqueue");

            // Try to batch through the chunk thread; fall back to vanilla peek if the patch is unavailable
            var enqueued = BatchPeekPatch.Enqueue(coordsToPeek, EnumWorldGenPass.Terrain, new TreeAttribute(), OnChunkColumnPeeked);
            if (!enqueued)
            {
                for (int i = 0; i < coordsToPeek.Count; i++)
                {
                    var coord = coordsToPeek[i];
                    sapi.WorldManager.PeekChunkColumn(coord.X, coord.Y, new ChunkPeekOptions()
                    {
                        UntilPass = EnumWorldGenPass.Terrain,
                        OnGenerated = OnChunkColumnPeeked,
                    });
                }
            }
        }

        if (profile) profiler.Leave();
    }

    private int ComputeAdaptivePeekBudget()
    {
        // Base between min and max from config
        int min = adaptivePeekMin;
        int max = adaptivePeekMax;

        // If chunk gen queue is heavy, back off
        int generating = sapi.WorldManager.CurrentGeneratingChunkCount;
        if (generating > modSystem.Server.Config.ChunkGenQueueThreshold)
        {
            return min;
        }

        // If idle, use max
        if (generating < modSystem.Server.Config.ChunkGenQueueThreshold / 4)
        {
            return max;
        }

        // Linear interpolate between min and max based on load
        float load = (float)generating / modSystem.Server.Config.ChunkGenQueueThreshold;
        load = GameMath.Clamp(load, 0f, 1f);
        return (int)GameMath.Lerp(min, max, 1f - load);
    }

    public void SetAdaptivePeekBounds(int min, int max)
    {
        adaptivePeekMin = GameMath.Clamp(min, 4, 256);
        adaptivePeekMax = GameMath.Clamp(max, adaptivePeekMin, 512);
    }

    private void OnChunkColumnPeeked(Dictionary<Vec2i, IServerChunk[]> columnsByChunkCoordinate)
    {
        var profiler = sapi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter("farseer-far-peeked");

        foreach (var pair in columnsByChunkCoordinate)
        {
            var chunkIdx = sapi.WorldManager.MapChunkIndex2D(pair.Key.X, pair.Key.Y);
            peekWaiting.Remove(chunkIdx);
            if (pair.Value.Length > 0)
            {
                var regionOfChunkX = pair.Key.X / chunksInRegionColumn;
                var regionOfChunkZ = pair.Key.Y / chunksInRegionColumn;
                var regionOfChunkIdx = sapi.WorldManager.MapRegionIndex2D(regionOfChunkX, regionOfChunkZ);

                // We only care about the chunk data if it's part of one of the enqueued regions..
                lock(queueLock)
                {
                    if (regionGenerationQueue.TryGetValue(regionOfChunkIdx, out InProgressRegion inProgressRegion))
                    {
                        PopulateRegionFromChunk(inProgressRegion, pair.Key.X, pair.Key.Y, pair.Value[0].MapChunk);
                    }
                }
            }
        }

        if (profile) profiler.Leave();
    }

    private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        if (chunks.Length <= 0) return;

        var regionOfChunkX = chunkCoord.X / chunksInRegionColumn;
        var regionOfChunkZ = chunkCoord.Y / chunksInRegionColumn;
        var regionOfChunkIdx = sapi.WorldManager.MapRegionIndex2D(regionOfChunkX, regionOfChunkZ);

        // We only care about the chunk data if it's part of one of the enqueued regions..
        lock(queueLock)
        {
            if (regionGenerationQueue.TryGetValue(regionOfChunkIdx, out InProgressRegion inProgressRegion))
            {
                PopulateRegionFromChunk(inProgressRegion, chunkCoord.X, chunkCoord.Y, chunks[0].MapChunk);
            }
        }
    }

    private bool IsRegionFullyPopulated(InProgressRegion region)
    {
        return region.FinishedChunks.Count >= chunksInRegionArea;
    }

    private void PopulateRegionFromChunk(InProgressRegion region, int chunkX, int chunkZ, IMapChunk chunk)
    {
        var profiler = sapi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter("farseer-far-populate");

        if (chunk?.WorldGenTerrainHeightMap == null) return;

        var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(region.RegionIdx);
        var chunkStartX = regionPos.X * chunksInRegionColumn;
        var chunkStartZ = regionPos.Z * chunksInRegionColumn;

        int gridSize = region.Heightmap.GridSize;
        float cellSize = regionSize / (float)gridSize;
        int[] heightmapPoints = region.Heightmap.Points;
        int heightmapLength = chunk.WorldGenTerrainHeightMap.Length;

        for (int z = 0; z < gridSize; z++)
        {
            int offsetBlockPosZ = (int)(z * cellSize);
            int targetChunkZ = chunkStartZ + offsetBlockPosZ / chunkSize;
            
            if (targetChunkZ != chunkZ) continue;
            
            int posInChunkZ = offsetBlockPosZ % chunkSize;
            if (posInChunkZ < 0) posInChunkZ += chunkSize;  // Handle negative modulo
            int baseChunkCoord = posInChunkZ * chunkSize;

            for (int x = 0; x < gridSize; x++)
            {
                int offsetBlockPosX = (int)(x * cellSize);
                int targetChunkX = chunkStartX + offsetBlockPosX / chunkSize;

                if (targetChunkX == chunkX)
                {
                    int posInChunkX = offsetBlockPosX % chunkSize;
                    if (posInChunkX < 0) posInChunkX += chunkSize;  // Handle negative modulo
                    int chunkHeightmapCoord = baseChunkCoord + posInChunkX;

                    // Bounds check to prevent crashes
                    if (chunkHeightmapCoord >= 0 && chunkHeightmapCoord < heightmapLength)
                    {
                        var sampledHeight = chunk.WorldGenTerrainHeightMap[chunkHeightmapCoord];
                        heightmapPoints[z * gridSize + x] = sampledHeight > seaLevel ? sampledHeight : seaLevel;
                    }
                }
            }
        }

        region.FinishedChunks.Add(sapi.WorldManager.MapChunkIndex2D(chunkX, chunkZ));

        if (IsRegionFullyPopulated(region))
        {
            FarRegionGenerated?.Invoke(region.RegionIdx, region.Heightmap);
            
            lock(queueLock)
            {
                regionGenerationQueue.Remove(region.RegionIdx);
            }

            // Try to keep up a good pace
            LoadNextFarChunksInQueue();

            if (regionGenerationQueue.Count == 0 && !modSystem.Server.Config.DisableProgressLogging)
            {
                modSystem.Mod.Logger.Notification("All done!");
            }
        }

        if (profile) profiler.Leave();
    }

    public void GenerateDummyData(long regionIdx)
    {
        int gridSize = 32;
        int heightmapSize = gridSize * gridSize;
        var heightmapPoints = new int[heightmapSize];
        var heightmapObj = new FarRegionHeightmap
        {
            GridSize = gridSize,
            Points = heightmapPoints
        };

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                heightmapPoints[z * gridSize + x] = 130 + sapi.World.Rand.Next() % 64;
            }
        }
        FarRegionGenerated?.Invoke(regionIdx, heightmapObj);
    }
}
