using Vintagestory.API.Server;
using Vintagestory.API.Common;
using System;
using System.Collections.Generic;

namespace Farseer;

public class FarseerServer : IDisposable
{
    public class FarseePlayer
    {
        public int DesiredRenderDistance { get; set; }
    }

    ModSystem modSystem;
    ICoreServerAPI sapi;

    FarDB db;
    Dictionary<IServerPlayer, FarseePlayer> players = new Dictionary<IServerPlayer, FarseePlayer>();

    public FarseerServer(ModSystem mod, ICoreServerAPI sapi)
    {
        this.modSystem = mod;
        this.sapi = sapi;
        this.db = new FarDB(mod.Mod.Logger);

        // this.map = new FarChunkMap();
        // sapi.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        // map.NewChunkLoaded += (coord, chunk) =>
        // {
        //     channel.BroadcastPacket(new FarChunkMessage { ChunkPosX = coord.X, ChunkPosZ = coord.Y, Heightmap = chunk.Heightmap });
        // };
        //
        sapi.Event.PlayerDisconnect += OnPlayerDisconnect;
        sapi.Event.RegisterGameTickListener(OnGameTick, 1000);

        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SetMessageHandler<FarEnableRequest>(EnableFarseeForPlayer);
    }

    private void OnGameTick(float time)
    {
        //modSystem.Mod.Logger.Chat(time.ToString());
    }

    private void EnableFarseeForPlayer(IServerPlayer fromPlayer, FarEnableRequest request)
    {
        if (players.TryGetValue(fromPlayer, out FarseePlayer player))
        {
            // Just update the desired render distance.
            // TODO: Send more if its larger i guess
            player.DesiredRenderDistance = request.DesiredRenderDistance;
        }
        else
        {
            players.Add(fromPlayer, new FarseePlayer() { DesiredRenderDistance = request.DesiredRenderDistance });
        }
        modSystem.Mod.Logger.Chat("enabled for player " + fromPlayer.PlayerName);


        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);

        var playerBlockPos = fromPlayer.Entity.Pos.AsBlockPos;
        var playerRegionIdx = sapi.WorldManager.MapRegionIndex2DByBlockPos(playerBlockPos.X, playerBlockPos.Z);

        var playerRegionCoord = sapi.WorldManager.MapRegionPosFromIndex2D(playerRegionIdx);

        for (var x = -request.DesiredRenderDistance; x <= request.DesiredRenderDistance; x++)
        {
            for (var z = -request.DesiredRenderDistance; z <= request.DesiredRenderDistance; z++)
            {
                var thisRegionX = playerRegionCoord.X + x;
                var thisRegionZ = playerRegionCoord.Z + z;
                var thisRegionIdx = sapi.WorldManager.MapRegionIndex2D(thisRegionX, thisRegionZ);

                var regionHeightmap = db.GetFarRegion(thisRegionIdx);
                var regionData = new FarRegionData
                {
                    RegionIndex = thisRegionIdx,
                    RegionX = thisRegionX,
                    RegionZ = thisRegionZ,
                    Heightmap = regionHeightmap,
                };

                channel.SendPacket(regionData, fromPlayer);
            }
        }
    }

    private void OnPlayerDisconnect(IServerPlayer byPlayer)
    {
        if (players.ContainsKey(byPlayer))
        {
            players.Remove(byPlayer);
        }
    }

    // private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    // {
    //     this.map.LoadFromWorld(chunkCoord, chunks[0]);
    // }
    //

    public void Dispose()
    {
        // sapi.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
    }
}
