using System;
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

    private ModSystem modSystem;
    private ICoreServerAPI sapi;

    private List<InProgressRegion> regionGenerationQueue = new();

    public event FarRegionGeneratedDelegate FarRegionGenerated;

    public FarRegionGen(ModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
        sapi.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        sapi.Event.RegisterGameTickListener((_) => KickstartChunkGen(), 500);
    }

    public void StartGeneratingRegion(long regionIdx)
    {
        if (regionGenerationQueue.Any(r => r.RegionIdx == regionIdx)) return;

        var chunksInRegion = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;
        var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
        var chunkStartX = regionPos.X * chunksInRegion;
        var chunkStartZ = regionPos.Z * chunksInRegion;

        int gridSize = 64;
        var newInProgressRegion = new InProgressRegion(regionIdx, gridSize);

        // First, populate already loaded chunks
        for (int z = 0; z < chunksInRegion; z++)
        {
            for (int x = 0; x < chunksInRegion; x++)
            {
                int targetChunkX = chunkStartX + x;
                int targetChunkZ = chunkStartZ + z;

                if (sapi.WorldManager.GetMapChunk(targetChunkX, targetChunkZ) is IMapChunk mapChunk)
                {
                    // Must be at least though the vegetation pass to have a heightmap
                    if (mapChunk.CurrentPass >= EnumWorldGenPass.Vegetation)
                    {
                        PopulateRegionFromChunk(newInProgressRegion, targetChunkX, targetChunkZ, mapChunk);
                    }
                }
            }
        }

        if (IsRegionFullyPopulated(newInProgressRegion))
        {
            //No need to enqueue if it's already done.
            modSystem.Mod.Logger.Notification("region {0} was already done because all its chunks are loaded!", regionIdx);
            FarRegionGenerated?.Invoke(newInProgressRegion.RegionIdx, newInProgressRegion.Heightmap);
        }
        else
        {
            modSystem.Mod.Logger.Notification("region {0} is unfinished and has been queued!", regionIdx);
            regionGenerationQueue.Add(newInProgressRegion);
        }
    }

    private void KickstartChunkGen()
    {
        var chunkQueueLimit = 800; // Leave space for vanilla chunk gen..
        var chunksInRegionSquared = (sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize) * (sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize);

        var regionsToPushToQueue = GameMath.Clamp((chunkQueueLimit - sapi.WorldManager.CurrentGeneratingChunkCount) / chunksInRegionSquared, 0, regionGenerationQueue.Count);

        for (var i = 0; i < regionsToPushToQueue; i++)
        {
            var inProgressRegion = regionGenerationQueue[i];

            var chunksInRegion = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;
            var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(inProgressRegion.RegionIdx);
            var chunkStartX = regionPos.X * chunksInRegion;
            var chunkStartZ = regionPos.Z * chunksInRegion;

            for (int z = 0; z < chunksInRegion; z++)
            {
                for (int x = 0; x < chunksInRegion; x++)
                {
                    int targetChunkX = chunkStartX + x;
                    int targetChunkZ = chunkStartZ + z;

                    sapi.WorldManager.LoadChunkColumn(targetChunkX, targetChunkZ);
                }
            }
            modSystem.Mod.Logger.Notification("chunk gen kickstart!!! for region {0}", inProgressRegion.RegionIdx);
        }
    }

    private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        var chunksInRegion = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;
        var regionOfChunkX = chunkCoord.X / chunksInRegion;
        var regionOfChunkZ = chunkCoord.Y / chunksInRegion;
        var regionOfChunkIdx = sapi.WorldManager.MapRegionIndex2D(regionOfChunkX, regionOfChunkZ);

        // TODO: if there is no in-progress region here, we should probably
        // create one and keep it around. Or maybe not.. who knows (the queue
        // can get very large as it is, when players move around..)

        var inProgressRegion = regionGenerationQueue.Find(region => region.RegionIdx == regionOfChunkIdx);
        if (inProgressRegion != null)
        {
            PopulateRegionFromChunk(inProgressRegion, chunkCoord.X, chunkCoord.Y, chunks[0].MapChunk);

            if (IsRegionFullyPopulated(inProgressRegion))
            {
                modSystem.Mod.Logger.Notification("region {0} is done because all its chunks were loaded after enqueuing!!", inProgressRegion.RegionIdx);

                FarRegionGenerated?.Invoke(inProgressRegion.RegionIdx, inProgressRegion.Heightmap);
                regionGenerationQueue.Remove(inProgressRegion);
            }
        }
    }


    private bool IsRegionFullyPopulated(InProgressRegion region)
    {
        var chunksInRegion = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;
        return region.FinishedChunks.Count >= chunksInRegion * chunksInRegion;
    }

    private void PopulateRegionFromChunk(InProgressRegion region, int chunkX, int chunkZ, IMapChunk chunk)
    {
        var chunksInRegion = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;
        var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(region.RegionIdx);
        var chunkStartX = regionPos.X * chunksInRegion;
        var chunkStartZ = regionPos.Z * chunksInRegion;

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
                    // modSystem.Mod.Logger.Notification("height {0}", sampledHeight);
                }
            }
        }

        region.FinishedChunks.Add(sapi.WorldManager.MapChunkIndex2D(chunkX, chunkZ));

        //modSystem.Mod.Logger.Notification("populated region from chunk X {0} Z {1}", chunkX, chunkZ);
    }

    public FarRegionHeightmap GenerateRegionTest(long regionIdx)
    {
        //sapi.WorldManager.LoadChunkColumn(0, 0);
        //modSystem.Mod.Logger.Notification("ok, generating 0,0");

        var chunksInRegion = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;
        var regionPos = sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
        var chunkStartX = regionPos.X * chunksInRegion;
        var chunkStartZ = regionPos.Z * chunksInRegion;

        int gridSize = 32;
        float cellSize = sapi.WorldManager.RegionSize / (float)gridSize;
        int heightmapSize = gridSize * gridSize;
        var heightmapPoints = new int[heightmapSize];

        // modSystem.Mod.Logger.Notification("cell size {0}, start X {1}, start Z {2}", cellSize, chunkStartX, chunkStartZ);

        for (int z = 0; z < gridSize; z++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                int sampledHeight = 0;

                int offsetBlockPosX = (int)(x * cellSize);
                int offsetBlockPosZ = (int)(z * cellSize);

                int targetChunkX = chunkStartX + offsetBlockPosX / sapi.WorldManager.ChunkSize;
                int targetChunkZ = chunkStartZ + offsetBlockPosZ / sapi.WorldManager.ChunkSize;

                int posInChunkX = offsetBlockPosX % sapi.WorldManager.ChunkSize;
                int posInChunkZ = offsetBlockPosZ % sapi.WorldManager.ChunkSize;

                // modSystem.Mod.Logger.Notification("gridX {0} gridZ {1} will target chunk X{2} Z{3}", x, z, targetChunkX, targetChunkZ);
                // modSystem.Mod.Logger.Notification("(offsetX {0}, offsetZ {1}) (posinX {2} posinZ {3})", offsetBlockPosX, offsetBlockPosZ, posInChunkX, posInChunkZ);

                //var blockPos = sapi.WorldManager.MapRegionIndex2DByBlockPos


                heightmapPoints[z * gridSize + x] = sampledHeight;
            }
        }

        return new FarRegionHeightmap
        {
            GridSize = gridSize,
            Points = heightmapPoints
        };
    }

    public FarRegionHeightmap GenerateDummyData(long regionIdx)
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
        return heightmapObj;
    }
}
