using System;
using System.Collections.Generic;
using System.Linq;

using Cairo;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed partial class BuildModeHud
{
    private const double TagHorizontalPadding = 3;
    private const double TagVerticalPadding = 2;
    private const double TagBorderWidth = 1;
    private static readonly RoomQualifications[] TagOrder =
        [RoomQualifications.Room, RoomQualifications.Shelter, RoomQualifications.Cellar, RoomQualifications.Greenhouse];
    private readonly System.Func<BuildModeRoomInfo> sampleRoom;
    private readonly CollapsibleSection roomSection;
    private BuildModeRoomInfo roomInfo;
    private GuiElementTable? roomTable;
    private float roomRefreshElapsed;
    private GuiElementTable RoomTable => roomTable
        ?? throw new InvalidOperationException("Room content is only available during HUD composition.");

    /// <summary>Rebuilds only when row structure or qualification artwork changes; numeric values retain their textures.</summary>
    private void RefreshRoom()
    {
        var next = sampleRoom();
        if (next == roomInfo) return;
        bool structureChanged = next.Status != roomInfo.Status || next.Qualifications != roomInfo.Qualifications;
        roomInfo = next;
        if (SingleComposer is null) return;
        if (structureChanged)
        {
            ResetHudComposition();
            return;
        }
        if (roomInfo.HasDetails && SingleComposer.GetElement("room-table") is GuiElementTable table)
        {
            foreach (string key in new[] { "dimensions", "sunlight", "cooling" })
            {
                var text = (GuiElementDynamicText)table.GetElement(key);
                var color = RoomValueColor(key);
                bool colorChanged = !text.Font.Color.SequenceEqual(color);
                if (colorChanged) text.Font.WithColor(color);
                text.SetNewText(RoomValue(key), forceRedraw: colorChanged);
            }
        }
    }

    private static string RoomText(string key, params object[] args) =>
        Lang.Get($"vanillabuildingexpanded:build-info-room-{key}", args);

    private string RoomValue(string key) => key switch
    {
        "dimensions" => RoomText("dimensions", roomInfo.Width, roomInfo.Height, roomInfo.Length),
        "sunlight" => PercentageText(roomInfo.SunlightPercent),
        "cooling" => PercentageText(roomInfo.CoolingPercent),
        "reason" => RoomText(roomInfo.Status == RoomInfoStatus.Incomplete ? "incomplete"
            : roomInfo.Status == RoomInfoStatus.Unavailable ? "missing" : "not-enclosed"),
        _ => roomInfo.Status != RoomInfoStatus.Complete
            ? Lang.Get("vanillabuildingexpanded:build-info-unavailable") : RoomText("none")
    };

    private static string PercentageText(int? value) => value is int percent
        ? RoomText("percent", percent) : Lang.Get("vanillabuildingexpanded:build-info-unavailable");

    private double[] RoomValueColor(string key) =>
        [.. (key is "type" or "reason" || key == "sunlight" && roomInfo.SunlightPercent is null
            || key == "cooling" && roomInfo.CoolingPercent is null ? UiPalette.UnavailableColor : UiPalette.ValueColor)];

    /// <summary>Measures localized tags on one line and builds passive, owning rows within the room section.</summary>
    private GuiElementTable CreateRoomTable()
    {
        var table = new GuiElementTable(capi, ElementBounds.Fixed(0, 0, 0, 0),
            new TableOptions(new Dictionary<int, TableColumn>
            {
                [0] = TableColumn.Auto(88),
                [1] = TableColumn.Auto(200)
            }, columnGap: 12, minimumRowHeight: 24, verticalAlignment: EnumVerticalAlign.Middle))
        {
            DividerRenderer = new FadingRowTableDividerRenderer()
        };
        bool complete = false;
        try
        {
            using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize);
            double Width(string text) => Math.Ceiling(font.GetTextExtents(text).Width / RuntimeEnv.GUIScale) + 2;
            double textHeight = MeasureTextHeight(font);
            // Reserve every localized tag, including while unavailable, to avoid panel-width jitter on movement.
            var tags = TagOrder.Select(tag => (Tag: tag, Text: RoomText(tag.ToString().ToLowerInvariant())))
                .Select(tag => (tag.Tag, tag.Text,
                    Width: Math.Ceiling(font.GetTextExtents(tag.Text).Width / RuntimeEnv.GUIScale)
                        + 2 * (TagHorizontalPadding + TagBorderWidth),
                    Height: Math.Ceiling(font.GetTextExtents(tag.Text).Height / RuntimeEnv.GUIScale)
                        + 2 * (TagVerticalPadding + TagBorderWidth))).ToArray();
            double tagHeight = tags.Max(tag => tag.Height);
            double valueWidth = Math.Max(tags.Sum(tag => tag.Width) + 4 * (tags.Length - 1),
                new[] { RoomText("incomplete"), RoomText("missing"), RoomText("not-enclosed"),
                    RoomText("dimensions", 14, 14, 14), PercentageText(null), PercentageText(100) }.Max(Width));

            void AddRow(string key, GuiElement value)
            {
                GuiElement? label = null;
                try
                {
                    string text = key == "reason" ? "" : RoomText($"{key}-label");
                    label = new GuiElementStaticText(capi, text, EnumTextOrientation.Left,
                        ElementBounds.Fixed(0, 0, Width(text), textHeight),
                        CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor]));
                    table.AddRowCells([new TableCell(label), new TableCell(value, key: key)]);
                }
                catch
                {
                    try { label?.Dispose(); }
                    finally { value.Dispose(); }
                    throw;
                }
            }

            void AddText(string key) => AddRow(key, new GuiElementDynamicText(capi, RoomValue(key),
                CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize)
                    .WithColor(RoomValueColor(key)),
                ElementBounds.Fixed(0, 0, valueWidth, textHeight)));

            if (roomInfo.Qualifications != RoomQualifications.None)
            {
                var qualifications = roomInfo.Qualifications;
                AddRow("type", new GuiElementCustomDraw(capi, ElementBounds.Fixed(0, 0, valueWidth, tagHeight),
                    (ctx, _, bounds) =>
                    {
                        double x = bounds.drawX;
                        foreach (var tag in tags)
                        {
                            if (!qualifications.HasFlag(tag.Tag)) continue;
                            DrawRoomTag(ctx, x, bounds.drawY + GuiElement.scaled((tagHeight - tag.Height) / 2),
                                tag.Width, tag.Text, tag.Tag);
                            x += GuiElement.scaled(tag.Width + 4);
                        }
                    }));
            }
            else AddText("type");
            if (roomInfo.HasDetails)
            {
                AddText("dimensions");
                AddText("sunlight");
                AddText("cooling");
            }
            else if (roomInfo.Qualifications == RoomQualifications.None) AddText("reason");
            complete = true;
            return table;
        }
        finally
        {
            if (!complete) table.Dispose();
        }
    }

    /// <summary>Draws noninteractive, keycap-like qualifications with distinct tints and explicit text labels.</summary>
    private static void DrawRoomTag(Context ctx, double x, double y, double width, string text, RoomQualifications tag)
    {
        ctx.Save();
        try
        {
            using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize)
                .WithColor([.. UiPalette.ValueColor]);
            font.SetupContext(ctx);
            var extents = ctx.TextExtents(text);
            double height = Math.Ceiling(extents.Height / RuntimeEnv.GUIScale)
                + 2 * (TagVerticalPadding + TagBorderWidth);
            var tint = tag switch
            {
                RoomQualifications.Room => UiPalette.RoomTagColor,
                RoomQualifications.Shelter => UiPalette.ShelterTagColor,
                RoomQualifications.Cellar => UiPalette.CellarTagColor,
                _ => UiPalette.GreenhouseTagColor
            };
            GuiElement.RoundRectangle(ctx, x + GuiElement.scaled(TagBorderWidth / 2),
                y + GuiElement.scaled(TagBorderWidth / 2),
                GuiElement.scaled(width - TagBorderWidth), GuiElement.scaled(height - TagBorderWidth), GuiElement.scaled(4));
            ctx.SetSourceRGBA(tint[0] * 0.3, tint[1] * 0.3, tint[2] * 0.3, 0.95);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(tint[0], tint[1], tint[2], 1);
            ctx.LineWidth = GuiElement.scaled(TagBorderWidth);
            ctx.Stroke();
            ctx.SetSourceRGBA(font.Color);
            ctx.MoveTo(x + (GuiElement.scaled(width) - extents.Width) / 2 - extents.XBearing,
                y + (GuiElement.scaled(height) - extents.Height) / 2 - extents.YBearing);
            ctx.ShowText(text);
        }
        finally { ctx.Restore(); }
    }

    private void ComposeRoomContent(GuiComposer composer, ElementBounds _)
    {
        if (!composer.TryAddTable(RoomTable, "room-table"))
            throw new InvalidOperationException("The room table was unexpectedly skipped during HUD composition.");
    }
}
