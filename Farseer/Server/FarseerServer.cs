using Vintagestory.API.Server;
using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace Farseer;

public class FarseerServer : IDisposable
{
    public class FarseePlayer
    {
        public IServerPlayer ServerPlayer { get; set; }
        public FarseerClientConfig ClientConfig { get; set; }
        public HashSet<long> RegionsInView { get; set; } = new();
        public HashSet<long> RegionsLoaded { get; set; } = new();
    }

    FarseerModSystem modSystem;
    ICoreServerAPI sapi;
    FarseerServerConfig config;

    FarRegionProvider regionProvider;
    Dictionary<IServerPlayer, FarseePlayer> playersWithFarsee = new Dictionary<IServerPlayer, FarseePlayer>();

    public FarseerServerConfig Config => config;

    public FarseerServer(FarseerModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
        this.regionProvider = new FarRegionProvider(this.modSystem, sapi);
        regionProvider.RegionReady += TryLoadRegionForPlayersInView;

        sapi.Event.PlayerDisconnect += OnPlayerDisconnect;
        sapi.Event.RegisterGameTickListener((_) => UpdateRegionsInView(), 1000);
        sapi.Event.RegisterGameTickListener((_) => PruneUnusedRegions(), 1000 * 10, 1000);

        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SetMessageHandler<FarEnableRequest>(EnableFarseeForPlayer);

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
            this.modSystem.Mod.Logger.Error("Could not load config! Loading default settings instead.");
            this.modSystem.Mod.Logger.Error(e);
            config = new FarseerServerConfig();
        }
    }

    private void EnableFarseeForPlayer(IServerPlayer fromPlayer, FarEnableRequest request)
    {
        if (playersWithFarsee.TryGetValue(fromPlayer, out FarseePlayer player))
        {
            // Assume that the player changed their config. Should clear and re-send regions.
            player.ClientConfig = request.ClientConfig;
            player.RegionsInView.Clear();
            player.RegionsLoaded.Clear();
        }
        else
        {
            if (sapi.Server.IsDedicated)
            {
                request.ClientConfig.FarViewDistance = GameMath.Min(request.ClientConfig.FarViewDistance, config.MaxClientViewDistance);
            }
            else
            {
                modSystem.Mod.Logger.Chat("Running locally, no view distance limit enforced.");
            }
            playersWithFarsee.Add(fromPlayer, new FarseePlayer() { ServerPlayer = fromPlayer, ClientConfig = request.ClientConfig });
        }
        modSystem.Mod.Logger.Chat("Enabled for player " + fromPlayer.PlayerName);
    }

    private void UpdateRegionsInView()
    {
        foreach (var player in playersWithFarsee.Values)
        {
            var regionsInViewNow = GetRegionsInViewOfPlayer(player);
            var regionsInViewBefore = player.RegionsInView;
            player.RegionsInView = regionsInViewNow;

            foreach (var newRegion in GetRegionsNewInView(regionsInViewBefore, regionsInViewNow))
            {
                // Start loading this region - will be sent to the player once
                // it's ready, as long as it's still in view.
                regionProvider.LoadRegion(newRegion);

                //LoadRegionForPlayer(player.Key, newRegion);
            }

            foreach (var lostRegion in GetRegionsNoLongerInView(regionsInViewBefore, regionsInViewNow))
            {
                if (player.RegionsLoaded.Contains(lostRegion))
                {
                    UnloadRegionForPlayer(player, lostRegion);
                }
            }
        }
    }

    private void TryLoadRegionForPlayersInView(FarRegionData regionData)
    {
        foreach (var player in playersWithFarsee.Values)
        {
            if (player.RegionsInView.Contains(regionData.RegionIndex) && !player.RegionsLoaded.Contains(regionData.RegionIndex))
            {
                LoadRegionForPlayer(player, regionData);
            }
        }
    }

    private void LoadRegionForPlayer(FarseePlayer player, FarRegionData regionData)
    {
        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SendPacket(regionData, player.ServerPlayer);
        player.RegionsLoaded.Add(regionData.RegionIndex);
    }

    private void UnloadRegionForPlayer(FarseePlayer player, long regionIdx)
    {
        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SendPacket(new FarRegionUnload { RegionIndex = regionIdx }, player.ServerPlayer);
        player.RegionsLoaded.Remove(regionIdx);
    }

    private void PruneUnusedRegions()
    {
        var regionsToKeep = new HashSet<long>();
        foreach (var playerData in playersWithFarsee.Values)
        {
            foreach (var regionIdx in playerData.RegionsInView)
            {
                regionsToKeep.Add(regionIdx);
            }
        }
        regionProvider.PruneRegionCache(regionsToKeep);
    }

    private HashSet<long> GetRegionsNewInView(HashSet<long> regionsInViewBefore, HashSet<long> regionsInViewNow)
    {
        return regionsInViewNow.Where(region => !regionsInViewBefore.Contains(region)).ToHashSet();
    }

    private HashSet<long> GetRegionsNoLongerInView(HashSet<long> regionsInViewBefore, HashSet<long> regionsInViewNow)
    {
        return regionsInViewBefore.Where(region => !regionsInViewNow.Contains(region)).ToHashSet();
    }

    private HashSet<long> GetRegionsInViewOfPlayer(FarseePlayer player)
    {
        var playerBlockPos = player.ServerPlayer.Entity.Pos.AsBlockPos;
        var playerRegionIdx = sapi.WorldManager.MapRegionIndex2DByBlockPos(playerBlockPos.X, playerBlockPos.Z);
        var playerRegionCoord = sapi.WorldManager.MapRegionPosFromIndex2D(playerRegionIdx);
        // modSystem.Mod.Logger.Chat("playerRegionIdx: {0}, playerRegionCoord: {1}", playerRegionIdx, playerRegionCoord);

        int farViewDistanceInRegions = (player.ClientConfig.FarViewDistance / sapi.WorldManager.RegionSize);

        var result = new HashSet<long>();

        var walker = new SpiralWalker(new Coord2D(), farViewDistanceInRegions);
        foreach (var coord in walker)
        {
            if (coord.Len() <= farViewDistanceInRegions)
            {
                var thisRegionX = playerRegionCoord.X + coord.X;
                var thisRegionZ = playerRegionCoord.Z + coord.Z;
                result.Add(sapi.WorldManager.MapRegionIndex2D(thisRegionX, thisRegionZ));
            }
        }
        return result;
    }

    private void OnPlayerDisconnect(IServerPlayer byPlayer)
    {
        if (playersWithFarsee.ContainsKey(byPlayer))
        {
            playersWithFarsee.Remove(byPlayer);
        }
    }

    public void Dispose()
    {
        this.regionProvider?.Dispose();
    }
}
