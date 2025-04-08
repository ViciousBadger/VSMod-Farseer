using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Farseer;

public class FarseerConfigDialog : GuiDialog
{
    FarseerModSystem modSystem;

    GuiComposer composer;

    private long configSaveDelayListener = -1;

    public FarseerConfigDialog(FarseerModSystem modSystem, ICoreClientAPI capi) : base(capi)
    {
        this.modSystem = modSystem;
    }

    public override string ToggleKeyCombinationCode => null;

    public override void OnGuiOpened()
    {
        ComposeDialog();
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();

        // CancelDebouncedSave();
        // modSystem.Client.SaveConfigChanges();
    }

    private void ComposeDialog()
    {
        var contentBounds = ElementBounds.Fixed(25.0, 45.0, 200.0, 30.0);

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.LeftMiddle).WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);
        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        composer =
        Composers["farseerconfig"] = capi.Gui.CreateCompo("farseerconfig", dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(Lang.Get("farseer:config-title"), OnTitleBarClose)

            .AddStaticText(Lang.Get("farseer:view-distance"), CairoFont.WhiteDetailText(), contentBounds)
            .AddSlider(OnChangeFarViewDistance, contentBounds = contentBounds.BelowCopy(), "farViewDistanceSlider")

            .AddStaticText(Lang.Get("farseer:sky-tint"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeSkyTint, contentBounds = contentBounds.BelowCopy(), "skyTintSlider")

            .AddStaticText(Lang.Get("farseer:color-tint-r"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeColorTintR, contentBounds = contentBounds.BelowCopy(), "colorTintRSlider")

            .AddStaticText(Lang.Get("farseer:color-tint-g"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeColorTintG, contentBounds = contentBounds.BelowCopy(), "colorTintGSlider")

            .AddStaticText(Lang.Get("farseer:color-tint-b"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeColorTintB, contentBounds = contentBounds.BelowCopy(), "colorTintBSlider")

            .AddStaticText(Lang.Get("farseer:color-tint-a"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeColorTintA, contentBounds = contentBounds.BelowCopy(), "colorTintASlider")

            .AddStaticText(Lang.Get("farseer:light-level-bias"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeLightLevelBias, contentBounds = contentBounds.BelowCopy(), "lightLevelBiasSlider")

            .AddStaticText(Lang.Get("farseer:fade-bias"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeFadeBias, contentBounds = contentBounds.BelowCopy(), "fadeBiasSlider")

            .AddButton(Lang.Get("farseer:reset"), ResetConfig, contentBounds = contentBounds.BelowCopy(0.0, 20.0), EnumButtonStyle.Small);

        bgBounds.WithChildren(contentBounds);

        ReadSliders();

        composer.Compose();
        Composers["farseerconfig"] = composer;
    }

    private void ReadSliders()
    {
        var config = modSystem.Client.Config;
        composer.GetSlider("farViewDistanceSlider").SetValues(config.FarViewDistance, 512, 16384, 512);
        composer.GetSlider("skyTintSlider").SetValues((int)(config.SkyTint * 100), 0, 1000, 10);
        composer.GetSlider("colorTintRSlider").SetValues((int)(config.ColorTintR * 100), 0, 100, 1);
        composer.GetSlider("colorTintGSlider").SetValues((int)(config.ColorTintG * 100), 0, 100, 1);
        composer.GetSlider("colorTintBSlider").SetValues((int)(config.ColorTintB * 100), 0, 100, 1);
        composer.GetSlider("colorTintASlider").SetValues((int)(config.ColorTintA * 100), 0, 100, 1);
        composer.GetSlider("lightLevelBiasSlider").SetValues((int)(config.LightLevelBias * 100), 1, 99, 1);
        composer.GetSlider("fadeBiasSlider").SetValues((int)(config.FadeBias * 100), 1, 99, 1);
    }

    private bool ResetConfig()
    {
        modSystem.Client.Config.Reset();
        modSystem.Client.SaveConfigChanges();
        ReadSliders();
        return true;
    }

    private bool OnChangeFarViewDistance(int value)
    {
        modSystem.Client.Config.FarViewDistance = value;
        StartDebouncedSave();
        return true;
    }

    private bool OnChangeSkyTint(int value)
    {
        modSystem.Client.Config.SkyTint = value / 100f;
        StartDebouncedSave();
        return true;
    }
    private bool OnChangeColorTintR(int value)
    {
        modSystem.Client.Config.ColorTintR = value / 100f;
        StartDebouncedSave();
        return true;
    }
    private bool OnChangeColorTintG(int value)
    {
        modSystem.Client.Config.ColorTintG = value / 100f;
        StartDebouncedSave();
        return true;
    }
    private bool OnChangeColorTintB(int value)
    {
        modSystem.Client.Config.ColorTintB = value / 100f;
        StartDebouncedSave();
        return true;
    }
    private bool OnChangeColorTintA(int value)
    {
        modSystem.Client.Config.ColorTintA = value / 100f;
        StartDebouncedSave();
        return true;
    }
    private bool OnChangeLightLevelBias(int value)
    {
        modSystem.Client.Config.LightLevelBias = value / 100f;
        StartDebouncedSave();
        return true;
    }
    private bool OnChangeFadeBias(int value)
    {
        modSystem.Client.Config.FadeBias = value / 100f;
        StartDebouncedSave();
        return true;
    }

    private void StartDebouncedSave()
    {
        CancelDebouncedSave();
        configSaveDelayListener = capi.Event.RegisterCallback((_) =>
        {
            modSystem.Client.SaveConfigChanges();
            configSaveDelayListener = -1;
        }, 1000);
    }

    private void CancelDebouncedSave()
    {
        if (configSaveDelayListener != -1)
        {
            capi.Event.UnregisterCallback(configSaveDelayListener);
            configSaveDelayListener = -1;
        }
    }
}
