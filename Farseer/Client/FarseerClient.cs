using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System;
using Vintagestory.API.Config;

namespace Farseer;

public class FarseerClient : IDisposable
{
    FarseerModSystem modSystem;
    ICoreClientAPI capi;
    FarseerClientConfig config;

    FarRegionRenderer renderer;

    FarseerConfigDialog configDialog;

    public FarseerClientConfig Config => config;

    private long configSaveDelayListener = -1;

    public FarseerClient(FarseerModSystem modSystem, ICoreClientAPI capi)
    {
        this.modSystem = modSystem;
        this.capi = capi;

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
            modSystem.Mod.Logger.Error("Could not load config! Loading default settings instead.");
            modSystem.Mod.Logger.Error(e);
            config = new FarseerClientConfig();
        }

        this.renderer = new FarRegionRenderer(modSystem, capi);
        this.configDialog = new FarseerConfigDialog(modSystem, capi);

        capi.Input.RegisterHotKey(
                "toggleFarseerConfig",
                Lang.Get("farseer:toggle-config"),
                GlKeys.F,
                HotkeyType.GUIOrOtherControls,
                false, // Alt
                true, // Control
                true // Shift
        );
        capi.Input.SetHotKeyHandler("toggleFarseerConfig", TestHandler);

        capi.Event.LevelFinalize += Init;
    }

    public void SaveConfigChanges()
    {
        capi.StoreModConfig<FarseerClientConfig>(config, "farseer-client.json");


        // Re-initialize with any potential changes.
        Init();
    }

    private bool TestHandler(KeyCombination t1)
    {
        modSystem.Mod.Logger.Notification("hey");
        configDialog.Toggle();
        return true;
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
        if (channel != null)
        {
            channel.SendPacket(new FarEnableRequest
            {
                ClientConfig = config,
            });
        }
        renderer.Init();
    }

    public void Dispose()
    {
        this.renderer?.Dispose();
    }
}
