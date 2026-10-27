using System;
using System.Collections.Generic;
using System.Linq;

using Cairo;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Draws a non-focusing, GUI-scaled build information panel.</summary>
internal sealed partial class BuildModeHud : HudElement, IBuildModeIndicator
{
    private const double KeycapSpacing = 3;
    private const double KeybindDividerPadding = 6;
    private const double KeybindDividerWidth = 1;
    private const double SectionsTop = 116;
    private const double SectionSpacing = 8;
    private const double BottomPadding = 20;
    private readonly CollapsibleSection[] sections;
    private bool disposed;
    private BuildModeInfo info;
    private BuildModeKeybindHint[] keybindHints = [];
    private GuiElementTable? controlsTable;
    private float keybindRefreshElapsed;
    private string displayLocale = Lang.CurrentLocale;
    private bool RotationUnavailable => info.IsSuspended || info.OrientationCount <= 1;
    private GuiElementTable ControlsTable => controlsTable
        ?? throw new InvalidOperationException("Controls content is only available during HUD composition.");

    #region Public API
    /// <summary>Retains client GUI services; composition is deferred until first activation.</summary>
    public BuildModeHud(ICoreClientAPI api) : this(api, () => BuildModeRoomInfo.Read(api)) { }

    internal BuildModeHud(ICoreClientAPI api, WorkAreaController workArea)
        : this(api, () => BuildModeRoomInfo.Read(api), workArea) { }

    /// <summary>Allows deterministic room samples without replacing the native GUI composition path.</summary>
    internal BuildModeHud(ICoreClientAPI api, System.Func<BuildModeRoomInfo> sampleRoom,
        WorkAreaController? workArea = null) : base(api)
    {
        this.sampleRoom = sampleRoom ?? throw new ArgumentNullException(nameof(sampleRoom));
        this.workArea = workArea;
        if (workArea is not null) workArea.Changed += OnWorkAreaChanged;
        roomSection = new CollapsibleSection("room", "vanillabuildingexpanded:build-info-room",
            () => RoomTable.Measure().Width, _ => RoomTable.Measure().Height, ComposeRoomContent);
        workAreaSection = new CollapsibleSection("workArea", "vanillabuildingexpanded:build-info-workarea",
            () => WorkAreaTable.Measure().Width, _ => WorkAreaTable.Measure().Height, ComposeWorkAreaContent);
        sections =
        [
            roomSection,
            workAreaSection,
            new CollapsibleSection("controls", "vanillabuildingexpanded:build-info-controls",
                () => ControlsTable.Measure().Width, _ => ControlsTable.Measure().Height, ComposeControlsContent, collapsed: true)
        ];
        api.Settings.Float.AddWatcher("guiScale", OnGuiScaleChanged);
    }

    /// <summary>Never takes focus from gameplay or other dialogs.</summary>
    public override bool Focusable => false;
    /// <summary>Removes the closed HUD from native dialog routing.</summary>
    public override bool UnregisterOnClose => true;
    /// <summary>Routes only actual controls, captures, and hover exit with a free cursor.</summary>
    public override bool ShouldReceiveMouseEvents() => !disposed && IsOpened() && !capi.Input.MouseGrabbed
        && (ActiveWorkAreaTable?.WantsPointerEvents(capi.Input.MouseX, capi.Input.MouseY) == true
            || sections.Any(section => section.ToggleButton is { } button && (button.IsPressed
                || button.IsPositionInside(capi.Input.MouseX, capi.Input.MouseY))));
    /// <summary>Leaves keyboard input entirely to gameplay and existing dialogs.</summary>
    public override bool ShouldReceiveKeyboardEvents() => false;

    internal bool ReservesPointer(int x, int y) => !disposed && IsOpened() && !capi.Input.MouseGrabbed
        && (ActiveWorkAreaTable?.ReservesPointer(x, y) == true
            || sections.Any(section => section.ToggleButton is { } button
                && (button.IsPressed || button.IsPositionInside(x, y))));

