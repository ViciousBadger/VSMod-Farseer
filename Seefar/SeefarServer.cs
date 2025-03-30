namespace Seefar;
using Vintagestory.API.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;

public class SeefarServer : IDisposable
{
    ModSystem modSystem;
    ICoreServerAPI api;

    FarChunkMap map;

    public SeefarServer(ModSystem mod, ICoreServerAPI api)
    {
        this.modSystem = mod;
        this.api = api;

        this.map = new FarChunkMap();

        api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        //
        var channel = api.Network.GetChannel("seefar");

        map.NewChunkLoaded += (coord, chunk) =>
        {
            api.Logger.Notification("sending chunk at:" + coord);
            channel.BroadcastPacket(new FarChunkMessage { ChunkPosX = coord.X, ChunkPosZ = coord.Y, Heightmap = chunk.Heightmap });
        };
    }

    private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        for (int i = 0; i < chunks.Length; i++)
        {
            int centerX = 16;
            int centerZ = 16;
            int centerIdx = centerZ * 32 + centerX;

            IWorldChunk chunk = chunks[i];
            Vec2i coord = chunkCoord.Copy();
            coord.X += i;

            this.map.Load(coord, chunk);
        }
        // modSystem.Mod.Logger.Notification("Chunk col loaded, coord " + chunkCoord + ", chunk count " + chunks.Length);
    }

    public void Dispose()
    {
        api.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
    }
}
