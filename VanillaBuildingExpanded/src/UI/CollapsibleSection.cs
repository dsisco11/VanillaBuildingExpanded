using System;

using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.UI;

internal sealed class CollapsibleSection
{
    private const double LeftPadding = 12;
    private const double RightPadding = 24;
    private const double TitleHeight = 24;
    private const double TitleContentGap = 4;
    private const double ContentBottomPadding = 8;
    private const double DividerHeight = 1;
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
        return TitleHeight + TitleContentGap + contentHeight + ContentBottomPadding + DividerHeight + ButtonHeight;
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
        double dividerY = height - ButtonHeight - DividerHeight;
        composer.BeginChildElements(ElementBounds.Fixed(0, top, width, height))
            .AddStaticText(Lang.Get(titleKey),
                CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.ValueColor]),
                ElementBounds.Fixed(LeftPadding, 0, contentWidth, TitleHeight), $"{Id}-title");
        if (!IsCollapsed)
        {
            var contentBounds = ElementBounds.Fixed(LeftPadding, TitleHeight + TitleContentGap,
                contentWidth, dividerY - TitleHeight - TitleContentGap - ContentBottomPadding);
            composer.BeginChildElements(contentBounds);
            composeContent(composer, contentBounds);
            composer.EndChildElements();
        }
        composer.AddStaticCustomDraw(ElementBounds.Fixed(LeftPadding, dividerY, contentWidth, DividerHeight),
            (ctx, surface, bounds) =>
            {
                ctx.Save();
                try
                {
                    ctx.SetSourceRGBA([.. UiPalette.SectionDividerColor]);
                    ctx.Rectangle(bounds.drawX, bounds.drawY, bounds.InnerWidth, bounds.InnerHeight);
                    ctx.Fill();
                }
                finally
                {
                    ctx.Restore();
                }
            });
        ToggleButton = new GuiElementCollapseButton(api,
            ElementBounds.Fixed(0, dividerY + DividerHeight, width, ButtonHeight), IsCollapsed, RequestToggle);
        composer.AddInteractiveElement(ToggleButton, $"{Id}-toggle");
        composer.EndChildElements();
        return height;
    }
}
