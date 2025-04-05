using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

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

    private int chunksInRegionColumn;
    private int chunksInRegionArea;

    public FarRegionGen(FarseerModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
        sapi.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        sapi.Event.RegisterGameTickListener((_) => LoadNextFarChunksInQueue(), 2500);

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
            modSystem.Mod.Logger.Notification("{0} was pre-cooked for us :o", newInProgressRegion.RegionIdx);
        }
        else
        {
            regionGenerationQueue.Add(newInProgressRegion);
        }
    }

    public void CancelTasksNotIn(HashSet<long> regionsToKeep)
    {
        var n = regionGenerationQueue.RemoveAll(r => !regionsToKeep.Contains(r.RegionIdx) && r.FinishedChunks.Count == 0);
        //lol
        if (n == 1)
        {
            modSystem.Mod.Logger.Notification("Cancelling {0} far generation task", n);
        }
        else if (n > 1)
        {
            modSystem.Mod.Logger.Notification("Cancelling {0} far generation tasks", n);
        }
    }

    private void LoadNextFarChunksInQueue()
    {
        int chunkQueueLimit = (int)(MagicNum.RequestChunkColumnsQueueSize * modSystem.Server.Config.ChunkQueueThreshold); // Leave space for vanilla chunk gen..

        var regionsToPushToQueue = GameMath.Clamp((chunkQueueLimit - sapi.WorldManager.CurrentGeneratingChunkCount) / chunksInRegionArea, 0, regionGenerationQueue.Count);

        for (var i = 0; i < regionsToPushToQueue; i++)
        {
            var inProgressRegion = regionGenerationQueue[i];

            var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(inProgressRegion.RegionIdx);
            var chunkStartX = regionPos.X * chunksInRegionColumn;
            var chunkStartZ = regionPos.Z * chunksInRegionColumn;

            for (int z = 0; z < chunksInRegionColumn; z++)
            {
                for (int x = 0; x < chunksInRegionColumn; x++)
                {
                    int targetChunkX = chunkStartX + x;
                    int targetChunkZ = chunkStartZ + z;

                    if (!inProgressRegion.FinishedChunks.Contains(sapi.WorldManager.MapChunkIndex2D(targetChunkX, targetChunkZ)))
                    {
                        sapi.WorldManager.LoadChunkColumn(targetChunkX, targetChunkZ);
                    }
                }
            }
        }

        if (regionsToPushToQueue > 0)
        {
            modSystem.Mod.Logger.Notification("Generating {0} faraway regions.. ({1} regions total in queue)", regionsToPushToQueue, regionGenerationQueue.Count);
        }
    }

    private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        var regionOfChunkX = chunkCoord.X / chunksInRegionColumn;
        var regionOfChunkZ = chunkCoord.Y / chunksInRegionColumn;
        var regionOfChunkIdx = sapi.WorldManager.MapRegionIndex2D(regionOfChunkX, regionOfChunkZ);

        // We only care about the chunk data if it's part of one of the enqueued regions..
        var inProgressRegion = regionGenerationQueue.Find(region => region.RegionIdx == regionOfChunkIdx);
        if (inProgressRegion != null)
        {
            PopulateRegionFromChunk(inProgressRegion, chunkCoord.X, chunkCoord.Y, chunks[0].MapChunk);

            if (IsRegionFullyPopulated(inProgressRegion))
            {
                modSystem.Mod.Logger.Notification("{0} is cooked", inProgressRegion.RegionIdx);
                FarRegionGenerated?.Invoke(inProgressRegion.RegionIdx, inProgressRegion.Heightmap);
                regionGenerationQueue.Remove(inProgressRegion);

                if (regionGenerationQueue.Count == 0)
                {
                    modSystem.Mod.Logger.Notification("All done!");
                }
            }
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
