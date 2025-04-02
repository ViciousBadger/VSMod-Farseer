using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System;
using Vintagestory.Common;
using Vintagestory.Client.NoObf;

namespace Seefar;


public class SeefarClient : IDisposable
{
    ModSystem modSystem;
    ICoreClientAPI capi;

    FarChunkRenderer renderer;

    FarChunkMap map;

    public SeefarClient(ModSystem mod, ICoreClientAPI api)
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
        var channel = capi.Network.GetChannel("seefar");
        channel.SendPacket(new EnableSeefarRequest
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
