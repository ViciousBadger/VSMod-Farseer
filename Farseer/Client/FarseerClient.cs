using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System;

namespace Farseer;

public class FarseerClient : IDisposable
{
    FarseerModSystem modSystem;
    ICoreClientAPI capi;
    FarseerClientConfig config;

    FarRegionRenderer renderer;

    public FarseerClientConfig Config => config;

    public FarseerClient(FarseerModSystem modSystem, ICoreClientAPI api)
    {
        this.modSystem = modSystem;
        this.capi = api;

        capi.Event.LevelFinalize += Init;

        var channel = capi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SetMessageHandler<FarRegionData>(OnRecieveFarRegionData);
        channel.SetMessageHandler<FarRegionUnload>(OnRecieveFarRegionUnload);

        try
        {
            config = capi.LoadModConfig<FarseerClientConfig>("farseer-client.json");
            if (config == null)
            {
                config = new FarseerClientConfig();
            }
            capi.StoreModConfig<FarseerClientConfig>(config, "farseer-client.json");
        }
        catch (Exception e)
        {
            this.modSystem.Mod.Logger.Error("Could not load config! Loading default settings instead.");
            this.modSystem.Mod.Logger.Error(e);
            config = new FarseerClientConfig();
        }

        this.renderer = new FarRegionRenderer(this.modSystem, api);
    }

    private void OnRecieveFarRegionData(FarRegionData data)
    {
        renderer.BuildRegion(data);
    }

    private void OnRecieveFarRegionUnload(FarRegionUnload packet)
    {
        renderer.UnloadRegion(packet.RegionIndex);
    }


    public void Init()
    {
        var channel = capi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        channel.SendPacket(new FarEnableRequest
        {
            ClientConfig = config,
        });
    }

    public void Dispose()
    {
        this.renderer?.Dispose();
    }
}
