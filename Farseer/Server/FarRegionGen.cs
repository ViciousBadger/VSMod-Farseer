using System.Collections.Generic;
using System.Linq;
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

        public InProgressRegion(long regionIdx, int gridSize)
        {
            RegionIdx = regionIdx;
            Heightmap = new FarRegionHeightmap
            {
                GridSize = gridSize,
                Points = new int[gridSize * gridSize],
            };
        }
    }

    public event FarRegionGeneratedDelegate FarRegionGenerated;

    private FarseerModSystem modSystem;
    private ICoreServerAPI sapi;

    private List<InProgressRegion> regionGenerationQueue = new();
    private HashSet<long> peekWaiting = new();

    private int chunksInRegionColumn;
    private int chunksInRegionArea;

    public FarRegionGen(FarseerModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
        sapi.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        sapi.Event.RegisterGameTickListener((_) => LoadNextFarChunksInQueue(), 8004);

        this.chunksInRegionColumn = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;
        this.chunksInRegionArea = this.chunksInRegionColumn * this.chunksInRegionColumn;
    }

    public void StartGeneratingRegion(long regionIdx)
    {
        if (regionGenerationQueue.Any(r => r.RegionIdx == regionIdx)) return;

        var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
        var chunkStartX = regionPos.X * chunksInRegionColumn;
        var chunkStartZ = regionPos.Z * chunksInRegionColumn;

        int gridSize = modSystem.Server.Config.HeightmapGridSize;
        var newInProgressRegion = new InProgressRegion(regionIdx, gridSize);

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
            regionGenerationQueue.Add(newInProgressRegion);
        }
    }

    public void CancelTasksNotIn(HashSet<long> regionsToKeep)
    {
        var n = regionGenerationQueue.RemoveAll(r => !regionsToKeep.Contains(r.RegionIdx) && r.FinishedChunks.Count == 0);
        if (n > 0 && !modSystem.Server.Config.DisableProgressLogging)
        {
            modSystem.Mod.Logger.Notification("Cancelling {0} far generation task(s) because no players are in range.", n);
        }
    }

    public void SortTasksByPriority(Dictionary<long, int> regionPriorities)
    {
        if (regionGenerationQueue.Count > 0)
        {
            regionGenerationQueue.Sort((a, b) =>
            {
                // Always prioritize half baked regions to avoid scattered chunk-loading
                var mostFinished = a.FinishedChunks.Count.CompareTo(b.FinishedChunks.Count);
                if (mostFinished != 0)
                {
                    return -mostFinished;
                }

                if (regionPriorities.TryGetValue(a.RegionIdx, out int aPrio))
                {
                    if (regionPriorities.TryGetValue(b.RegionIdx, out int bPrio))
                    {
                        return aPrio.CompareTo(bPrio);
                    }
                }
                // No sorting done if either region is missing a priority.
                return 0;
            });
        }
    }

    private void LoadNextFarChunksInQueue()
    {
        if (regionGenerationQueue.Count <= 0 || sapi.WorldManager.CurrentGeneratingChunkCount > modSystem.Server.Config.ChunkGenQueueThreshold) return;

        if (!modSystem.Server.Config.DisableProgressLogging)
        {
            modSystem.Mod.Logger.Notification("Building heightmaps for {0} faraway region(s)..", regionGenerationQueue.Count);
        }

        var nextRegionInQueue = regionGenerationQueue[0];

        var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(nextRegionInQueue.RegionIdx);
        var chunkStartX = regionPos.X * chunksInRegionColumn;
        var chunkStartZ = regionPos.Z * chunksInRegionColumn;

        for (int z = 0; z < chunksInRegionColumn; z++)
        {
            for (int x = 0; x < chunksInRegionColumn; x++)
            {
                int targetChunkX = chunkStartX + x;
                int targetChunkZ = chunkStartZ + z;
                var targetChunkIdx = sapi.WorldManager.MapChunkIndex2D(targetChunkX, targetChunkZ);

                if (!peekWaiting.Contains(targetChunkIdx) && !nextRegionInQueue.FinishedChunks.Contains(targetChunkIdx))
                {
                    if (modSystem.Server.Config.GenRealChunks)
                    {
                        sapi.WorldManager.LoadChunkColumn(targetChunkX, targetChunkZ);
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
                var inProgressRegion = regionGenerationQueue.Find(region => region.RegionIdx == regionOfChunkIdx);
                if (inProgressRegion != null)
                {
                    PopulateRegionFromChunk(inProgressRegion, pair.Key.X, pair.Key.Y, pair.Value[0].MapChunk);
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
        var inProgressRegion = regionGenerationQueue.Find(region => region.RegionIdx == regionOfChunkIdx);
        if (inProgressRegion != null)
        {
            PopulateRegionFromChunk(inProgressRegion, chunkCoord.X, chunkCoord.Y, chunks[0].MapChunk);
        }
    }

    private bool IsRegionFullyPopulated(InProgressRegion region)
    {
        return region.FinishedChunks.Count >= chunksInRegionArea;
    }

    private void PopulateRegionFromChunk(InProgressRegion region, int chunkX, int chunkZ, IMapChunk chunk)
    {
        var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(region.RegionIdx);
        var chunkStartX = regionPos.X * chunksInRegionColumn;
        var chunkStartZ = regionPos.Z * chunksInRegionColumn;

        int gridSize = region.Heightmap.GridSize;
        float cellSize = sapi.WorldManager.RegionSize / (float)gridSize;

        for (int z = 0; z < gridSize; z++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                int offsetBlockPosX = (int)(x * cellSize);
                int offsetBlockPosZ = (int)(z * cellSize);

                int targetChunkX = chunkStartX + offsetBlockPosX / sapi.WorldManager.ChunkSize;
                int targetChunkZ = chunkStartZ + offsetBlockPosZ / sapi.WorldManager.ChunkSize;

                if (targetChunkX == chunkX && targetChunkZ == chunkZ)
                {
                    int posInChunkX = offsetBlockPosX % sapi.WorldManager.ChunkSize;
                    int posInChunkZ = offsetBlockPosZ % sapi.WorldManager.ChunkSize;

                    int chunkHeightmapCoord = posInChunkZ * sapi.WorldManager.ChunkSize + posInChunkX;

                    var sampledHeight = chunk.WorldGenTerrainHeightMap[chunkHeightmapCoord];
                    var sampledHeightOrSea = GameMath.Max(sampledHeight, sapi.World.SeaLevel);
                    region.Heightmap.Points[z * gridSize + x] = sampledHeightOrSea;
                }
            }
        }

        region.FinishedChunks.Add(sapi.WorldManager.MapChunkIndex2D(chunkX, chunkZ));

        if (IsRegionFullyPopulated(region))
        {
            FarRegionGenerated?.Invoke(region.RegionIdx, region.Heightmap);
            regionGenerationQueue.Remove(region);

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
