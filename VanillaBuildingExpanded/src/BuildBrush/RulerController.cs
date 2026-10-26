using System;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class RulerController
{
    public RulerState State { get; private set; }

    public event Action<RulerState>? Changed;
    public event Action<RulerNotice>? NoticeRequested;

    public void SetContext(bool buildModeEnabled, bool playerAlive)
    {
        SetState(State with
        {
            IsBuildModeEnabled = buildModeEnabled,
            IsPlayerAlive = playerAlive,
            PickMode = buildModeEnabled && playerAlive ? State.PickMode : RulerPickMode.Idle
        });
    }

    public void SetVisibility(bool visible)
    {
        if (State.ShowRuler == visible) return;
        SetState(State with { ShowRuler = visible });
        if (visible && State.Bounds is null)
            NoticeRequested?.Invoke(RulerNotice.SetBothEndpoints);
    }

    public bool TogglePicking(RulerPickMode mode)
    {
        if (mode is not (RulerPickMode.PickingStart or RulerPickMode.PickingEnd))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Choose a start or end endpoint.");
        if (!State.CanPick) return false;
        var next = State.PickMode == mode ? RulerPickMode.Idle : mode;
        SetState(State with { PickMode = next });
        if (next != RulerPickMode.Idle)
            NoticeRequested?.Invoke(next == RulerPickMode.PickingStart
                ? RulerNotice.SelectStart : RulerNotice.SelectEnd);
        return true;
    }

    public void CancelPicking() => SetState(State with { PickMode = RulerPickMode.Idle });

    /// <summary>The world-facing caller validates coordinates without requiring loaded blocks or chunks.</summary>
    public bool TrySelect(BlockPos? position, bool isValidPosition)
    {
        if (!State.CanPick || State.PickMode == RulerPickMode.Idle) return false;
        if (position is null || !isValidPosition)
        {
            NoticeRequested?.Invoke(RulerNotice.AimAtBlock);
            return false;
        }

        var endpoint = new RulerEndpoint(position.X, position.Y, position.Z, position.dimension);
        var start = State.Start;
        var end = State.End;
        bool resetOpposite;
        if (State.PickMode == RulerPickMode.PickingStart)
        {
            start = endpoint;
            resetOpposite = end is { } other && other.Dimension != endpoint.Dimension;
            if (resetOpposite) end = null;
        }
        else
        {
            end = endpoint;
            resetOpposite = start is { } other && other.Dimension != endpoint.Dimension;
            if (resetOpposite) start = null;
        }

        SetState(State with { Start = start, End = end, PickMode = RulerPickMode.Idle });
        if (resetOpposite)
            NoticeRequested?.Invoke(RulerNotice.SetOtherEndpoint);
        return true;
    }

    private void SetState(RulerState next)
    {
        if (State == next) return;
        State = next;
        Changed?.Invoke(next);
    }
}
