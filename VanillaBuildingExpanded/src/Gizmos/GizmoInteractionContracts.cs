using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Identity and ownership view implemented by the persistent handle, not a copied ID wrapper.</summary>
public interface IGizmoHandle
{
    /// <summary>Gets the stable identity used only within the owning gizmo.</summary>
    GizmoHandleId Id { get; }

    /// <summary>Gets the current owner, or null while detached.</summary>
    Gizmo? Owner { get; }
}

/// <summary>
/// Gesture protocol for a manager-selected target. Implementations neither subscribe to native input
/// nor acquire global capture; the manager grants and releases that authority.
/// </summary>
public interface IGizmoDragInputBehavior : IDisposable
{
    /// <summary>Gets whether an accepted session is active.</summary>
    bool IsInteracting { get; }

    /// <summary>Prepares a measurement session and initializes its edit scopes before reporting success.</summary>
    /// <param name="target">The eligible target selected by the manager.</param>
    /// <param name="pointer">The validated press pointer.</param>
    /// <param name="rejection">A reason code on rejection, for the existing notice/error path; otherwise null.</param>
    /// <returns>Whether initialization succeeded and the manager may publish the start notification.</returns>
    /// <remarks>
    /// A rejected or throwing initialization disposes its prepared session and unwinds initialized scopes.
    /// Active/reentrant begin attempts are rejected. No successful-start or completion event is emitted on failure.
    /// </remarks>
    bool TryBegin(IGizmoDragTarget target, GizmoPointer pointer, [NotNullWhen(false)] out string? rejection);

    /// <summary>Updates the active measurement session without retargeting the captured handle.</summary>
    /// <param name="pointer">The current validated pointer.</param>
    /// <param name="rejection">A reason code when no update is accepted; otherwise null.</param>
    /// <returns>Whether the update was accepted, including an unchanged constrained value.</returns>
    bool TryUpdate(GizmoPointer pointer, [NotNullWhen(false)] out string? rejection);

    /// <summary>Ends parameter modification and completes the accepted edit exactly once.</summary>
    /// <remarks>If parameter cleanup fails, cancel the owner edit instead of committing, release resources and propagate.</remarks>
    void Complete();

    /// <summary>Terminates capture, ends parameter bookkeeping and cancels the edit exactly once.</summary>
    /// <remarks>Cancellation, including disposal, restores before the manager's canceled notification.</remarks>
    void Cancel();
}

/// <summary>Replaceable picking for one owner, independent of movement measurement and value semantics.</summary>
public interface IGizmoHitTarget
{
    /// <summary>Gets the owner whose attached handles may be returned.</summary>
    Gizmo Owner { get; }

    /// <summary>Tests the current resolved geometry without changing capture or application state.</summary>
    /// <param name="pointer">The pointer in the same context as the geometry snapshot.</param>
    /// <returns>Eligible owned-handle hits; no hits when the target is not pickable.</returns>
    IEnumerable<GizmoHandleHit> HitTest(GizmoPointer pointer);

    /// <summary>Updates the handle's existing hover state without introducing separate ownership.</summary>
    /// <param name="handle">The attached handle receiving feedback.</param>
    /// <param name="hovered">Whether this handle is hovered.</param>
    void UpdateHover(IGizmoHandle handle, bool hovered);

    /// <summary>Updates the handle's existing captured state after the manager's lifecycle transition.</summary>
    /// <param name="handle">The attached handle receiving feedback.</param>
    /// <param name="interacting">Whether this handle is captured.</param>
    void UpdateInteracting(IGizmoHandle handle, bool interacting);
}

/// <summary>Supplies a logical spatial frame without depending on rendered vertices or handle IDs.</summary>
public interface IGizmoFrameSource
{
    /// <summary>Reads the current validated frame; a measurement session freezes it at preparation.</summary>
    /// <param name="context">The camera/dimension snapshot used by geometry or measurement.</param>
    /// <returns>A valid frame in the context's dimension.</returns>
    /// <exception cref="ArgumentException">The context cannot supply the requested frame.</exception>
    GizmoFrame GetFrame(GizmoContext context);
}

/// <summary>Pointer-to-parameter measurement definition, independent of hit shape and global gesture routing.</summary>
public interface IGizmoDragTarget
{
    /// <summary>Checks eligibility and initializes measurement without modifying parameter or application state.</summary>
    /// <param name="pointer">The validated press pointer.</param>
    /// <param name="session">The fresh, caller-owned capture session on success; otherwise null.</param>
    /// <param name="rejection">A reason code on rejection; otherwise null.</param>
    /// <returns>Whether measurement is usable. Failure releases all allocations made before returning.</returns>
    bool TryPrepare(GizmoPointer pointer, [NotNullWhen(true)] out IGizmoDragSession? session,
        [NotNullWhen(false)] out string? rejection);
}

/// <summary>
/// Capture-local solver state owned by the input behavior. Shared frame/parameter/value/edit targets
/// are borrowed configuration, not disposed by this session.
/// </summary>
public interface IGizmoDragSession : IDisposable
{
    /// <summary>Begins parameter modification, then the edit-state scope, without publishing a value.</summary>
    /// <param name="rejection">A reason code on rejection; otherwise null.</param>
    /// <returns>Whether both scopes are ready before the manager announces interaction start.</returns>
    /// <remarks>
    /// Failed initialization unwinds every initialized scope. Exceptions propagate after cleanup;
    /// they are not converted into successful edits. A disposed or terminal session cannot restart.
    /// </remarks>
    bool TryBegin([NotNullWhen(false)] out string? rejection);

