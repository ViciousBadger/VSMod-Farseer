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
        public IServerPlayer ServerPlayer { get; set; }
        public int FarViewDistance { get; set; }
        public HashSet<long> RegionsInView { get; set; } = new();
        public HashSet<long> RegionsLoaded { get; set; } = new();
    }

    ModSystem modSystem;
    ICoreServerAPI sapi;

    FarRegionProvider regionProvider;
    Dictionary<IServerPlayer, FarseePlayer> playersWithFarsee = new Dictionary<IServerPlayer, FarseePlayer>();

    public FarseerServer(ModSystem mod, ICoreServerAPI sapi)
    {
        this.modSystem = mod;
        this.sapi = sapi;
        this.regionProvider = new FarRegionProvider(modSystem, sapi);
        regionProvider.RegionReady += TryLoadRegionForPlayersInView;

        sapi.Event.PlayerDisconnect += OnPlayerDisconnect;
        sapi.Event.RegisterGameTickListener((_) => UpdateRegionsInView(), 1000);
        sapi.Event.RegisterGameTickListener((_) => PruneUnusedRegions(), 1000 * 10, 1000);

        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SetMessageHandler<FarEnableRequest>(EnableFarseeForPlayer);
    }

    private void EnableFarseeForPlayer(IServerPlayer fromPlayer, FarEnableRequest request)
    {
        if (playersWithFarsee.TryGetValue(fromPlayer, out FarseePlayer player))
        {
            // Just update view distance
            player.FarViewDistance = request.FarViewDistance;
        }
        else
        {
            playersWithFarsee.Add(fromPlayer, new FarseePlayer() { ServerPlayer = fromPlayer, FarViewDistance = request.FarViewDistance });
        }
        modSystem.Mod.Logger.Chat("enabled for player " + fromPlayer.PlayerName);
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
            modSystem.Mod.Logger.Notification(player.ServerPlayer.PlayerName);
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

        int farViewDistanceInRegions = (player.FarViewDistance / sapi.WorldManager.RegionSize);

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

        // for (var x = -farViewDistanceInRegions - 1; x <= farViewDistanceInRegions + 1; x++)
        // {
        //     for (var z = -farViewDistanceInRegions - 1; z <= farViewDistanceInRegions + 1; z++)
        //     {
        //         var thisRegionX = playerRegionCoord.X + x;
        //         var thisRegionZ = playerRegionCoord.Z + z;
        //         result.Add(sapi.WorldManager.MapRegionIndex2D(thisRegionX, thisRegionZ));
        //     }
        // }
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
