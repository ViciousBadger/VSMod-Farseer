using Vintagestory.API.Server;
using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farseer;

public class FarseerServer : IDisposable
{
    public class FarseePlayer
    {
        public int FarViewDistance { get; set; }
        public HashSet<long> RegionsInView { get; set; } = new();
    }

    ModSystem modSystem;
    ICoreServerAPI sapi;

    FarRegionProvider regionProvider;
    Dictionary<IServerPlayer, FarseePlayer> playersWithFarsee = new Dictionary<IServerPlayer, FarseePlayer>();

    Dictionary<long, FarRegionData> regionDataCache = new Dictionary<long, FarRegionData>();

    public FarseerServer(ModSystem mod, ICoreServerAPI sapi)
    {
        this.modSystem = mod;
        this.sapi = sapi;
        this.regionProvider = new FarRegionProvider(modSystem, sapi);

        sapi.Event.PlayerDisconnect += OnPlayerDisconnect;
        sapi.Event.RegisterGameTickListener((_) => UpdateLoadedRegions(), 1000);
        sapi.Event.RegisterGameTickListener((_) => PruneUnusedRegions(), 1000 * 10, 1000);

        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SetMessageHandler<FarEnableRequest>(EnableFarseeForPlayer);
    }

    private void UpdateLoadedRegions()
    {
        foreach (var player in playersWithFarsee)
        {
            var regionsInViewNow = GetRegionsInViewOfPlayer(player.Key, player.Value);
            var regionsInViewBefore = player.Value.RegionsInView;

            var newRegions = GetRegionsNewInView(regionsInViewBefore, regionsInViewNow);
            foreach (var newRegion in newRegions)
            {
                LoadRegionForPlayer(player.Key, newRegion);
            }

            var lostRegions = GetRegionsNoLongerInView(regionsInViewBefore, regionsInViewNow);
            foreach (var lostRegion in lostRegions)
            {
                UnloadRegionForPlayer(player.Key, lostRegion);
            }

            player.Value.RegionsInView = regionsInViewNow;
        }
    }

    private void LoadRegionForPlayer(IServerPlayer serverPlayer, long regionIdx)
    {
        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        if (regionDataCache.ContainsKey(regionIdx))
        {
            channel.SendPacket(regionDataCache[regionIdx], serverPlayer);
            modSystem.Mod.Logger.Notification("region {0} loaded for player {1} (was cached)", regionIdx, serverPlayer.PlayerName);
        }
        else
        {
            var newlyLoadedData = regionProvider.GetOrGenerateRegion(regionIdx);
            regionDataCache.Add(regionIdx, newlyLoadedData);
            channel.SendPacket(newlyLoadedData, serverPlayer);
            modSystem.Mod.Logger.Notification("region {0} loaded for player {1} (was accessed)", regionIdx, serverPlayer.PlayerName);
        }
    }

    private void UnloadRegionForPlayer(IServerPlayer serverPlayer, long regionIdx)
    {
        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SendPacket(new FarRegionUnload { RegionIndex = regionIdx }, serverPlayer);

        modSystem.Mod.Logger.Notification("region {0} unloaded for player {1}", regionIdx, serverPlayer.PlayerName);
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

    private HashSet<long> GetRegionsInViewOfPlayer(IServerPlayer serverPlayer, FarseePlayer playerModData)
    {
        var playerBlockPos = serverPlayer.Entity.Pos.AsBlockPos;
        var playerRegionIdx = sapi.WorldManager.MapRegionIndex2DByBlockPos(playerBlockPos.X, playerBlockPos.Z);
        var playerRegionCoord = sapi.WorldManager.MapRegionPosFromIndex2D(playerRegionIdx);
        // modSystem.Mod.Logger.Chat("playerRegionIdx: {0}, playerRegionCoord: {1}", playerRegionIdx, playerRegionCoord);

        int farViewDistanceInRegions = playerModData.FarViewDistance / sapi.WorldManager.RegionSize;

        var result = new HashSet<long>();
        for (var x = -farViewDistanceInRegions - 1; x <= farViewDistanceInRegions + 1; x++)
        {
            for (var z = -farViewDistanceInRegions - 1; z <= farViewDistanceInRegions + 1; z++)
            {
                var thisRegionX = playerRegionCoord.X + x;
                var thisRegionZ = playerRegionCoord.Z + z;
                result.Add(sapi.WorldManager.MapRegionIndex2D(thisRegionX, thisRegionZ));
            }
        }
        return result;
    }

    private void EnableFarseeForPlayer(IServerPlayer fromPlayer, FarEnableRequest request)
    {
        if (playersWithFarsee.TryGetValue(fromPlayer, out FarseePlayer player))
        {
            player.FarViewDistance = 2048;
        }
        else
        {
            playersWithFarsee.Add(fromPlayer, new FarseePlayer() { FarViewDistance = request.FarViewDistance = 2048 });
        }
        modSystem.Mod.Logger.Chat("enabled for player " + fromPlayer.PlayerName);
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
