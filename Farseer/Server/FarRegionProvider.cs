using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Farseer;

public delegate void RegionReadyDelegate(FarRegionData regionData);

public class FarRegionProvider : IDisposable
{
    public event RegionReadyDelegate RegionReady;

    private ModSystem modSystem;
    private ICoreServerAPI sapi;

    private readonly Dictionary<long, FarRegionData> inMemoryRegionCache = new();
    private FarRegionDB db;
    private FarRegionGen generator;

    public FarRegionProvider(ModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;

        this.db = new FarRegionDB(modSystem.Mod.Logger);
        string errorMessage = null;
        string path = GetDbFilePath();
        db.OpenOrCreate(path, ref errorMessage, true, true, false);
        if (errorMessage != null)
        {
            // TODO: maybe just delete and re-create the entire database here.
            throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", path));
        }

        this.generator = new FarRegionGen(modSystem, sapi);
        generator.FarRegionGenerated += OnFarRegionGenerated;
    }

    public void LoadRegion(long regionIdx)
    {
        if (inMemoryRegionCache.TryGetValue(regionIdx, out FarRegionData regionDataFromCache))
        {
            RegionReady?.Invoke(regionDataFromCache);
        }
        else
        {
            if (db.GetRegionHeightmap(regionIdx) is FarRegionHeightmap heightmap)
            {
                var regionDataFromDB = CreateDataObject(regionIdx, heightmap);
                inMemoryRegionCache.Add(regionIdx, regionDataFromDB);
                RegionReady?.Invoke(regionDataFromDB);
            }
            else
            {
                generator.StartGeneratingRegion(regionIdx);

                // var newRegionHeightmap = generator.GenerateRegion(regionIdx);
                //
                // db.InsertRegionHeightmap(regionIdx, newRegionHeightmap);
                // var newRegionData = CreateDataObject(regionIdx, newRegionHeightmap);
                // inMemoryRegionCache.Add(regionIdx, newRegionData);
                // RegionReady?.Invoke(newRegionData);
            }
        }
    }

    private void OnFarRegionGenerated(long regionIdx, FarRegionHeightmap generatedHeightmap)
    {
        db.InsertRegionHeightmap(regionIdx, generatedHeightmap);
        var newRegionData = CreateDataObject(regionIdx, generatedHeightmap);
        inMemoryRegionCache.Add(regionIdx, newRegionData);
        RegionReady?.Invoke(newRegionData);
    }


    public void PruneRegionCache(HashSet<long> regionsToKeep)
    {
        var toRemove = new List<long>();
        foreach (var regionIdx in inMemoryRegionCache.Keys)
        {
            if (!regionsToKeep.Contains(regionIdx))
            {
                toRemove.Add(regionIdx);
            }
        }

        foreach (var regionIdx in toRemove)
        {
            inMemoryRegionCache.Remove(regionIdx);
            modSystem.Mod.Logger.Notification("region {0} dropped as no players are in range", regionIdx);
        }
    }

    private string GetDbFilePath()
    {
        string path = Path.Combine(GamePaths.DataPath, "Farseer");
        GamePaths.EnsurePathExists(path);
        return Path.Combine(path, sapi.World.SavegameIdentifier + ".db");
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
