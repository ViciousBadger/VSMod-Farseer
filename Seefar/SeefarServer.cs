namespace Seefar;
using Vintagestory.API.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;

public class SeefarServer : IDisposable
{
    ModSystem modSystem;
    ICoreServerAPI sapi;

    FarChunkMap map;

    FarDB farDB;

    public SeefarServer(ModSystem mod, ICoreServerAPI sapi)
    {
        this.modSystem = mod;
        this.sapi = sapi;

        this.map = new FarChunkMap();

        sapi.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        //
        var channel = sapi.Network.GetChannel("seefar");

        map.NewChunkLoaded += (coord, chunk) =>
        {
            channel.BroadcastPacket(new FarChunkMessage { ChunkPosX = coord.X, ChunkPosZ = coord.Y, Heightmap = chunk.Heightmap });
        };

        var asdf = sapi.Event.GetRegisteredWorldGenHandlers("standard");
        sapi.Logger.Notification(asdf.ToString());
    }

    private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        // sapi.Logger.Notification("chunks length " + chunks.Length);
        this.map.LoadFromWorld(chunkCoord, chunks[0]);
        // for (int i = 0; i < chunks.Length; i++)
        // {
        //     this.map.LoadFromWorld(new Vec2i(chunkCoord.X, chunkCoord.Y + i), chunks[i]);
        // }
    }

    public void Dispose()
    {
        sapi.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
    }
}
