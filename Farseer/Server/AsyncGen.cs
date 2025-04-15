namespace Farseer;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

public class AsyncGen : IAsyncServerSystem
{
    public delegate void AsyncGenDoneDelegate(int chunkX, int chunkZ, IMapChunk mapChunk);

    FarseerModSystem modSystem;
    private ICoreServerAPI sapi;

    private ConcurrentQueue<long> regionQueue = new();

    public event AsyncGenDoneDelegate AsyncGenDone;

    public AsyncGen(FarseerModSystem modSystem, ICoreServerAPI sapi)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
    }

    public void Enqueue(long regionIdx)
    {
        if (!regionQueue.Any(r => r == regionIdx))
        {
            regionQueue.Enqueue(regionIdx);
        }
    }

    public int OffThreadInterval()
    {
        return 1000;
    }

    public void OnSeparateThreadTick()
    {
        if (regionQueue.TryDequeue(out long next))
        {
            //var supplyCHunks = sapi.Server.AddServerThread
            LetsGo(next);
        }
    }

    private void LetsGo(long regionIdx)
    {
        int chunksInRegionColumn = sapi.WorldManager.RegionSize / sapi.WorldManager.ChunkSize;

        var coord = sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
        var regionX = coord.X;
        var regionZ = coord.Z;
        var chunkGenParams = new TreeAttribute();

        var mapRegion = ServerMapRegion.CreateNew();
        var worldgenHandler = sapi.Event.GetRegisteredWorldGenHandlers("standard");
        for (int i = 0; i < worldgenHandler.OnMapRegionGen.Count; i++)
        {
            worldgenHandler.OnMapRegionGen[i](mapRegion, regionX, regionZ, chunkGenParams);
        }
        var chunkDataPool = new ChunkDataPool(sapi.WorldManager.ChunkSize, (ServerMain)sapi.World);

        for (int z = 0; z < chunksInRegionColumn; z++)
        {
            for (int x = 0; x < chunksInRegionColumn; x++)
            {
                var chunkX = regionX * chunksInRegionColumn + x;
                var chunkZ = regionZ * chunksInRegionColumn + z;
                var index2d = sapi.WorldManager.MapChunkIndex2D(chunkX, chunkZ);

                // Create the map chunk
                ServerMapChunk mapChunk = ServerMapChunk.CreateNew(mapRegion);
                for (int i = 0; i < worldgenHandler.OnMapChunkGen.Count; i++)
                {
                    worldgenHandler.OnMapChunkGen[i](mapChunk, chunkX, chunkZ);
                }

                // Create the column load request thing
                var chunkRequest = new ChunkColumnLoadRequest(index2d, chunkX, chunkZ, 0, (int)EnumWorldGenPass.Terrain, (IShutDownMonitor)sapi.World);
                var mapChunkProp = chunkRequest.GetType().GetField("MapChunk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                mapChunkProp.SetValue(chunkRequest, mapChunk);
                chunkRequest.CurrentIncompletePass = EnumWorldGenPass.Terrain;

                // Create the actual friggin column
                int chunkMapSizeY = sapi.WorldManager.MapSizeY / sapi.WorldManager.ChunkSize;
                var chunks = new ServerChunk[chunkMapSizeY];
                var chunksProp = chunkRequest.GetType().GetField("Chunks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                chunksProp.SetValue(chunkRequest, chunks); //put it in the req
                for (int i = 0; i < chunkMapSizeY; i++)
                {
                    ServerChunk serverChunk = ServerChunk.CreateNew(chunkDataPool);
                    serverChunk.serverMapChunk = mapChunk;
                    serverChunk.Unpack();
                    chunks[i] = serverChunk;
                }

                List<ChunkColumnGenerationDelegate> generationDelegates = worldgenHandler.OnChunkColumnGen[(int)EnumWorldGenPass.Terrain];
                for (int i = 0; i < generationDelegates.Count; i++)
                {
                    try
                    {
                        generationDelegates[i](chunkRequest);
                    }
                    catch (Exception ex)
                    {
                        modSystem.Mod.Logger.Error("An error was thrown in when generating chunk column X={0},Z={1}\nException {2}\n\n", chunkX, chunkZ, ex);
                        break;
                    }
                }

                AsyncGenDone?.Invoke(chunkX, chunkZ, mapChunk);
            }
        }
    }

    public void ThreadDispose()
    {
    }
}
