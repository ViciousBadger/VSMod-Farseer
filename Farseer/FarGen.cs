
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Farseer;

public class FarGen
{
    private ModSystem modSystem;
    private ICoreServerAPI sapi;

    public FarGen(ModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
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
