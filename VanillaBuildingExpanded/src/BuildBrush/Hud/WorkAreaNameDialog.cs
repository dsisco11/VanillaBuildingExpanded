using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaNameDialog : GuiDialog
{
    private readonly System.Func<string, string?> submit;
    public override string ToggleKeyCombinationCode => null!;
    public override bool UnregisterOnClose => true;
    public override bool DisableMouseGrab => true;
    public override bool CaptureAllInputs() => true;

    public WorkAreaNameDialog(ICoreClientAPI api, string title, string name, System.Func<string, string?> submit)
        : base(api)
    {
        this.submit = submit;
        var background = ElementStdBounds.DialogBackground().WithFixedPadding(GuiStyle.ElementToDialogPadding);
        var composer = api.Gui.CreateCompo("vbe-workarea-name", ElementStdBounds.AutosizedMainDialog);
        try
        {
            composer.AddShadedDialogBG(background)
                .AddDialogTitleBar(title, () => TryClose())
                .BeginChildElements(background)
                    .AddTextInput(ElementBounds.Fixed(0, 0, 400, 30), null, CairoFont.TextInput(), "name")
                    .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 40, 400, 50), "error")
                    .AddSmallButton(Lang.Get("Cancel"), () => TryClose(), ElementBounds.Fixed(0, 100, 110, 30))
                    .AddSmallButton(Lang.Get("Save"), Submit, ElementBounds.Fixed(290, 100, 110, 30))
                .EndChildElements().Compose();
            composer.GetTextInput("name").SetValue(name);
            SingleComposer = composer;
        }
        catch
        {
            composer.Dispose();
            throw;
        }
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        SingleComposer.FocusElement(SingleComposer.GetTextInput("name").TabIndex);
    }

    private bool Submit()
    {
        string? error = submit(SingleComposer.GetTextInput("name").GetText());
        if (error is null) TryClose();
        else SingleComposer.GetDynamicText("error").SetNewText(error);
        return true;
    }

    public override void OnKeyDown(KeyEvent args)
    {
        if (args.KeyCode == (int)GlKeys.Escape) TryClose();
        else if (args.KeyCode == (int)GlKeys.Enter) Submit();
        else base.OnKeyDown(args);
        args.Handled = true;
    }

    public override void OnKeyPress(KeyEvent args)
    {
        base.OnKeyPress(args);
        args.Handled = true;
    }

    public override void OnKeyUp(KeyEvent args)
    {
        base.OnKeyUp(args);
        args.Handled = true;
    }
}
