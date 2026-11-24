using Robust.Shared.Player;

namespace Content.Server._WF.Lattice;

public sealed partial class WFLatticeSystem
{
    /// <summary>How much faster a carrier gains Drift for who is aboard: the largest multiplier anyone asks for.</summary>
    private float GainMultiplier(List<(ICommonSession Session, EntityUid Mob)> aboard)
    {
        var multiplier = 1f;
        foreach (var (_, mob) in aboard)
        {
            var ev = new WFDriftGainEvent();
            RaiseLocalEvent(mob, ref ev);
            multiplier = MathF.Max(multiplier, ev.Multiplier);
        }

        return multiplier;
    }
}

/// <summary>Raised on each player aboard a carrier that is gaining Drift, to scale the gain.</summary>
[ByRefEvent]
public record struct WFDriftGainEvent(float Multiplier = 1f);
