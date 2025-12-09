using System.Collections.Generic;
using Vintagestory.API.Common;
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
    }

    public void StartGeneratingRegion(long regionIdx)
    {
        lock(queueLock)
        {
            if (regionGenerationQueue.ContainsKey(regionIdx)) return;

            var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
            var chunkStartX = regionPos.X * chunksInRegionColumn;
            var chunkStartZ = regionPos.Z * chunksInRegionColumn;

            int gridSize = modSystem.Server.Config.HeightmapGridSize;
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
        int maxChunksPerTick = 32; // Limit to 32 chunk requests per tick

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
                        // Test if the chunk exists first. It's faster to load
                        // existing chunks than to peek. (Peek ignores saved data)
                        sapi.WorldManager.TestMapChunkExists(targetChunkX, targetChunkZ, (exists) =>
                        {
                            if (exists)
                            {
                                sapi.WorldManager.LoadChunkColumn(targetChunkX, targetChunkZ);
                            }
                            else
                            {
                                // It seems peek is about ~20-60% faster than
                                // full chunk generation and less taxing on the
                                // server (not to mention disk space)
                                sapi.WorldManager.PeekChunkColumn(targetChunkX, targetChunkZ, new ChunkPeekOptions()
                                {
                                    UntilPass = EnumWorldGenPass.Terrain,
                                    OnGenerated = OnChunkColumnPeeked,
                                });
                                peekWaiting.Add(targetChunkIdx);
                            }
                        });
                        chunksRequestedThisTick++;
                    }
                }
            }
        }
    }

    private void OnChunkColumnPeeked(Dictionary<Vec2i, IServerChunk[]> columnsByChunkCoordinate)
    {
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