    public override void OnMouseDown(MouseEvent args)
    {
        if (!disposed && IsOpened() && !capi.Input.MouseGrabbed)
        {
            ActiveWorkAreaTable?.OnMouseDown(capi, args);
            foreach (var section in sections)
                section.ToggleButton?.OnMouseDown(capi, args);
        }
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (!disposed && IsOpened())
        {
            if (capi.Input.MouseGrabbed) CancelWorkAreaInput();
            else ActiveWorkAreaTable?.OnMouseUp(capi, args);
            foreach (var section in sections)
                section.ToggleButton?.OnMouseUp(capi, args);
        }
    }

    // Do not call the dialog defaults: they consume events over the entire panel.
    public override void OnMouseMove(MouseEvent args)
    {
        if (!disposed && IsOpened() && !capi.Input.MouseGrabbed)
            ActiveWorkAreaTable?.OnMouseMove(capi, args);
    }
    public override void OnMouseWheel(MouseWheelEventArgs args) { }

    public override void OnRenderGUI(float deltaTime)
    {
        if (disposed) return;
        if (IsOpened())
        {
            if (capi.Input.MouseGrabbed) CancelWorkAreaInput();
            bool roomWasCollapsed = roomSection.IsCollapsed;
            bool sectionsChanged = false;
            foreach (var section in sections)
                sectionsChanged |= section.ApplyPendingToggle();
            if (sectionsChanged)
            {
                roomRefreshElapsed = 0;
                if (roomWasCollapsed && !roomSection.IsCollapsed) RefreshRoom();
                ResetHudComposition();
                SetVisible(true);
            }
            if (workAreaDirty)
            {
                ResetHudComposition();
                SetVisible(true);
            }
            if (!roomSection.IsCollapsed)
            {
                roomRefreshElapsed += deltaTime;
                if (roomRefreshElapsed >= 0.5f)
                {
                    roomRefreshElapsed = 0;
                    RefreshRoom();
                    if (SingleComposer is null) ComposeHud();
                }
            }
            keybindRefreshElapsed += deltaTime;
            if (keybindRefreshElapsed >= 0.5f)
            {
                keybindRefreshElapsed = 0;
                SetVisible(true);
            }
        }
        base.OnRenderGUI(deltaTime);
    }

    public void Update(BuildModeInfo info)
    {
        if (disposed || this.info == info) return;
        bool wasRotationUnavailable = RotationUnavailable;
        this.info = info;
        if (SingleComposer is not null)
        {
            SingleComposer.GetDynamicText("title").SetNewText(GetTitle());
            var information = (GuiElementTable)SingleComposer.GetElement("information-table");
            ((GuiElementDynamicText)information.GetElement("snapping")).SetNewText(GetSnapping());
            var rotation = (GuiElementDynamicText)information.GetElement("rotation");
            if (wasRotationUnavailable != RotationUnavailable)
                rotation.Font.WithColor([.. (RotationUnavailable ? UiPalette.UnavailableColor : UiPalette.ValueColor)]);
            rotation.SetNewText(GetRotation(), forceRedraw: wasRotationUnavailable != RotationUnavailable);
        }
    }

    /// <summary>Composes once and opens without focus, or hides while retaining reusable artwork.</summary>
    public void SetVisible(bool visible)
    {
        if (disposed) return;
        if (!visible)
        {
            roomRefreshElapsed = 0;
            foreach (var section in sections)
                section.CancelInput();
            CancelWorkAreaInput();
            TryClose();
            return;
        }
        if (!IsOpened() && !roomSection.IsCollapsed) RefreshRoom();
        if (displayLocale != Lang.CurrentLocale)
        {
            displayLocale = Lang.CurrentLocale;
            ResetHudComposition();
        }
        var currentHints = BuildModeKeybinds.Read(capi.Input);
        if (keybindHints.Length != currentHints.Length
            || keybindHints.Where((hint, i) => hint.Action != currentHints[i].Action
                || !hint.Keys.SequenceEqual(currentHints[i].Keys)
                || !hint.AlternateKeys.SequenceEqual(currentHints[i].AlternateKeys)).Any())
        {
            keybindHints = currentHints;
            ResetHudComposition();
        }
        if (workAreaDirty) ResetHudComposition();
        if (SingleComposer is null) ComposeHud();
        if (!IsOpened()) TryOpen(false);
    }

