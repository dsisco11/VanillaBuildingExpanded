using System;
using System.Diagnostics.CodeAnalysis;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Owns one manager-authorized gesture session, without input subscriptions or measurement math.</summary>
public sealed class GizmoDragInputBehavior : IGizmoDragInputBehavior
{
    private IGizmoDragSession? session;
    private bool beginning, ending, stopped, disposed;

    /// <inheritdoc />
    public bool IsInteracting => session is not null && !beginning && !ending;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">The target is null.</exception>
    /// <exception cref="ArgumentException">The pointer is invalid.</exception>
    /// <exception cref="ObjectDisposedException">This behavior is disposed.</exception>
    public bool TryBegin(IGizmoDragTarget target, GizmoPointer pointer,
        [NotNullWhen(false)] out string? rejection)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        pointer.Validate();
        rejection = "gesture-active-or-busy";
        if (session is not null || beginning || ending) return false;
        beginning = true;
        stopped = false;
        bool accepted = false;
        try
        {
            if (!target.TryPrepare(pointer, out var prepared, out rejection)) return false;
            session = prepared;
            if (stopped || disposed) { rejection = "gesture-terminated"; return false; }
            if (!prepared.TryBegin(out rejection)) return false;
            if (stopped || disposed) { rejection = "gesture-terminated"; return false; }
            accepted = true;
            return true;
        }
        finally
        {
            try { if (!accepted) End(false); }
            finally { beginning = false; }
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The pointer is invalid.</exception>
    /// <exception cref="ObjectDisposedException">This behavior is disposed.</exception>
    public bool TryUpdate(GizmoPointer pointer, [NotNullWhen(false)] out string? rejection)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        pointer.Validate();
        rejection = "gesture-inactive";
        return IsInteracting && session!.TryUpdate(pointer, out rejection);
    }

    /// <inheritdoc />
    public void Complete()
    {
        if (beginning) { Cancel(); return; }
        End(true);
    }

    /// <inheritdoc />
    public void Cancel()
    {
        if (beginning && !ending)
        {
            if (stopped) return;
            stopped = true;
            session?.Cancel();
            return;
        }
        End(false);
    }

    private void End(bool complete)
    {
        if (ending || session is not { } current) return;
        session = null;
        ending = true;
        try
        {
            try
            {
                if (complete) current.Complete();
                else current.Cancel();
            }
            finally { current.Dispose(); }
        }
        finally { ending = false; }
    }

    /// <summary>Cancels unfinished work once; shared target configuration is borrowed, not disposed.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Cancel();
    }
}
