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

    FarRegionRenderer renderer;

    FarChunkMap map;

    int farRenderDistance = 3072;

    public FarseerClient(ModSystem mod, ICoreClientAPI api)
    {
        this.modSystem = mod;
        this.capi = api;
        this.map = new FarChunkMap();

        this.renderer = new FarRegionRenderer(api, farRenderDistance);
        // this.renderer = new FarChunkRenderer(api, map);
        // channel.SetMessageHandler<FarChunkMessage>(OnRecieveFarChunkMessage);

        capi.Event.LevelFinalize += Init;

        var channel = capi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SetMessageHandler<FarRegionData>(OnRecieveFarRegionData);
    }

    private void OnRecieveFarRegionData(FarRegionData data)
    {
        modSystem.Mod.Logger.Chat("New far data. Idx {0}, X {1}, Z {2}, Size {3}, Resolution {4}, Length {5}", data.RegionIndex, data.RegionX, data.RegionZ, data.RegionSize, data.Heightmap.GridSize, data.Heightmap.Points.Length);

        renderer.BuildRegion(data);
    }

    public void Init()
    {
        var channel = capi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SendPacket(new FarEnableRequest
        {
            FarViewDistance = farRenderDistance,
        });
    }

    void OnRecieveFarChunkMessage(FarChunkMessage msg)
    {
        map.LoadFromMessage(msg);
    }

    public void Dispose()
    {
        this.renderer?.Dispose();
    }
}
