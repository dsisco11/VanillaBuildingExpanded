using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Independent invalidation categories for handle consumers.</summary>
[Flags]
public enum GizmoHandleInvalidation
{
    /// <summary>No cached data changed.</summary>
    None = 0,
    /// <summary>Redraw appearance or state-dependent label visibility.</summary>
    Visual = 1,
    /// <summary>Rebuild spatial drawing and default picking geometry.</summary>
    Geometry = 2,
    /// <summary>Refresh or retire label raster content.</summary>
    LabelTexture = 4,
    /// <summary>Recalculate label placement or visibility without rerasterization.</summary>
    LabelPlacement = 8,
    /// <summary>Interaction configuration changed after canceling any capture.</summary>
    Interaction = 16,
    /// <summary>Visibility or enabled state changed.</summary>
    Eligibility = 32
}

/// <summary>Persistent, single-owner control configuration; owns no native input or GPU resources.</summary>
public sealed class GizmoHandle : IGizmoHandle, IDisposable
{
    /// <summary>Validated state entries behind the read-only public view.</summary>
    private readonly Dictionary<EAppearenceState, AppearenceProperties> appearances = [];
    /// <summary>The optional immutable label definition.</summary>
    private GizmoLabel? label;
    /// <summary>The borrowed logical frame source.</summary>
    private IGizmoFrameSource? frameSource;
    /// <summary>The borrowed picking target.</summary>
    private IGizmoHitTarget? hitTarget;
    /// <summary>The borrowed measurement target with its typed bindings.</summary>
    private IGizmoDragTarget? interactionTarget;
    /// <summary>The local visibility flag.</summary>
    private bool visible = true;
    /// <summary>The local enabled flag.</summary>
    private bool enabled = true;

    /// <summary>Gets the stable owner-local identity, not an operation selector.</summary>
    public GizmoHandleId Id { get; }
    /// <summary>Gets the owner, or null while detached.</summary>
    public Gizmo? Owner { get; private set; }
    /// <summary>Gets whether this instance has released its configuration/subscriptions.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Gets the protected state-to-appearance map.</summary>
    public IReadOnlyDictionary<EAppearenceState, AppearenceProperties> Appearances { get; }
    /// <summary>Gets whether this exact instance is hovered.</summary>
    public bool IsHovered => ReferenceEquals(Owner?.HoveredHandleInstance, this);
    /// <summary>Gets whether this exact instance is captured.</summary>
    public bool IsSelected => ReferenceEquals(Owner?.CapturedHandleInstance, this);
    /// <summary>Gets the active appearance state, with capture taking precedence over hover.</summary>
    public EAppearenceState AppearanceState => IsSelected ? EAppearenceState.Selected
        : IsHovered ? EAppearenceState.Hover : EAppearenceState.Default;
    /// <summary>Gets complete active styling; no caller-side nullable fallback is needed.</summary>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public ResolvedAppearenceProperties Appearance => ResolveAppearance(AppearanceState);

    /// <summary>Notifies consumers after a validated configuration or state transition.</summary>
    public event Action<GizmoHandle, GizmoHandleInvalidation>? Invalidated;

    /// <summary>Creates a detached handle with a complete baseline for its shape and optional label.</summary>
    /// <param name="id">The stable owner-local identity.</param>
    /// <param name="baseline">All required Default fields.</param>
    /// <param name="label">Optional label requiring a complete baseline label style.</param>
    /// <exception cref="ArgumentException">The baseline is incomplete or invalid.</exception>
    public GizmoHandle(GizmoHandleId id, AppearenceProperties baseline, GizmoLabel? label = null)
    {
        baseline.ResolveOver(default, label is not null);
        Id = id;
        this.label = label;
        appearances.Add(EAppearenceState.Default, baseline);
        Appearances = new ReadOnlyDictionary<EAppearenceState, AppearenceProperties>(appearances);
    }

    /// <summary>Gets or sets local visibility; hiding cancels before the mutation.</summary>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public bool Visible
    {
        get => visible;
        set
        {
            ThrowIfDisposed();
            if (visible == value) return;
            if (!value) CancelCapture();
            ThrowIfDisposed();
            visible = value;
            if (!value) Owner?.ClearHandleHover(this);
            Notify(GizmoHandleInvalidation.Visual | GizmoHandleInvalidation.Eligibility
                | GizmoHandleInvalidation.LabelPlacement);
        }
    }

    /// <summary>Gets or sets local interaction eligibility; disabling cancels before the mutation.</summary>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public bool Enabled
    {
        get => enabled;
        set
        {
            ThrowIfDisposed();
            if (enabled == value) return;
            if (!value) CancelCapture();
            ThrowIfDisposed();
            enabled = value;
            if (!value) Owner?.ClearHandleHover(this);
            Notify(GizmoHandleInvalidation.Visual | GizmoHandleInvalidation.Eligibility
                | GizmoHandleInvalidation.LabelPlacement);
        }
    }

