using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System;

namespace Seefar;


public class SeefarClient : IDisposable
{
    ModSystem modSystem;
    ICoreClientAPI api;

    TestRenderer renderer;

    FarChunkMap map;

    public SeefarClient(ModSystem mod, ICoreClientAPI api)
    {
        this.modSystem = mod;
        this.api = api;

        this.renderer = new TestRenderer(api);
        api.Event.RegisterRenderer(this.renderer, EnumRenderStage.Opaque);

        //api.Event.MapRegionLoaded
        var channel = api.Network.GetChannel("seefar");
        channel.SetMessageHandler<FarChunkMessage>(OnRecieveFarChunkMessage);
    }

    void OnRecieveFarChunkMessage(FarChunkMessage msg)
    {
        api.Logger.Notification("recieved:" + msg.Heightmap);
    }

    public void Dispose()
    {
        if (this.renderer != null && this.api != null)
        {
            this.api.Event.UnregisterRenderer(this.renderer, EnumRenderStage.Opaque);
        }
    }
}
