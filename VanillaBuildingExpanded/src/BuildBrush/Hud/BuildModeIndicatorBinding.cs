using System;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Connects one session's local mode transitions to a lazily created HUD.</summary>
internal sealed class BuildModeIndicatorBinding : IDisposable
{
    private readonly BuildBrushControllerClient controller;
    private readonly Func<IBuildModeIndicator> createIndicator;
    private IBuildModeIndicator? indicator;
    private BuildBrushInstance? brush;
    private bool disposed;

    #region Public API
    /// <summary>Observes mode changes without creating a brush or GPU resources at startup.</summary>
    public BuildModeIndicatorBinding(BuildBrushControllerClient controller, Func<IBuildModeIndicator> createIndicator)
    {
        this.controller = controller;
        this.createIndicator = createIndicator;
        controller.ModeChanged += OnModeChanged;
        if (controller.State.IsActive) OnModeChanged();
    }

    /// <summary>Detaches before releasing resources so late mode changes cannot reopen the old HUD.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        controller.ModeChanged -= OnModeChanged;
        if (brush is not null)
        {
            brush.OnSnappingModeChanged -= OnInfoChanged;
            brush.OnRotationInfoChanged -= OnInfoChanged;
            brush.OnOrientationChanged -= OnInfoChanged;
        }
        indicator?.Dispose();
        indicator = null;
    }
    #endregion

    #region Private
    /// <summary>Uses mode alone; source eligibility and target selection do not affect this indicator.</summary>
    private void OnModeChanged()
    {
        if (disposed) return;
        bool active = controller.State.IsActive;
        if (active)
        {
            indicator ??= createIndicator();
            if (brush is null)
            {
                brush = controller.GetBrush();
                brush.OnSnappingModeChanged += OnInfoChanged;
                brush.OnRotationInfoChanged += OnInfoChanged;
                brush.OnOrientationChanged += OnInfoChanged;
            }
            RefreshInfo();
        }
        indicator?.SetVisible(active);
    }

    private void OnInfoChanged(object? sender, EventArgs args) => RefreshInfo();

    private void RefreshInfo()
    {
        if (!disposed && brush?.IsActive == true)
            indicator?.Update(BuildModeInfo.FromBrush(brush));
    }
    #endregion
}
