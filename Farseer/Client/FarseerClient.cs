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
        channel.SetMessageHandler<FarRegionData>(OnReceiveFarRegionData);
        channel.SetMessageHandler<FarRegionUnload>(OnReceiveFarRegionUnload);

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

    private void OnReceiveFarRegionData(FarRegionData data)
    {
        // Decompress if needed
        if (data.Compressed)
        {
            if (data.Heightmap != null && data.Heightmap.Points == null && data.CompressedPoints != null)
            {
                data.Heightmap.Points = DecompressToIntArray(data.CompressedPoints, data.Heightmap.GridSize * data.Heightmap.GridSize);
            }
            if (data.Heightmap != null && data.Heightmap.Colors == null && data.CompressedColors != null)
            {
                data.Heightmap.Colors = DecompressToIntArray(data.CompressedColors, data.Heightmap.GridSize * data.Heightmap.GridSize);
            }
        }

        if (config.Enabled)
        {
            renderer.BuildRegion(data);
        }
    }

    private void OnReceiveFarRegionUnload(FarRegionUnload packet)
    {
        if (config.Enabled)
        {
            foreach (var idx in packet.RegionIndices)
            {
                renderer.UnloadRegion(idx);
            }
        }
    }

    private int[] DecompressToIntArray(byte[] compressed, int expectedLength)
    {
        using var input = new System.IO.MemoryStream(compressed);
        using var ds = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        var buffer = new byte[expectedLength * sizeof(int)];
        int read;
        int offset = 0;
        while ((read = ds.Read(buffer, offset, buffer.Length - offset)) > 0)
        {
            offset += read;
            if (offset >= buffer.Length) break;
        }
        var ints = new int[expectedLength];
        Buffer.BlockCopy(buffer, 0, ints, 0, buffer.Length);
        return ints;
    }

    public void Init()
    {
        var channel = capi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);
        if (channel != null)
        {
            channel.SendPacket(new FarseerEnable
            {
                PlayerConfig = config.ToServerPlayerConfig(),
                SupportsCompressedFarRegions = true,
            });
        }
        renderer.Init();
    }

    public void Dispose()
    {
        this.renderer?.Dispose();
    }
}
