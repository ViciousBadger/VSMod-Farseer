using System;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Farseer;

public class FarRegionAccess : IDisposable
{
    private ModSystem modSystem;
    private ICoreServerAPI sapi;
    private FarDB db;
    private FarGen generator;

    public FarRegionAccess(ModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;

        this.db = new FarDB(modSystem.Mod.Logger);
        string errorMessage = null;
        string path = GetDbFilePath();
        db.OpenOrCreate(path, ref errorMessage, true, true, false);
        if (errorMessage != null)
        {
            throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", path));
        }

        this.generator = new FarGen(modSystem, sapi);

        this.generator.GenerateRegion(0);
    }

    private string GetDbFilePath()
    {
        string path = Path.Combine(GamePaths.DataPath, "Farseer");
        GamePaths.EnsurePathExists(path);
        return Path.Combine(path, sapi.World.SavegameIdentifier + ".db");
    }

    public FarRegionData GetOrGenerateRegion(long regionIdx)
    {
        var storedRegion = db.GetRegionHeightmap(regionIdx);
        if (storedRegion != null)
        {
            return CreateDataObject(regionIdx, storedRegion);
        }
        else
        {
            var newRegion = generator.GenerateRegion(regionIdx);
            db.InsertRegionHeightmap(regionIdx, newRegion);
            return CreateDataObject(regionIdx, newRegion);
        }
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

    public void Dispose()
    {
        this.db?.Dispose();
    }
}
