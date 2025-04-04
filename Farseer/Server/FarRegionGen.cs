using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Farseer;

public delegate void FarRegionGeneratedDelegate(long regionIdx, FarRegionHeightmap generatedHeightmap);

public class FarRegionGen
{
    private ModSystem modSystem;
    private ICoreServerAPI sapi;

    // public event FarRegionGeneratedDelegate FarRegionGenerated;

    public FarRegionGen(ModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
    }

    public FarRegionHeightmap GenerateRegion(long regionIdx)
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
