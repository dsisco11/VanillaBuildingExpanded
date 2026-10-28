using System;
using System.Collections.Generic;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>
/// Session-owned membership and capture. Adapters alone own native subscriptions and GPU state.
/// Removal detaches without disposing; manager disposal disposes its remaining instances.
/// </summary>
public sealed class GizmoManager : IDisposable
{
    private sealed class Entry(Gizmo gizmo, long insertionId)
    {
        public Gizmo Gizmo { get; } = gizmo;
        public long InsertionId { get; } = insertionId;
        public GizmoContext? RenderedContext { get; set; }
    }

    private readonly List<Entry> entries = [];
    private long nextInsertionId;
    private int transitionDepth;
    private long frameRevision;
    public bool IsDisposed { get; private set; }
    public int Count => entries.Count;
    public Gizmo? CapturedGizmo { get; private set; }
    public GizmoHandleId? CapturedHandle => CapturedGizmo?.CapturedHandle;

    public void Add(Gizmo gizmo)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(gizmo);
        ObjectDisposedException.ThrowIf(gizmo.IsDisposed, gizmo);
        if (ReferenceEquals(gizmo.Owner, this)) return;
        if (gizmo.Owner is not null)
            throw new InvalidOperationException("A gizmo can belong to only one session manager.");
        entries.Add(new(gizmo, checked(nextInsertionId++)));
        gizmo.Owner = this;
        gizmo.Invalidated += OnInvalidated;
        gizmo.InteractionEnded += OnInteractionEnded;
    }

    public void Remove(Gizmo gizmo)
    {
        ArgumentNullException.ThrowIfNull(gizmo);
        var entry = Find(gizmo);
        if (entry is null) return;
        transitionDepth++;
        try { gizmo.CancelInteraction(); }
        finally
        {
            try
            {
                if (Find(gizmo) == entry)
                {
                    entries.Remove(entry);
                    gizmo.Owner = null;
                    gizmo.Invalidated -= OnInvalidated;
                    gizmo.InteractionEnded -= OnInteractionEnded;
                    if (ReferenceEquals(CapturedGizmo, gizmo)) CapturedGizmo = null;
                    gizmo.HoveredHandle = null;
                }
            }
            finally { transitionDepth--; }
        }
    }

    public void Render(GizmoContext context)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (CapturedGizmo is { } captured && !captured.IsEligible(context.Dimension))
            Cancel();
        long revision = frameRevision;
        foreach (var entry in entries.ToArray()) entry.RenderedContext = null;
        foreach (var entry in entries.ToArray())
        {
            var gizmo = entry.Gizmo;
            if (Find(gizmo) != entry || !gizmo.IsEligible(context.Dimension)) continue;
            try { gizmo.Render(context); }
            catch
            {
                if (!gizmo.IsDisposed) gizmo.Enabled = false;
                throw;
            }
            if (IsDisposed || frameRevision != revision) return;
            if (!IsDisposed && Find(gizmo) == entry && gizmo.IsEligible(context.Dimension))
                entry.RenderedContext = context;
        }
    }

    public Gizmo? UpdateHover(GizmoPointer pointer)
    {
        Validate(pointer);
        if (transitionDepth != 0) return CapturedGizmo;
        transitionDepth++;
        try
        {
            ClearHover();
            if (CapturedGizmo is { } captured)
            {
                if (captured.IsEligible(pointer.Context.Dimension)) return captured;
                Cancel();
            }
            var best = Select(pointer);
            if (best is not { } target) return null;
            target.Entry.Gizmo.HoveredHandle = target.Hit.Handle;
            return IsCurrentHit(target, pointer.Context) ? target.Entry.Gizmo : null;
        }
        finally { transitionDepth--; }
    }

    public bool TryBegin(GizmoPointer pointer)
    {
        Validate(pointer);
        if (transitionDepth != 0 || CapturedGizmo is not null) return false;
        transitionDepth++;
        try
        {
            var best = Select(pointer);
            if (IsDisposed || CapturedGizmo is not null || best is not { } target
                || !CanHit(target.Entry, pointer.Context)) return false;
            var gizmo = target.Entry.Gizmo;
            ClearHover();
            if (!IsCurrentHit(target, pointer.Context)) return false;
            CapturedGizmo = gizmo;
            try
            {
                bool began = gizmo.BeginInteraction(target.Hit.Handle, pointer);
                if (!began || Find(gizmo) != target.Entry)
                {
                    if (ReferenceEquals(CapturedGizmo, gizmo)) CapturedGizmo = null;
                    gizmo.CancelInteraction();
                    return false;
                }
                return ReferenceEquals(CapturedGizmo, gizmo) && gizmo.IsInteracting;
            }
            catch
            {
                if (ReferenceEquals(CapturedGizmo, gizmo)) CapturedGizmo = null;
                gizmo.CancelInteraction();
                throw;
            }
        }
        finally { transitionDepth--; }
    }

    public void Update(GizmoPointer pointer)
    {
        Validate(pointer);
        if (CapturedGizmo is not { } gizmo) return;
        if (!gizmo.IsEligible(pointer.Context.Dimension)) { Cancel(); return; }
        gizmo.UpdateInteraction(pointer);
    }

    public void Complete()
    {
        if (CapturedGizmo is not { } gizmo) return;
        CapturedGizmo = null;
        transitionDepth++;
        try { gizmo.CompleteInteraction(); }
        finally { transitionDepth--; }
    }

    public void Cancel()
    {
        if (CapturedGizmo is not { } gizmo) return;
        CapturedGizmo = null;
        transitionDepth++;
        try { gizmo.CancelInteraction(); }
        finally { transitionDepth--; }
    }

    public void InvalidateFrame()
    {
        frameRevision++;
        foreach (var entry in entries) entry.RenderedContext = null;
        ClearHover();
        Cancel();
    }

    private (Entry Entry, GizmoHit Hit, GizmoHandle Handle)? Select(GizmoPointer pointer)
    {
        (Entry Entry, GizmoHit Hit, GizmoHandle Handle)? best = null;
        foreach (var entry in entries.ToArray())
        {
            var gizmo = entry.Gizmo;
            if (!CanHit(entry, pointer.Context)) continue;
            var hit = gizmo.HitTest(pointer);
            if (hit is not { } candidate || !CanHit(entry, pointer.Context)) continue;
            if (best is not { } previous || DepthKey(candidate) < DepthKey(previous.Hit)
                || (DepthKey(candidate) == DepthKey(previous.Hit)
                    && entry.InsertionId < previous.Entry.InsertionId))
                best = (entry, candidate, gizmo.Handles[candidate.Handle]);
        }
        return best is { } result && IsCurrentHit(result, pointer.Context) ? best : null;
    }

    private bool IsCurrentHit((Entry Entry, GizmoHit Hit, GizmoHandle Handle) hit, GizmoContext context) =>
        CanHit(hit.Entry, context) && hit.Handle.IsEligible(context.Dimension)
            && hit.Entry.Gizmo.Handles.TryGetValue(hit.Hit.Handle, out var member)
            && ReferenceEquals(hit.Handle, member);
    private static double DepthKey(GizmoHit hit) => Math.Floor(hit.Distance / 1e-6);
    private bool CanHit(Entry entry, GizmoContext context) => !IsDisposed
        && Find(entry.Gizmo) == entry && ReferenceEquals(entry.RenderedContext, context)
        && entry.Gizmo.IsEligible(context.Dimension);
    private Entry? Find(Gizmo gizmo) => entries.Find(entry => ReferenceEquals(entry.Gizmo, gizmo));

    private void OnInvalidated(Gizmo gizmo)
    {
        if (Find(gizmo) is { } entry) entry.RenderedContext = null;
        if (gizmo.IsDisposed) Remove(gizmo);
        else if (!gizmo.Visible || !gizmo.Enabled) gizmo.CancelInteraction();
    }

    private void OnInteractionEnded(Gizmo gizmo)
    {
        if (ReferenceEquals(CapturedGizmo, gizmo)) CapturedGizmo = null;
    }

    private void ClearHover()
    {
        foreach (var entry in entries.ToArray())
            if (Find(entry.Gizmo) == entry) entry.Gizmo.HoveredHandle = null;
    }

    private void Validate(GizmoPointer pointer)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        pointer.Validate();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        List<Exception> errors = [];
        try { Cancel(); }
        catch (Exception error) { errors.Add(error); }
        foreach (var entry in entries.ToArray())
        {
            try { Remove(entry.Gizmo); }
            catch (Exception error) { errors.Add(error); }
            try { entry.Gizmo.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0)
            throw new AggregateException("Gizmo session cleanup failed.", errors);
    }
}