    /// <summary>Builds the information and controls tables, transferring registered tables to the composer.</summary>
    private void ComposeHud()
    {
        GuiComposer? composer = null;
        GuiElementTable? informationTable = null;
        try
        {
            controlsTable = CreateControlsTable(capi, keybindHints);
            roomTable = CreateRoomTable();
            workAreaTable = CreateWorkAreaTable();
            workAreaDirty = false;
            double width = Math.Max(320, sections.Max(section => section.MeasureWidth()));
            double height = SectionsTop + sections.Sum(section => section.MeasureHeight(width))
                + (sections.Length - 1) * SectionSpacing + BottomPadding;
            var bounds = ElementBounds.Fixed(0, 0, width, height)
                .WithAlignment(EnumDialogArea.LeftTop).WithFixedOffset(12, 12);
            composer = capi.Gui.CreateCompo("vbe-build-mode", bounds);
            composer
                .AddStaticCustomDraw(ElementBounds.Fixed(0, 0, width, height),
                    (ctx, surface, backgroundBounds) => DrawBackground(ctx, surface, backgroundBounds, width, height))
                .AddStaticCustomDraw(ElementBounds.Fixed(12, 10, 36, 36), DrawIcon)
                .AddDynamicText(GetTitle(), CairoFont.WhiteSmallText().WithFontSize(UiPalette.TitleFontSize).WithColor([.. UiPalette.ValueColor]),
                    ElementBounds.Fixed(58, 14, 250, 30), "title");
            informationTable = CreateInformationTable();
            if (!composer.TryAddTable(informationTable, "information-table"))
                throw new InvalidOperationException("The information table was unexpectedly skipped during HUD composition.");
            double sectionY = SectionsTop;
            foreach (var section in sections)
            {
                sectionY += section.Compose(capi, composer, sectionY, width) + SectionSpacing;
            }
            SingleComposer = composer.Compose();
            composer = null;
        }
        finally
        {
            var table = controlsTable;
            var pendingRoom = roomTable;
            var pendingWorkArea = workAreaTable;
            controlsTable = null;
            roomTable = null;
            workAreaTable = null;
            try { composer?.Dispose(); }
            finally
            {
                // Collapsed measurement never registers the table; registered tables remain composer-owned.
                try
                {
                    if (table is { IsRegistered: false }) table.Dispose();
                }
                finally
                {
                    try
                    {
                        if (informationTable is { IsRegistered: false }) informationTable.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (pendingRoom is { IsRegistered: false }) pendingRoom.Dispose();
                        }
                        finally
                        {
                            if (pendingWorkArea is { IsRegistered: false }) pendingWorkArea.Dispose();
                        }
                    }
                }
            }
        }
    }

    /// <summary>Reserves the existing label/value footprints while letting table tracks position both information rows.</summary>
    private GuiElementTable CreateInformationTable()
    {
        var table = new GuiElementTable(capi, ElementBounds.Fixed(12, 54, 0, 0),
            new TableOptions(new Dictionary<int, TableColumn>
            {
                [0] = TableColumn.Fixed(88),
                [1] = TableColumn.Fixed(200)
            }, columnGap: 8, minimumRowHeight: 24, verticalAlignment: EnumVerticalAlign.Middle))
        {
            DividerRenderer = new FadingRowTableDividerRenderer()
        };
        bool complete = false;
        try
        {
            AddInformationRow(table, "snapping", GetSnapping(), false);
            AddInformationRow(table, "rotation", GetRotation(), RotationUnavailable);
            complete = true;
            return table;
        }
        finally
        {
            if (!complete) table.Dispose();
        }
    }

    /// <summary>Adds a passive label and native dynamic value with a lookup key local to the owning table.</summary>
    private void AddInformationRow(GuiElementTable table, string key, string value, bool unavailable)
    {
        var labelFont = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor]);
        double textHeight = MeasureTextHeight(labelFont);
        var label = new GuiElementStaticText(capi, Lang.Get($"vanillabuildingexpanded:build-info-{key}-label"),
            EnumTextOrientation.Left, ElementBounds.Fixed(0, 0, 88, textHeight), labelFont);
        var text = new GuiElementDynamicText(capi, value,
            CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize)
                .WithColor([.. (unavailable ? UiPalette.UnavailableColor : UiPalette.ValueColor)]),
            ElementBounds.Fixed(0, 0, 200, textHeight));
        try
        {
            table.AddRowCells([new TableCell(label), new TableCell(text, key: key)]);
        }
        catch
        {
            try { label.Dispose(); }
            finally { text.Dispose(); }
            throw;
        }
    }

    /// <summary>Closes native registration and releases the composer's texture exactly once.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (workArea is not null) workArea.Changed -= OnWorkAreaChanged;
        CancelWorkAreaInput();
        roomInfo = default;
        capi.Settings.Float.RemoveWatcher("guiScale", OnGuiScaleChanged);
        TryClose();
        base.Dispose();
    }
    #endregion

    #region Private
    private string GetTitle() => Lang.Get(info.IsSuspended
        ? "vanillabuildingexpanded:build-info-suspended"
        : "vanillabuildingexpanded:build-info-title");

    private string GetSnapping() => Lang.Get($"vanillabuildingexpanded:build-info-snapping-{info.Snapping.GetCode()}");

    private string GetRotation() => info.IsSuspended
            ? Lang.Get("vanillabuildingexpanded:build-info-unavailable")
            : info.OrientationCount <= 1
                ? Lang.Get("vanillabuildingexpanded:build-info-no-rotation")
                : info.RotationAngleDegrees is float angle
                    ? Lang.Get("vanillabuildingexpanded:build-info-angle", angle)
                    : Lang.Get("vanillabuildingexpanded:build-info-orientation", info.OrientationIndex + 1, info.OrientationCount);

    /// <summary>Builds passive rows with measured footprints; table tracks determine both controls dimensions.</summary>
    /// <remarks>Drawing stays in the HUD, while accepted child ownership and all layout belong to the table.</remarks>
    internal static GuiElementTable CreateControlsTable(ICoreClientAPI api, IReadOnlyList<BuildModeKeybindHint> hints)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(hints);
        if (hints.Count == 0) throw new ArgumentException("Controls require at least one keybinding hint.", nameof(hints));
        if (!float.IsFinite(RuntimeEnv.GUIScale) || RuntimeEnv.GUIScale <= 0)
            throw new InvalidOperationException("GUI scale must be finite and positive before measuring controls.");
        var table = new GuiElementTable(api, ElementBounds.Fixed(0, 0, 0, 0),
            new TableOptions(new Dictionary<int, TableColumn>
            {
                [0] = TableColumn.Auto(88),
                [1] = TableColumn.Auto(200)
            }, columnGap: 12, minimumRowHeight: 20, verticalAlignment: EnumVerticalAlign.Middle))
        {
            DividerRenderer = new FadingRowTableDividerRenderer()
        };
        bool complete = false;
        try
        {
            using var measureFont = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize);
            using var measureKeyFont = CairoFont.WhiteSmallText().WithFontSize(UiPalette.KeybindFontSize);
            double labelHeight = MeasureTextHeight(measureFont);
            // Native keycaps start one device pixel down and extend another 0.75 pixels for their border.
            double bindingHeight = Math.Ceiling(((int)measureKeyFont.GetFontExtents().Height + 1.75) / RuntimeEnv.GUIScale);
            foreach (var hint in hints)
            {
                double labelWidth = Math.Ceiling(measureFont.GetTextExtents(hint.Action).Width / RuntimeEnv.GUIScale);
                double bindingWidth = Math.Ceiling(MeasureKeybindWidth(measureKeyFont, hint));
                var binding = new GuiElementCustomDraw(api, ElementBounds.Fixed(0, 0, bindingWidth, bindingHeight),
                    (ctx, _, bounds) => DrawKeybind(api, ctx, bounds, hint));
                table.AddRow(
                    new GuiElementCustomDraw(api, ElementBounds.Fixed(0, 0, labelWidth, labelHeight),
                        (ctx, _, bounds) => DrawControlLabel(ctx, bounds, hint.Action,
                            GuiElement.scaled(binding.Bounds.fixedX - 12))),
                    binding);
            }
            complete = true;
            return table;
        }
        finally
        {
            if (!complete) table.Dispose();
        }
    }

    /// <summary>Reserves a full native font line so row alignment centers static and changing text consistently.</summary>
    private static double MeasureTextHeight(CairoFont font) =>
        Math.Ceiling(font.GetFontExtents().Height / RuntimeEnv.GUIScale);

    /// <summary>Preserves native label drawing against the allocated column without retaining font resources.</summary>
    private static void DrawControlLabel(Context ctx, ElementBounds bounds, string action, double columnWidth)
    {
        using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor]);
        font.SetupContext(ctx);
        // Cells retain preferred widths; the table-arranged binding origin gives the shared label track's width.
        new TextDrawUtil().AutobreakAndDrawMultilineTextAt(ctx, font, action,
            bounds.drawX, bounds.drawY, columnWidth);
    }

    /// <summary>Registers the already measured table within the section's existing content parent.</summary>
    private void ComposeControlsContent(GuiComposer composer, ElementBounds _)
    {
        if (!composer.TryAddTable(ControlsTable, "controls-table"))
            throw new InvalidOperationException("The controls table was unexpectedly skipped during HUD composition.");
    }

    private static double MeasureKeybindWidth(CairoFont font, BuildModeKeybindHint hint)
    {
        double width = MeasureKeyCombinationWidth(font, hint.Keys.AsSpan());
        if (!hint.AlternateKeys.IsEmpty)
            width += GuiElement.scaled(2 * KeybindDividerPadding + KeybindDividerWidth)
                + MeasureKeyCombinationWidth(font, hint.AlternateKeys.AsSpan());
        return (width + 3) / RuntimeEnv.GUIScale;
    }

    private static double MeasureKeyCombinationWidth(CairoFont font, ReadOnlySpan<string> keys)
    {
        double width = 0;
        foreach (string key in keys)
        {
            if (width > 0) width += GuiElement.scaled(KeycapSpacing);
            width += (int)(font.GetTextExtents(key).Width + GuiElement.scaled(8));
        }
        return width;
    }

    private static void DrawKeybind(ICoreClientAPI api, Context ctx, ElementBounds bounds, BuildModeKeybindHint hint)
    {
        using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.KeybindFontSize).WithColor([.. UiPalette.ValueColor]);
        double textHeight = font.GetFontExtents().Height;
        double[] color = [.. UiPalette.ValueColor];
        double DrawKeys(ReadOnlySpan<string> keys)
        {
            ctx.Save();
            try
            {
                double width = 0;
                foreach (string key in keys)
                {
                    if (width > 0)
                    {
                        double gap = GuiElement.scaled(KeycapSpacing);
                        ctx.Translate(gap, 0);
                        width += gap;
                    }
                    // A translated origin suppresses the native '+' separator without changing the keycap styling.
                    double keyWidth = HotkeyComponent.DrawHotkey(api, key, 0, 0, ctx, font,
                        (int)textHeight, textHeight, 0, 0, 4, color);
                    ctx.Translate(keyWidth, 0);
                    width += keyWidth;
                }
                return width;
            }
            finally
            {
                ctx.Restore();
            }
        }
        ctx.Save();
        try
        {
            ctx.Translate(bounds.drawX, bounds.drawY);
            font.SetupContext(ctx);
            double x = DrawKeys(hint.Keys.AsSpan());
            if (!hint.AlternateKeys.IsEmpty)
            {
                double padding = GuiElement.scaled(KeybindDividerPadding);
                double dividerWidth = GuiElement.scaled(KeybindDividerWidth);
                double keycapHeight = (int)textHeight;
                ctx.SetSourceRGBA([.. UiPalette.KeybindDividerColor]);
                ctx.Rectangle(x + 1 + padding, 1 + keycapHeight * 0.25, dividerWidth, keycapHeight * 0.5);
                ctx.Fill();
                ctx.Translate(x + 2 * padding + dividerWidth, 0);
                DrawKeys(hint.AlternateKeys.AsSpan());
            }
        }
        finally
        {
            ctx.Restore();
        }
    }

    internal static double GetFadeOpacity(double progress) => 1 - progress * progress * (3 - 2 * progress);

    private static void DrawBackground(Context ctx, ImageSurface surface, ElementBounds bounds, double width, double height)
    {
        ctx.Save();
        try
        {
            ctx.Translate(bounds.drawX, bounds.drawY);
            ctx.Scale(bounds.InnerWidth / width, bounds.InnerHeight / height);
            ctx.Rectangle(0, 0, width, height);
            ctx.Clip();

            // Mask an isolated group so only the background fades, not the icon or text.
            ctx.PushGroup();
            ctx.SetSourceRGBA([.. UiPalette.PanelBackgroundColor]);
            ctx.Paint();
            ctx.SetSourceRGBA([.. UiPalette.TitleOverlayColor]);
            ctx.Rectangle(0, 0, width, 48);
            ctx.Fill();

            ctx.Operator = Operator.DestIn;
            var opaque = UiPalette.OpaqueFadeMaskColor;
            var transparent = UiPalette.TransparentFadeMaskColor;
            using var rightFade = new LinearGradient(width - 24, 0, width, 0) { Extend = Extend.Pad };
            using var bottomFade = new LinearGradient(0, height - 20, 0, height) { Extend = Extend.Pad };
            // Sample smoothstep because Cairo interpolates linearly between gradient stops.
            const int intervals = 16;
            for (int i = 0; i <= intervals; i++)
            {
                double progress = i / (double)intervals;
                double opacity = GetFadeOpacity(progress);
                var mask = new Color(opaque[0], opaque[1], opaque[2],
                    transparent[3] + (opaque[3] - transparent[3]) * opacity);
                rightFade.AddColorStop(progress, mask);
                bottomFade.AddColorStop(progress, mask);
            }
            ctx.SetSource(rightFade);
            ctx.Paint();
            ctx.SetSource(bottomFade);
            ctx.Paint();
            ctx.PopGroupToSource();
            ctx.Operator = Operator.Over;
            ctx.Paint();
        }
        finally
        {
            ctx.Restore();
        }
    }

    /// <summary>Invalidates scaled artwork; hidden HUDs rebuild lazily on their next activation.</summary>
    private void OnGuiScaleChanged(float scale)
    {
        if (disposed) return;
        bool visible = IsOpened();
        ResetHudComposition();
        if (visible) SetVisible(true);
    }

    /// <summary>Scales the official artwork and applies translucency without changing its source pixels.</summary>
    private void DrawIcon(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        using var icon = GuiElement.getImageSurfaceFromAsset(capi,
            new AssetLocation("vanillabuildingexpanded", "textures/gui/build-mode.png"));
        ctx.Save();
        try
        {
            // Preserve the transparent background and apply opacity to the hammer alone.
            ctx.Translate(bounds.drawX, bounds.drawY);
            ctx.Scale(bounds.InnerWidth / icon.Width, bounds.InnerHeight / icon.Height);
            using var pattern = new SurfacePattern(icon) { Filter = Filter.Best };
            ctx.SetSource(pattern);
            ctx.PaintWithAlpha(UiPalette.IconOpacity);
        }
        finally
        {
            ctx.Restore();
        }
    }
    #endregion
}
