using System;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Determines when a handle's label is drawn.</summary>
public enum GizmoLabelVisibility
{
    /// <summary>Draw whenever the owning handle is rendered.</summary>
    Always,
    /// <summary>Draw only while the owning handle is hovered.</summary>
    Hover,
    /// <summary>Draw only while the owning handle is captured for interaction.</summary>
    Selected,
    /// <summary>Draw when the caller-supplied predicate returns true.</summary>
    Predicate
}

/// <summary>Live handle state supplied to a label's visibility predicate.</summary>
/// <param name="Gizmo">The owning gizmo.</param>
/// <param name="Handle">The handle carrying the label.</param>
/// <param name="Context">The current rendered camera and dimension snapshot.</param>
public readonly record struct GizmoLabelContext(Gizmo Gizmo, GizmoHandleId Handle, GizmoContext Context)
{
    /// <summary>Gets whether this particular handle is hovered.</summary>
    public bool IsHovered => Gizmo.HoveredHandle == Handle;
    /// <summary>Gets whether this particular handle is captured, rather than merely hovered.</summary>
    public bool IsSelected => Gizmo.CapturedHandle == Handle;
}

/// <summary>Immutable literal text and screen-space placement for one gizmo handle.</summary>
public sealed class GizmoLabel
{
    /// <summary>Gets the single-line literal label text.</summary>
    public string Text { get; }
    /// <summary>Gets the horizontal offset in framebuffer pixels, positive to the right.</summary>
    public double OffsetX { get; }
    /// <summary>Gets the vertical offset in framebuffer pixels, positive downward.</summary>
    public double OffsetY { get; }
    /// <summary>Gets the visibility policy, evaluated each rendered frame.</summary>
    public GizmoLabelVisibility Visibility { get; }
    /// <summary>Gets the custom visibility predicate, required only for Predicate mode.</summary>
    public Func<GizmoLabelContext, bool>? Predicate { get; }

    /// <summary>Creates a label whose top-left corner is offset from the projected handle anchor.</summary>
    /// <param name="text">Nonblank literal text without control characters.</param>
    /// <param name="offsetX">Horizontal framebuffer-pixel offset from the handle anchor.</param>
    /// <param name="offsetY">Vertical framebuffer-pixel offset from the handle anchor.</param>
    /// <param name="visibility">Visibility policy; Always is the default.</param>
    /// <param name="predicate">Custom, side-effect-free policy required in Predicate mode.</param>
    /// <exception cref="ArgumentException">Text or predicate does not satisfy the selected policy.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An offset is nonfinite or the policy is unsupported.</exception>
    public GizmoLabel(string text, double offsetX = 8, double offsetY = -8,
        GizmoLabelVisibility visibility = GizmoLabelVisibility.Always,
        Func<GizmoLabelContext, bool>? predicate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        foreach (char character in text)
            if (char.IsControl(character))
                throw new ArgumentException("Gizmo labels require single-line text without control characters.", nameof(text));
        if (!double.IsFinite(offsetX)) throw new ArgumentOutOfRangeException(nameof(offsetX));
        if (!double.IsFinite(offsetY)) throw new ArgumentOutOfRangeException(nameof(offsetY));
        if (!Enum.IsDefined(visibility)) throw new ArgumentOutOfRangeException(nameof(visibility));
        if ((visibility == GizmoLabelVisibility.Predicate) != (predicate is not null))
            throw new ArgumentException("A custom predicate is required only for Predicate visibility.", nameof(predicate));
        Text = text;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Visibility = visibility;
        Predicate = predicate;
    }

    /// <summary>Evaluates visibility without overriding the owning gizmo's eligibility.</summary>
    /// <param name="gizmo">The label's owning gizmo.</param>
    /// <param name="handle">The label's owning handle.</param>
    /// <param name="context">The current rendering context.</param>
    /// <returns>Whether the label's policy permits rendering in this context.</returns>
    public bool IsVisible(Gizmo gizmo, GizmoHandleId handle, GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(gizmo);
        ArgumentNullException.ThrowIfNull(context);
        if (!gizmo.IsEligible(context.Dimension)) return false;
        return Visibility switch
        {
            GizmoLabelVisibility.Always => true,
            GizmoLabelVisibility.Hover => gizmo.HoveredHandle == handle,
            GizmoLabelVisibility.Selected => gizmo.CapturedHandle == handle,
            GizmoLabelVisibility.Predicate => Predicate!(new(gizmo, handle, context)),
            _ => throw new InvalidOperationException("Unsupported gizmo label visibility.")
        };
    }
}
