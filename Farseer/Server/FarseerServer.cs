using Vintagestory.API.Server;
using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace Farseer;

public class FarseerServer : IDisposable
{
    public class FarseePlayer
    {
        public IServerPlayer ServerPlayer { get; set; }
        public FarseerServerPlayerConfig PlayerConfig { get; set; }
        public HashSet<long> RegionsInView { get; set; } = new();
        public HashSet<long> RegionsLoaded { get; set; } = new();
        public Vec3i LastPos { get; set; } = null;
    }

    FarseerModSystem modSystem;
    ICoreServerAPI sapi;
    FarseerServerConfig config;
    BatchedRegionDataBuffer regionSendBuffer;

    FarRegionProvider regionProvider;
    Dictionary<IServerPlayer, FarseePlayer> playersWithFarseer = new Dictionary<IServerPlayer, FarseePlayer>();
    HashSet<IServerPlayer> compressionCapable = new();

    // Reusable collections to avoid allocations
    private Dictionary<long, int> regionPrioritiesCombined = new();
    private List<long> regionsNewInView = new();
    private List<long> regionsToUnload = new();
    private List<IServerPlayer> relevantPlayers = new();

    public FarseerServerConfig Config => config;

    public FarseerServer(FarseerModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
        this.regionProvider = new FarRegionProvider(this.modSystem, sapi);
        regionProvider.RegionReady += LoadRegionForPlayersInView;
        this.regionSendBuffer = new(modSystem, sapi, 8);

        sapi.Event.PlayerDisconnect += OnPlayerDisconnect;
        sapi.Event.RegisterGameTickListener((_) => { if (AnyPlayerMovedRecently()) { UpdateRegionsInView(); } }, 7005, 2000);
        sapi.Event.RegisterGameTickListener((_) => PruneUnusedRegions(), 15002, 4000);
        sapi.Event.RegisterGameTickListener((_) => regionSendBuffer.SendNextBatch(), 302, 1000);

        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SetMessageHandler<FarseerEnable>(EnableForPlayer);
        channel.SetMessageHandler<FarseerDisable>(DisableForPlayer);

        try
        {
            config = sapi.LoadModConfig<FarseerServerConfig>("farseer-server.json");
            if (config == null)
            {
                config = new FarseerServerConfig();
            }
            sapi.StoreModConfig<FarseerServerConfig>(config, "farseer-server.json");
        }
        catch (Exception e)
        {
            //Couldn't load the mod config... Create a new one with default settings, but don't save it.
            this.modSystem.Mod.Logger.Error("Could not load config! Loading default settings instead. If you delete the config file, this error will go away magically, but your custom settings will also be lost.");
            this.modSystem.Mod.Logger.Error(e);
            config = new FarseerServerConfig();
        }

        regionProvider.ApplyAdaptivePeekConfig(config);

        // Optional: enable batch peeking via Harmony to reduce pause/resume overhead
        if (config.EnableBatchPeek)
        {
            BatchPeekPatch.Configure(config.MaxBatchPeekColumns);
            BatchPeekPatch.Apply();
        }

        // Validate and set world config value
        try
        {
            int maxViewDistance = Math.Max(512, Math.Min(16384, config.MaxClientViewDistance));
            sapi.World.Config.SetInt("maxFarViewDistance", maxViewDistance);
        }
        catch (Exception e)
        {
            this.modSystem.Mod.Logger.Error("Failed to set maxFarViewDistance in world config. Using default value.");
            this.modSystem.Mod.Logger.Error(e);
        }
    }

    private void EnableForPlayer(IServerPlayer fromPlayer, FarseerEnable request)
    {
        if (sapi.Server.IsDedicated)
        {
            request.PlayerConfig.FarViewDistance = GameMath.Min(request.PlayerConfig.FarViewDistance, config.MaxClientViewDistance);
        }
        else
        {
            modSystem.Mod.Logger.Chat("Running locally, no view distance limit enforced.");
        }

        if (playersWithFarseer.TryGetValue(fromPlayer, out FarseePlayer player))
        {
            // Happens when players change their client-side config.
            player.PlayerConfig = request.PlayerConfig;
        }
        else
        {
            playersWithFarseer.Add(fromPlayer, new FarseePlayer() { ServerPlayer = fromPlayer, PlayerConfig = request.PlayerConfig });
        }

        if (request.SupportsCompressedFarRegions)
        {
            compressionCapable.Add(fromPlayer);
        }
        else
        {
            compressionCapable.Remove(fromPlayer);
        }

        UpdateRegionsInView();
    }

