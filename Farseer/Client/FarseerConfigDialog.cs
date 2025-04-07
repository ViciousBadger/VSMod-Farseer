using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Farseer;

public class FarseerConfigDialog : GuiDialog
{
    FarseerModSystem modSystem;

    bool dirty = false;

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

        if (dirty)
        {
            modSystem.Client.SaveConfigChanges();
            dirty = false;
        }
    }


    private void ComposeDialog()
    {
        var contentBounds = ElementBounds.Fixed(0.0, 25.0, 200.0, 20.0);

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.LeftMiddle).WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);
        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var composer =
        Composers["farseerconfig"] = capi.Gui.CreateCompo("farseerconfig", dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(Lang.Get("farseer:title"), OnTitleBarClose)
            // .BeginChildElements(dialogBounds)

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

            .AddStaticText(Lang.Get("farseer:light-level-adjust"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeLightLevelAdjust, contentBounds = contentBounds.BelowCopy(), "lightLevelAdjustSlider")

            .AddStaticText(Lang.Get("farseer:fade-bias"), CairoFont.WhiteDetailText(), contentBounds = contentBounds.BelowCopy())
            .AddSlider(OnChangeFadeBias, contentBounds = contentBounds.BelowCopy(), "fadeBiasSlider")

            .AddButton(Lang.Get("farseer:apply"), OnApplyChanges, contentBounds = contentBounds.BelowCopy());

        bgBounds.WithChildren(contentBounds);

        var config = modSystem.Client.Config;
        composer.GetSlider("farViewDistanceSlider").SetValues(config.FarViewDistance, 512, 16384, 512);
        composer.GetSlider("skyTintSlider").SetValues((int)(config.SkyTint * 100), 0, 300, 1);
        composer.GetSlider("colorTintRSlider").SetValues((int)(config.ColorTintR * 100), 0, 100, 1);
        composer.GetSlider("colorTintGSlider").SetValues((int)(config.ColorTintG * 100), 0, 100, 1);
        composer.GetSlider("colorTintBSlider").SetValues((int)(config.ColorTintB * 100), 0, 100, 1);
        composer.GetSlider("colorTintASlider").SetValues((int)(config.ColorTintA * 100), 0, 100, 1);
        composer.GetSlider("lightLevelAdjustSlider").SetValues((int)(config.LightLevelAdjust * 100), -100, 100, 1);
        composer.GetSlider("fadeBiasSlider").SetValues((int)(config.FadeBias * 100), 0, 100, 1);

        composer.Compose();
        Composers["farseerconfig"] = composer;
    }

    private bool OnApplyChanges()
    {
        if (dirty)
        {
            modSystem.Client.SaveConfigChanges();
            dirty = false;
        }
        return true;
    }

    private bool OnChangeFarViewDistance(int value)
    {
        modSystem.Client.Config.FarViewDistance = value;
        MarkDirty();
        return true;
    }

    private bool OnChangeSkyTint(int value)
    {
        modSystem.Client.Config.SkyTint = value / 100f;
        MarkDirty();
        return true;
    }
    private bool OnChangeColorTintR(int value)
    {
        modSystem.Client.Config.ColorTintR = value / 100f;
        MarkDirty();
        return true;
    }
    private bool OnChangeColorTintG(int value)
    {
        modSystem.Client.Config.ColorTintG = value / 100f;
        MarkDirty();
        return true;
    }
    private bool OnChangeColorTintB(int value)
    {
        modSystem.Client.Config.ColorTintB = value / 100f;
        MarkDirty();
        return true;
    }
    private bool OnChangeColorTintA(int value)
    {
        modSystem.Client.Config.ColorTintA = value / 100f;
        MarkDirty();
        return true;
    }
    private bool OnChangeLightLevelAdjust(int value)
    {
        modSystem.Client.Config.LightLevelAdjust = value / 100f;
        MarkDirty();
        return true;
    }
    private bool OnChangeFadeBias(int value)
    {
        modSystem.Client.Config.FadeBias = value / 100f;
        MarkDirty();
        return true;
    }

    private void MarkDirty()
    {
        dirty = true;
    }
}
