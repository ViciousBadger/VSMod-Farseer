using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Farseer;

public delegate void RegionReadyDelegate(FarRegionData regionData);

public class FarRegionProvider : IDisposable
{
    public event RegionReadyDelegate RegionReady;

    private FarseerModSystem modSystem;
    private ICoreServerAPI sapi;

    private readonly Dictionary<long, FarRegionData> inMemoryRegionCache = new();
    private readonly Dictionary<long, int> regionGridSizeOverrides = new();
    private FarRegionDB db;
    private FarRegionGen generator;

    // Pre-calculated world bounds
    private int minRegionX;
    private int minRegionZ;
    private int maxRegionX;
    private int maxRegionZ;

    public FarRegionProvider(FarseerModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;

        // Pre-calculate world bounds (VS allows negative coordinates down to -30 blocks)
        int regionSize = sapi.WorldManager.RegionSize;
        this.minRegionX = -30 / regionSize - 1;  // -1 for safety margin
        this.minRegionZ = -30 / regionSize - 1;
        this.maxRegionX = (sapi.WorldManager.MapSizeX + 30) / regionSize + 1;
        this.maxRegionZ = (sapi.WorldManager.MapSizeZ + 30) / regionSize + 1;

        this.db = new FarRegionDB(modSystem.Mod.Logger);
        string errorMessage = null;
        string path = GetDbFilePath();
        db.OpenOrCreate(path, ref errorMessage, true, true, false);
        if (errorMessage != null)
        {
            // IDEA: maybe just delete and re-create the entire database here.
            throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", path));
        }

        this.generator = new FarRegionGen(modSystem, sapi);
        generator.FarRegionGenerated += OnFarRegionGenerated;
    }

    public void ApplyAdaptivePeekConfig(FarseerServerConfig config)
    {
        generator.SetAdaptivePeekBounds(config.AdaptivePeekMin, config.AdaptivePeekMax);
    }

    public void LoadRegion(long regionIdx)
    {
        var profiler = sapi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter("farseer-region-load");

        int desiredGridSize = GetDesiredGridSizeOrDefault(regionIdx);

        // Check if this region is within the world bounds first.
        Vec3i regionCoords = sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
        if (regionCoords.X < minRegionX || regionCoords.Z < minRegionZ || 
            regionCoords.X > maxRegionX || regionCoords.Z > maxRegionZ)
        {
            if (profile) profiler.Leave();
            return;
        }

        if (inMemoryRegionCache.TryGetValue(regionIdx, out FarRegionData regionDataFromCache))
        {
            if (desiredGridSize > regionDataFromCache.Heightmap.GridSize)
            {
                generator.StartGeneratingRegion(regionIdx, desiredGridSize);
            }
            RegionReady?.Invoke(EnsureGridSize(regionDataFromCache, desiredGridSize));
        }
        else
        {
            if (db.GetRegionHeightmap(regionIdx) is FarRegionHeightmap heightmap)
            {
                var baseData = CreateDataObject(regionIdx, heightmap);
                inMemoryRegionCache.Add(regionIdx, baseData);

                // If we need higher detail than stored, kick off a regen in the background
                if (desiredGridSize > heightmap.GridSize)
                {
                    generator.StartGeneratingRegion(regionIdx, desiredGridSize);
                }

                RegionReady?.Invoke(EnsureGridSize(baseData, desiredGridSize));
            }
            else
            {
                generator.StartGeneratingRegion(regionIdx, desiredGridSize);
            }
        }

        if (profile) profiler.Leave();
    }

    public void SetDesiredGridSize(long regionIdx, int gridSize)
    {
        // Keep the highest resolution requested (max grid size)
        if (regionGridSizeOverrides.TryGetValue(regionIdx, out int existing))
        {
            if (gridSize > existing)
            {
                regionGridSizeOverrides[regionIdx] = gridSize;
            }
        }
        else
        {
            regionGridSizeOverrides[regionIdx] = gridSize;
        }
    }

