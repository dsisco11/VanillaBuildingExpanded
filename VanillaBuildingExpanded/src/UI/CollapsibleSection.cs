using System;

using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.UI;

internal sealed class CollapsibleSection
{
    private const double LeftPadding = 12;
    private const double RightPadding = 24;
    private const double TitleHeight = 24;
    private const double TitleContentGap = 4;
    private const double TitleRuleGap = 8;
    private const double TitleRuleMinimumWidth = 16;
    private const double TitleRuleHeight = 1;
    private const double ContentBottomPadding = 8;
    private const double ButtonHeight = 24;

    private readonly string titleKey;
    private readonly Func<double> measureContentWidth;
    private readonly Func<double, double> measureContentHeight;
    private readonly Action<GuiComposer, ElementBounds> composeContent;
    private bool togglePending;

    public string Id { get; }
    public bool IsCollapsed { get; private set; }
    public GuiElementCollapseButton? ToggleButton { get; private set; }

    public CollapsibleSection(string id, string titleKey,
        Func<double> measureContentWidth, Func<double, double> measureContentHeight,
        Action<GuiComposer, ElementBounds> composeContent, bool collapsed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);
        ArgumentNullException.ThrowIfNull(measureContentWidth);
        ArgumentNullException.ThrowIfNull(measureContentHeight);
        ArgumentNullException.ThrowIfNull(composeContent);
        Id = id;
        this.titleKey = titleKey;
        this.measureContentWidth = measureContentWidth;
        this.measureContentHeight = measureContentHeight;
        this.composeContent = composeContent;
        IsCollapsed = collapsed;
    }

    // Measure expanded width even while collapsed so toggling does not change the overlay's width.
    public double MeasureWidth() => LeftPadding + measureContentWidth() + RightPadding;

    public double MeasureHeight(double width)
    {
        double contentHeight = IsCollapsed ? 0 : measureContentHeight(width - LeftPadding - RightPadding);
        return TitleHeight + TitleContentGap + contentHeight + ContentBottomPadding + ButtonHeight;
    }

    public void RequestToggle() => togglePending = !togglePending;

    public bool ApplyPendingToggle()
    {
        if (!togglePending) return false;
        togglePending = false;
        IsCollapsed = !IsCollapsed;
        return true;
    }

    public void CancelInput()
    {
        togglePending = false;
        ToggleButton?.CancelPress();
    }

    public double Compose(ICoreClientAPI api, GuiComposer composer, double top, double width)
    {
        double height = MeasureHeight(width);
        double contentWidth = width - LeftPadding - RightPadding;
        double buttonY = height - ButtonHeight;
        composer.BeginChildElements(ElementBounds.Fixed(0, top, width, height));
        string title = Lang.Get(titleKey);
        composer.AddStaticElement(new GuiElementCustomDraw(api,
            ElementBounds.Fixed(LeftPadding, 0, width - 2 * LeftPadding, TitleHeight),
            (ctx, _, bounds) => DrawTitle(ctx, bounds, title)), $"{Id}-title");
        if (!IsCollapsed)
        {
            var contentBounds = ElementBounds.Fixed(LeftPadding, TitleHeight + TitleContentGap,
                contentWidth, buttonY - TitleHeight - TitleContentGap - ContentBottomPadding);
            composer.BeginChildElements(contentBounds);
            composeContent(composer, contentBounds);
            composer.EndChildElements();
        }
        ToggleButton = new GuiElementCollapseButton(api,
            ElementBounds.Fixed(0, buttonY, width, ButtonHeight), IsCollapsed, RequestToggle);
        composer.AddInteractiveElement(ToggleButton, $"{Id}-toggle");
        composer.EndChildElements();
        return height;
    }

    /// <summary>Centers a localized heading between light rules, independently of content columns and collapse state.</summary>
    private static void DrawTitle(Context ctx, ElementBounds bounds, string title)
    {
        ctx.Save();
        try
        {
            ctx.Rectangle(bounds.drawX, bounds.drawY, bounds.InnerWidth, bounds.InnerHeight);
            ctx.Clip();
            using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize)
                .WithColor([.. UiPalette.SectionTitleColor]);
            font.SetupContext(ctx);
            var extents = ctx.TextExtents(title);
            double gap = GuiElement.scaled(TitleRuleGap);
            double available = Math.Max(1, bounds.InnerWidth - 2 * (gap + GuiElement.scaled(TitleRuleMinimumWidth)));
            while (extents.Width > available)
            {
                // Hinting can change glyph widths nonlinearly; remeasure and guarantee progress on each reduction.
                font.WithFontSize((float)(font.UnscaledFontsize * Math.Min(0.9, available / extents.Width)));
                font.SetupContext(ctx);
                extents = ctx.TextExtents(title);
            }
            double textLeft = bounds.drawX + (bounds.InnerWidth - extents.Width) / 2;
            double middleY = bounds.drawY + bounds.InnerHeight / 2;
            ctx.SetSourceRGBA([.. UiPalette.SectionDividerColor]);
            ctx.LineWidth = GuiElement.scaled(TitleRuleHeight);
            ctx.LineCap = LineCap.Butt;
            ctx.SetDash([], 0);
            if (textLeft - gap > bounds.drawX)
            {
                ctx.MoveTo(bounds.drawX, middleY);
                ctx.LineTo(textLeft - gap, middleY);
                ctx.MoveTo(textLeft + extents.Width + gap, middleY);
                ctx.LineTo(bounds.drawX + bounds.InnerWidth, middleY);
                ctx.Stroke();
            }
            ctx.SetSourceRGBA(font.Color);
            ctx.MoveTo(textLeft - extents.XBearing,
                middleY - extents.Height / 2 - extents.YBearing);
            ctx.ShowText(title);
        }
        finally { ctx.Restore(); }
    }
}