    /// <summary>Measures the pointer and passes typed parameters to the configured parameter source.</summary>
    /// <param name="pointer">The current validated pointer.</param>
    /// <param name="rejection">A reason code for an invalid/rejected update; otherwise null.</param>
    /// <returns>Whether a parameter was accepted; rejection retains the last accepted measurement/value.</returns>
    bool TryUpdate(GizmoPointer pointer, [NotNullWhen(false)] out string? rejection);

    /// <summary>Ends parameter modification and completes the edit scope once, releasing capture-local resources.</summary>
    /// <remarks>Parameter cleanup failure cancels rather than commits the edit, and is propagated after cleanup.</remarks>
    void Complete();

    /// <summary>Ends parameter modification and cancels the edit scope once, releasing capture-local resources.</summary>
    /// <remarks>
    /// Clear active state before callbacks to prevent reentrant terminal operations. Disposal cancels
    /// unfinished work; terminal calls and repeated disposal are no-ops after cleanup.
    /// </remarks>
    void Cancel();
}

/// <summary>Typed parameter storage/interpretation and constraints, without pointer math or transaction commits.</summary>
/// <typeparam name="TParameter">The measurement type, such as double or GizmoPlaneParameter.</typeparam>
public interface IInteractionParameterSource<TParameter>
{
    /// <summary>Gets the current parameter, without substituting a second authoritative application value.</summary>
    TParameter Parameter { get; }

    /// <summary>Records the initial parameter/value relationship without publishing an application change.</summary>
    /// <remarks>Throwing initialization must unwind its own partial bookkeeping before propagating.</remarks>
    void BeginModification();

    /// <summary>Maps a measured parameter to a constrained candidate through the configured typed value source.</summary>
    /// <param name="parameter">The measured candidate.</param>
    /// <param name="rejection">A reason code on rejection; otherwise null.</param>
    /// <returns>Whether accepted; unchanged snapped values are successful no-ops.</returns>
    bool TrySetParameter(TParameter parameter, [NotNullWhen(false)] out string? rejection);

    /// <summary>Releases parameter bookkeeping only; does not commit, cancel or restore an application edit.</summary>
    void EndModification();
}

/// <summary>Typed read/write access to the existing notifying application-facing gizmo value.</summary>
/// <typeparam name="TValue">The immutable edited value type; it need not represent a transform.</typeparam>
public interface IGizmoValueSource<TValue>
{
    /// <summary>Gets or assigns the value through its normal notifying path, suppressing equal assignments.</summary>
    TValue Value { get; set; }
}

/// <summary>Coordinates the existing gizmo baseline and owner edit transaction, not a separate undo/value store.</summary>
public interface IGizmoEditStateTarget
{
    /// <summary>Begins the existing edit scope without publishing a preview.</summary>
    /// <param name="rejection">A reason code on rejection; otherwise null.</param>
    /// <returns>Whether the edit scope is ready; failure unwinds any partial initialization.</returns>
    bool TryBegin([NotNullWhen(false)] out string? rejection);

    /// <summary>Completes the existing transaction once without duplicating owner lifecycle callbacks.</summary>
    void Complete();

    /// <summary>Restores through the notifying value source and cancels the existing owner transaction once.</summary>
    /// <remarks>Restoration and owner cancellation finish before the public canceled notification.</remarks>
    void Cancel();
}

/// <summary>Two-dimensional measurement parameters, independent of world coordinates and transform types.</summary>
public readonly record struct GizmoPlaneParameter
{
    /// <summary>Gets the first plane-basis parameter.</summary>
    public double X { get; }

    /// <summary>Gets the second plane-basis parameter.</summary>
    public double Y { get; }

    /// <summary>Creates finite planar parameters.</summary>
    /// <param name="x">The first parameter.</param>
    /// <param name="y">The second parameter.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is not finite.</exception>
    public GizmoPlaneParameter(double x, double y)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        X = x;
        Y = y;
    }
}

/// <summary>Value-source adapter that keeps the existing gizmo setter as the only notifying storage path.</summary>
/// <typeparam name="TValue">The edited value type.</typeparam>
public sealed class GizmoValueSource<TValue> : IGizmoValueSource<TValue>
{
    /// <summary>The borrowed gizmo; this adapter neither subscribes to it nor owns its lifetime.</summary>
    private readonly Gizmo<TValue> gizmo;

    /// <summary>Creates a source without copying the authoritative value.</summary>
    /// <param name="gizmo">The existing gizmo whose setter supplies equality checks and notifications.</param>
    /// <exception cref="ArgumentNullException">The gizmo is null.</exception>
    public GizmoValueSource(Gizmo<TValue> gizmo)
    {
        ArgumentNullException.ThrowIfNull(gizmo);
        this.gizmo = gizmo;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">An assignment is attempted after the borrowed gizmo is disposed.</exception>
    public TValue Value { get => gizmo.Value; set => gizmo.Value = value; }
}