    /// <summary>Gets or replaces label content after validating every state's required style.</summary>
    /// <exception cref="ArgumentException">The baseline label style is incomplete.</exception>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public GizmoLabel? Label
    {
        get => label;
        set
        {
            ThrowIfDisposed();
            if (ReferenceEquals(label, value)) return;
            ValidateAppearances(appearances, value is not null);
            var before = Appearance;
            string? oldText = label?.Text;
            label = value;
            var change = Difference(before, Appearance) | GizmoHandleInvalidation.Visual
                | GizmoHandleInvalidation.LabelPlacement;
            if (oldText != label?.Text) change |= GizmoHandleInvalidation.LabelTexture;
            Notify(change);
        }
    }

    /// <summary>Gets or replaces the logical frame source, canceling capture before replacement.</summary>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public IGizmoFrameSource? FrameSource
    {
        get => frameSource;
        set => Configure(value, hitTarget, interactionTarget);
    }

    /// <summary>Gets or replaces the owner-specific picking target.</summary>
    /// <exception cref="ArgumentException">The target belongs to another owner.</exception>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public IGizmoHitTarget? HitTarget
    {
        get => hitTarget;
        set => Configure(frameSource, value, interactionTarget);
    }

    /// <summary>Gets or replaces the measurement target with its typed parameter/value/edit bindings.</summary>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public IGizmoDragTarget? InteractionTarget
    {
        get => interactionTarget;
        set => Configure(frameSource, hitTarget, value);
    }

    /// <summary>Checks local and owner eligibility without overriding dimension or disposal gates.</summary>
    /// <param name="dimension">The active context's dimension.</param>
    /// <returns>Whether this attached handle may be interacted with.</returns>
    public bool IsEligible(int dimension) => !IsDisposed && visible && enabled
        && Owner is { } owner && owner.IsEligible(dimension);

    /// <summary>Adds/replaces an override atomically after validating every resulting state.</summary>
    /// <param name="state">The state to configure.</param>
    /// <param name="properties">Nullable overrides, or the complete Default baseline.</param>
    /// <exception cref="ArgumentException">A state or resulting appearance is invalid/incomplete.</exception>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public void SetAppearance(EAppearenceState state, AppearenceProperties properties)
    {
        ThrowIfDisposed();
        ValidateState(state);
        if (appearances.TryGetValue(state, out var previous) && previous == properties) return;
        var proposed = new Dictionary<EAppearenceState, AppearenceProperties>(appearances) { [state] = properties };
        ValidateAppearances(proposed, label is not null);
        var before = Appearance;
        appearances[state] = properties;
        Notify(Difference(before, Appearance) | GizmoHandleInvalidation.Visual);
    }

    /// <summary>Removes an override so that state inherits Default; Default itself cannot be removed.</summary>
    /// <param name="state">The non-default state to remove.</param>
    /// <returns>Whether an override was removed.</returns>
    /// <exception cref="ArgumentException">The state is invalid or is Default.</exception>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public bool RemoveAppearance(EAppearenceState state)
    {
        ThrowIfDisposed();
        ValidateState(state);
        if (state == EAppearenceState.Default)
            throw new ArgumentException("Default appearance cannot be removed.", nameof(state));
        if (!appearances.ContainsKey(state)) return false;
        var before = Appearance;
        appearances.Remove(state);
        Notify(Difference(before, Appearance) | GizmoHandleInvalidation.Visual);
        return true;
    }

    /// <summary>Resolves each property directly against Default, including for absent state entries.</summary>
    /// <param name="state">The desired appearance state.</param>
    /// <returns>A validated nonnullable snapshot with label style only when a label exists.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The state is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public ResolvedAppearenceProperties ResolveAppearance(EAppearenceState state)
    {
        ThrowIfDisposed();
        ValidateState(state);
        appearances.TryGetValue(state, out var active);
        return active.ResolveOver(appearances[EAppearenceState.Default], label is not null);
    }

    /// <summary>Replaces borrowed interaction configuration atomically, after canceling an active capture.</summary>
    /// <param name="frame">The logical frame source, or null during construction.</param>
    /// <param name="hit">The owner-specific picking target, or null during construction.</param>
    /// <param name="interaction">The measurement target with its typed bindings, or null during construction.</param>
    /// <exception cref="ArgumentException">The hit target belongs to another owner.</exception>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    public void Configure(IGizmoFrameSource? frame, IGizmoHitTarget? hit, IGizmoDragTarget? interaction)
    {
        ThrowIfDisposed();
        if (ReferenceEquals(frameSource, frame) && ReferenceEquals(hitTarget, hit)
            && ReferenceEquals(interactionTarget, interaction)) return;
        ValidateHitOwner(hit, Owner);
        CancelCapture();
        ThrowIfDisposed();
        ValidateHitOwner(hit, Owner);
        bool frameChanged = !ReferenceEquals(frameSource, frame);
        frameSource = frame;
        hitTarget = hit;
        interactionTarget = interaction;
        Notify(GizmoHandleInvalidation.Interaction | (frameChanged
            ? GizmoHandleInvalidation.Geometry | GizmoHandleInvalidation.Visual | GizmoHandleInvalidation.LabelPlacement
            : GizmoHandleInvalidation.None));
    }