    private int GetDesiredGridSizeOrDefault(long regionIdx)
    {
        if (regionGridSizeOverrides.TryGetValue(regionIdx, out int size))
        {
            return size;
        }
        return modSystem.Server.Config.HeightmapGridSize;
    }

    private FarRegionHeightmap EnsureGridSize(FarRegionHeightmap source, int desiredGridSize)
    {
        if (desiredGridSize >= source.GridSize)
        {
            return source;
        }

        return DownsampleHeightmap(source, desiredGridSize);
    }

    private FarRegionData EnsureGridSize(FarRegionData data, int desiredGridSize)
    {
        if (desiredGridSize >= data.Heightmap.GridSize)
        {
            return data;
        }

        var downsampled = DownsampleHeightmap(data.Heightmap, desiredGridSize);
        return new FarRegionData
        {
            RegionIndex = data.RegionIndex,
            RegionX = data.RegionX,
            RegionZ = data.RegionZ,
            RegionSize = data.RegionSize,
            RegionMapSize = data.RegionMapSize,
            Heightmap = downsampled,
        };
    }

    private FarRegionHeightmap DownsampleHeightmap(FarRegionHeightmap source, int targetGrid)
    {
        int srcGrid = source.GridSize;
        targetGrid = Math.Max(8, Math.Min(targetGrid, srcGrid));

        var result = new FarRegionHeightmap
        {
            GridSize = targetGrid,
            Points = new int[targetGrid * targetGrid],
            Colors = source.Colors == null ? null : new int[targetGrid * targetGrid],
        };

        float scale = (srcGrid - 1f) / (targetGrid - 1f);
        for (int z = 0; z < targetGrid; z++)
        {
            int srcZ = Math.Min(srcGrid - 1, (int)(z * scale));
            for (int x = 0; x < targetGrid; x++)
            {
                int srcX = Math.Min(srcGrid - 1, (int)(x * scale));
                int srcIdx = srcZ * srcGrid + srcX;
                int dstIdx = z * targetGrid + x;
                result.Points[dstIdx] = source.Points[srcIdx];
                if (result.Colors != null && source.Colors != null && source.Colors.Length > srcIdx)
                {
                    result.Colors[dstIdx] = source.Colors[srcIdx];
                }
            }
        }

        return result;
    }

    private void OnFarRegionGenerated(long regionIdx, FarRegionHeightmap generatedHeightmap)
    {
        var profiler = sapi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter("farseer-region-generated");

        db.InsertRegionHeightmap(regionIdx, generatedHeightmap);
        var newRegionData = CreateDataObject(regionIdx, generatedHeightmap);
        inMemoryRegionCache.Add(regionIdx, newRegionData);
        RegionReady?.Invoke(newRegionData);

        if (profile) profiler.Leave();
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

        // Remove regions not in view
        for (int i = 0; i < toRemove.Count; i++)
        {
            inMemoryRegionCache.Remove(toRemove[i]);
        }

        generator.CancelTasksNotIn(regionsToKeep);

        // Drop grid overrides for regions no longer in view
        var toRemoveOverrides = new List<long>();
        foreach (var key in regionGridSizeOverrides.Keys)
        {
            if (!regionsToKeep.Contains(key))
            {
                toRemoveOverrides.Add(key);
            }
        }
        for (int i = 0; i < toRemoveOverrides.Count; i++)
        {
            regionGridSizeOverrides.Remove(toRemoveOverrides[i]);
        }
    }

    public void Reprioritize(Dictionary<long, int> regionPriorities)
    {
        generator.SortTasksByPriority(regionPriorities);
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
            RegionMapSize = sapi.WorldManager.MapSizeX / sapi.WorldManager.RegionSize,
            Heightmap = heightmap,
        };
    }

    public void Dispose()
    {
        this.db?.Dispose();
    }
}
