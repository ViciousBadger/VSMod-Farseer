using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System;
using Vintagestory.Common;
using Vintagestory.Client.NoObf;

namespace Farseer;


public class FarseerClient : IDisposable
{
    ModSystem modSystem;
    ICoreClientAPI capi;

    FarChunkRenderer renderer;

    FarChunkMap map;

    public FarseerClient(ModSystem mod, ICoreClientAPI api)
    {
        this.modSystem = mod;
        this.capi = api;
        this.map = new FarChunkMap();

        // this.renderer = new FarChunkRenderer(api, map);
        // channel.SetMessageHandler<FarChunkMessage>(OnRecieveFarChunkMessage);

        capi.Event.LevelFinalize += Init;
    }


    public void Init()
    {
        var channel = capi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SendPacket(new FarEnableRequest
        {
            DesiredRenderDistance = 8,
        });
    }

    // public void RequestRegionsAroundPlayer()
    // {
    //
    //     int radius = 8;
    //     var playerPos = capi.World.Player.Entity.Pos;
    //
    //     channel.SendPacket(new FarRegionRequest
    //     {
    //         PlayerUID = capi.World.Player.PlayerUID,
    //         RegionIndex = MapRegionIndex2D(0, 0),
    //     });
    // }

    // public long MapRegionIndex2D(int regionX, int regionY)
    // {
    //     return ((ClientMain)capi.World).WorldMap.MapRegionIndex2D(regionX, regionY);
    // }

    void OnRecieveFarChunkMessage(FarChunkMessage msg)
    {
        map.LoadFromMessage(msg);
    }

    public void Dispose()
    {
        this.renderer?.Dispose();
    }
}
