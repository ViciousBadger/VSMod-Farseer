using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Farseer;

public class FarRegionAccess
{
    private ModSystem modSystem;
    private ICoreServerAPI sapi;
    private FarDB db;

    public FarRegionAccess(ModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;

        this.db = new FarDB(modSystem.Mod.Logger);
    }

    public FarRegionData GetDummyData(long regionIdx)
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
        return CreateDataObject(regionIdx, heightmapObj);
    }

    private FarRegionData CreateDataObject(long regionIdx, FarRegionHeightmap heightmap)
    {
        var regionCoord = sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
        return new FarRegionData
        {
            RegionIndex = regionIdx,
            RegionX = regionCoord.X,
            RegionZ = regionCoord.Z,
            RegionSize = sapi.WorldManager.RegionSize,
            Heightmap = heightmap,
        };
    }
}