    private void DisableForPlayer(IServerPlayer fromPlayer, FarseerDisable packet)
    {
        if (playersWithFarseer.ContainsKey(fromPlayer))
        {
            playersWithFarseer.Remove(fromPlayer);
            regionSendBuffer.CancelAllForTarget(fromPlayer);
        }
    }

    private bool AnyPlayerMovedRecently()
    {
        var anyPlayerMoved = false;
        foreach (var player in playersWithFarseer.Values)
        {
            // Validate player entity exists
            if (player.ServerPlayer?.Entity == null) continue;
            
            var oldPos = player.LastPos;
            var newPos = player.ServerPlayer.Entity.ServerPos.XYZInt;

            if (oldPos != null)
            {
                var dist = oldPos.DistanceTo(newPos);
                if (dist > 128f)
                {
                    anyPlayerMoved = true;
                    player.LastPos = newPos.Clone();
                }
            }
            else
            {
                anyPlayerMoved = true;
                player.LastPos = newPos.Clone();
            }
        }
        return anyPlayerMoved;
    }

    private void UpdateRegionsInView()
    {
        var profiler = sapi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter("farseer-update-regions");

        // Select highest priority for each region.
        regionPrioritiesCombined.Clear();

        foreach (var player in playersWithFarseer.Values)
        {
            var regionsInViewNow = GetRegionsInViewOfPlayer(player, out Dictionary<long, int> regionPrioritiesForPlayer, out Dictionary<long, float> regionDistances);
            var regionsInViewBefore = player.RegionsInView;
            player.RegionsInView = regionsInViewNow;

            foreach (var pair in regionPrioritiesForPlayer)
            {
                // Distance-based grid size selection per region
                float dist = regionDistances.TryGetValue(pair.Key, out float d) ? d : 0f;
                int gridSize = ComputeGridSizeForDistance(dist);
                regionProvider.SetDesiredGridSize(pair.Key, gridSize);

                if (regionPrioritiesCombined.TryGetValue(pair.Key, out int existingPrio))
                {
                    if (pair.Value < existingPrio)
                    {
                        // Override only if "higher" priority
                        regionPrioritiesCombined[pair.Key] = pair.Value;
                    }
                }
                else
                {
                    regionPrioritiesCombined.Add(pair.Key, pair.Value);
                }
            }

            GetRegionsNewInView(regionsInViewBefore, regionsInViewNow, regionsNewInView);
            for (int i = 0; i < regionsNewInView.Count; i++)
            {
                regionProvider.LoadRegion(regionsNewInView[i]);
            }

            GetRegionsToUnload(regionsInViewBefore, regionsInViewNow, player.RegionsLoaded, regionsToUnload);
            if (regionsToUnload.Count > 0)
            {
                UnloadRegionsForPlayer(player, regionsToUnload);
            }
        }
        regionProvider.Reprioritize(regionPrioritiesCombined);

        if (profile) profiler.Leave();
    }

    private void LoadRegionForPlayersInView(FarRegionData regionData)
    {
        var profiler = sapi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter("farseer-region-ready");

        relevantPlayers.Clear();
        
        foreach (var player in playersWithFarseer.Values)
        {
            // Validate player is still connected and has valid entity
            if (player.ServerPlayer?.Entity == null) continue;
            
            if (player.RegionsInView.Contains(regionData.RegionIndex) &&
                !player.RegionsLoaded.Contains(regionData.RegionIndex))
            {
                relevantPlayers.Add(player.ServerPlayer);
                player.RegionsLoaded.Add(regionData.RegionIndex);
            }
        }

        if (relevantPlayers.Count > 0)
        {
            regionSendBuffer.Insert(regionData, relevantPlayers.ToArray());
        }

        if (profile) profiler.Leave();
    }

