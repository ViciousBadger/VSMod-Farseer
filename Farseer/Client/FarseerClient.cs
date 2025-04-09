using Vintagestory.API.Client;
using System;
using Vintagestory.API.Config;

namespace Farseer;

public class FarseerClient : IDisposable
{
    FarseerModSystem modSystem;
    ICoreClientAPI capi;
    FarseerClientConfig config;
    FarseerClientConfig configOnLastLoad;

    FarRegionRenderer renderer;

    FarseerConfigDialog configDialog;

    public FarseerClientConfig Config => config;

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

        configOnLastLoad = config.Clone();

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
        capi.Input.SetHotKeyHandler("toggleFarseerConfig", ToggleConfigDialog);

        capi.Event.LevelFinalize += Init;
    }

    public void SaveConfigChanges()
    {
        capi.StoreModConfig<FarseerClientConfig>(config, "farseer-client.json");

        if (config.ShouldShareWithServer(configOnLastLoad))
        {
            var channel = capi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
            if (channel != null)
            {
                if (config.Enabled)
                {
                    channel.SendPacket(new FarseerEnable
                    {
                        PlayerConfig = config.ToServerPlayerConfig(),
                    });
                }
                else
                {
                    channel.SendPacket(new FarseerDisable());
                }
            }
        }

        if (config.FarViewDistance != configOnLastLoad.FarViewDistance)
        {
            // re-init renderer so that zfar is updated
            renderer.Init();
        }

        if (configOnLastLoad.Enabled && !config.Enabled)
        {
            renderer.ClearLoadedRegions();
        }

        configOnLastLoad = config.Clone();
        modSystem.Mod.Logger.Notification("Saved client config changes.");
    }

    private bool ToggleConfigDialog(KeyCombination _)
    {
        configDialog.Toggle();
        return true;
    }

    private void OnRecieveFarRegionData(FarRegionData data)
    {
        if (config.Enabled)
        {
            renderer.BuildRegion(data);
        }
    }

    private void OnRecieveFarRegionUnload(FarRegionUnload packet)
    {
        if (config.Enabled)
        {
            foreach (var idx in packet.RegionIndices)
            {
                renderer.UnloadRegion(idx);
            }
        }
    }

    public void Init()
    {
        var channel = capi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        if (channel != null)
        {
            channel.SendPacket(new FarseerEnable
            {
                PlayerConfig = config.ToServerPlayerConfig(),
            });
        }
        renderer.Init();
    }

    public void Dispose()
    {
        this.renderer?.Dispose();
    }
}