    /// <summary>Validates ownership-specific configuration before an owner publishes attachment.</summary>
    /// <param name="owner">The proposed owner.</param>
    /// <exception cref="ArgumentException">A hit target belongs to another owner.</exception>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    internal void ValidateAttachment(Gizmo owner)
    {
        ThrowIfDisposed();
        ValidateHitOwner(hitTarget, owner);
        ValidateAppearances(appearances, label is not null);
    }

    /// <summary>Sets ownership after the owner's collection has accepted this instance.</summary>
    /// <param name="owner">The owner, or null on detachment.</param>
    internal void SetOwner(Gizmo? owner) => Owner = owner;

    /// <summary>Invalidates only data affected by a hover/capture transition, without canceling it.</summary>
    /// <param name="previous">The state before the owner changed its actual-handle reference.</param>
    internal void StateChanged(EAppearenceState previous)
    {
        if (!IsDisposed && previous != AppearanceState)
            Notify(Difference(ResolveAppearance(previous), Appearance) | GizmoHandleInvalidation.Visual
                | GizmoHandleInvalidation.LabelPlacement);
    }

    /// <summary>Cancels capture before detaching, then releases configuration and subscribers once.</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        try { Owner?.RemoveHandle(this); }
        finally
        {
            frameSource = null;
            hitTarget = null;
            interactionTarget = null;
            label = null;
            appearances.Clear();
            Invalidated = null;
        }
    }

    /// <summary>Validates a complete baseline and every independently merged state before publication.</summary>
    /// <param name="states">The proposed map.</param>
    /// <param name="hasLabel">Whether label fields are required.</param>
    /// <exception cref="ArgumentException">A state or required field is invalid.</exception>
    private static void ValidateAppearances(Dictionary<EAppearenceState, AppearenceProperties> states, bool hasLabel)
    {
        var baseline = states[EAppearenceState.Default];
        baseline.ResolveOver(default, hasLabel);
        foreach (var (state, properties) in states)
        {
            ValidateState(state);
            properties.ResolveOver(baseline, hasLabel);
        }
    }

    /// <summary>Rejects unsupported appearance-state values.</summary>
    /// <param name="state">The proposed state.</param>
    /// <exception cref="ArgumentOutOfRangeException">The state is unsupported.</exception>
    private static void ValidateState(EAppearenceState state)
    {
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
    }

    /// <summary>Checks the target's owner without mutating configuration.</summary>
    /// <param name="target">The optional target.</param>
    /// <param name="owner">The proposed/current owner, or null while detached.</param>
    /// <exception cref="ArgumentException">An attached target belongs to another gizmo.</exception>
    private static void ValidateHitOwner(IGizmoHitTarget? target, Gizmo? owner)
    {
        if (target is not null && owner is not null && !ReferenceEquals(target.Owner, owner))
            throw new ArgumentException("A handle's hit target must belong to its owner.", nameof(target));
    }

    /// <summary>Classifies resolved visual changes without rebuilding geometry for color-only edits.</summary>
    /// <param name="before">The preceding snapshot.</param>
    /// <param name="after">The new snapshot.</param>
    /// <returns>The spatial/raster categories affected by the change.</returns>
    private static GizmoHandleInvalidation Difference(ResolvedAppearenceProperties before, ResolvedAppearenceProperties after)
    {
        var change = before == after ? GizmoHandleInvalidation.None : GizmoHandleInvalidation.Visual;
        if (before.Shape != after.Shape || before.Size != after.Size)
            change |= GizmoHandleInvalidation.Geometry | GizmoHandleInvalidation.LabelPlacement;
        if (before.LabelStyle != after.LabelStyle)
            change |= GizmoHandleInvalidation.LabelTexture;
        return change;
    }

    /// <summary>Uses the existing owner cancellation path, preserving its value/notification ordering.</summary>
    private void CancelCapture()
    {
        if (IsSelected) Owner!.CancelInteraction();
    }

    /// <summary>Rejects mutation or resolution after disposal.</summary>
    /// <exception cref="ObjectDisposedException">This handle is disposed.</exception>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    /// <summary>Publishes invalidation after state/configuration is coherent.</summary>
    /// <param name="change">The affected cache/interaction categories.</param>
    private void Notify(GizmoHandleInvalidation change) => Invalidated?.Invoke(this, change);
}
