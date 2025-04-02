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

    Dictionary<IServerPlayer, FarseePlayer> players = new Dictionary<IServerPlayer, FarseePlayer>();

    // FarChunkMap map;

    public FarseerServer(ModSystem mod, ICoreServerAPI sapi)
    {
        this.modSystem = mod;
        this.sapi = sapi;


        // this.map = new FarChunkMap();
        // sapi.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        // map.NewChunkLoaded += (coord, chunk) =>
        // {
        //     channel.BroadcastPacket(new FarChunkMessage { ChunkPosX = coord.X, ChunkPosZ = coord.Y, Heightmap = chunk.Heightmap });
        // };
        //
        sapi.Event.PlayerDisconnect += OnPlayerDisconnect;

        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SetMessageHandler<FarEnableRequest>(EnableFarseeForPlayer);
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
