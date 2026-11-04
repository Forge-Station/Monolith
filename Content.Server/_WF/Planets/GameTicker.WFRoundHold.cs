using Content.Server._WF.Planets;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    /// <summary>True while something, such as a round-start world still forming, holds the round start back.</summary>
    private bool WFRoundStartHeld(bool force)
    {
        var ev = new WFRoundStartHoldEvent(force);
        RaiseLocalEvent(ref ev);
        return ev.Hold;
    }
}