    private void UnloadRegionsForPlayer(FarseePlayer player, List<long> regionIndices)
    {
        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SendPacket(new FarRegionUnload { RegionIndices = regionIndices.ToArray() }, player.ServerPlayer);
        
        for (int i = 0; i < regionIndices.Count; i++)
        {
            long idx = regionIndices[i];
            player.RegionsLoaded.Remove(idx);
            regionSendBuffer.CancelForTarget(idx, player.ServerPlayer);
        }
    }

    private void PruneUnusedRegions()
    {
        var regionsToKeep = new HashSet<long>();
        foreach (var playerData in playersWithFarseer.Values)
        {
            foreach (var regionIdx in playerData.RegionsInView)
            {
                regionsToKeep.Add(regionIdx);
            }
        }
        regionProvider.PruneRegionCache(regionsToKeep);
    }

    private int ComputeGridSizeForDistance(float regionDistance)
    {
        var cfg = modSystem.Server.Config;
        if (!cfg.EnableDistanceLod) return cfg.HeightmapGridSize;

        int baseGrid = cfg.HeightmapGridSize;
        int minGrid = cfg.MinHeightmapGridSize;

        if (regionDistance >= cfg.Lod2StartRegions)
        {
            return Math.Max(minGrid, baseGrid / 4);
        }
        if (regionDistance >= cfg.Lod1StartRegions)
        {
            return Math.Max(minGrid, baseGrid / 2);
        }
        return baseGrid;
    }

    private void GetRegionsNewInView(HashSet<long> regionsInViewBefore, HashSet<long> regionsInViewNow, List<long> output)
    {
        output.Clear();
        foreach (var region in regionsInViewNow)
        {
            if (!regionsInViewBefore.Contains(region))
            {
                output.Add(region);
            }
        }
    }

    private void GetRegionsToUnload(HashSet<long> regionsInViewBefore, HashSet<long> regionsInViewNow, HashSet<long> regionsLoaded, List<long> output)
    {
        output.Clear();
        foreach (var region in regionsInViewBefore)
        {
            if (!regionsInViewNow.Contains(region) && regionsLoaded.Contains(region))
            {
                output.Add(region);
            }
        }
    }

    private HashSet<long> GetRegionsInViewOfPlayer(FarseePlayer player, out Dictionary<long, int> priorities, out Dictionary<long, float> distances)
    {
        var playerBlockPos = player.ServerPlayer.Entity.Pos.AsBlockPos;
        var playerRegionIdx = sapi.WorldManager.MapRegionIndex2DByBlockPos(playerBlockPos.X, playerBlockPos.Z);
        var playerRegionCoord = sapi.WorldManager.MapRegionPosFromIndex2D(playerRegionIdx);

        int farViewDistanceInRegions = (player.PlayerConfig.FarViewDistance / sapi.WorldManager.RegionSize) + 1;

        var result = new HashSet<long>();
        priorities = new();
        distances = new();
        var thisPriority = 0;

        var walker = new SpiralWalker(new Coord2D(), farViewDistanceInRegions);
        foreach (var coord in walker)
        {
            if (coord.Len() <= farViewDistanceInRegions)
            {
                var thisRegionX = playerRegionCoord.X + coord.X;
                var thisRegionZ = playerRegionCoord.Z + coord.Z;

                var regionIdx = sapi.WorldManager.MapRegionIndex2D(thisRegionX, thisRegionZ);
                result.Add(regionIdx);
                priorities.Add(regionIdx, thisPriority++);
                distances[regionIdx] = coord.Len();
            }
        }
        return result;
    }

    private void OnPlayerDisconnect(IServerPlayer byPlayer)
    {
        if (playersWithFarseer.ContainsKey(byPlayer))
        {
            playersWithFarseer.Remove(byPlayer);
            compressionCapable.Remove(byPlayer);
        }
    }

    public void Dispose()
    {
        this.regionProvider?.Dispose();
    }

    public bool IsCompressionCapable(IServerPlayer player) => compressionCapable.Contains(player);
}
