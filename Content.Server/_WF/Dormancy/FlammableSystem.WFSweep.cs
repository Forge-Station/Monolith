namespace Content.Server.Atmos.EntitySystems;

public sealed partial class FlammableSystem
{
    /// <summary>How often the per-entity sweep runs; each entity still acts once a second, at most this much late.</summary>
    private static readonly TimeSpan WfSweepEvery = TimeSpan.FromSeconds(0.1);

    private TimeSpan _wfNextSweep;

    /// <summary>
    /// Whether this tick's sweep over every flammable entity can be skipped. Planets hold thousands of flammable
    /// weeds, trees and walls that act once a second, so walking all of them every tick cost more than the work.
    /// </summary>
    private bool WfSkipSweep()
    {
        var now = _timing.CurTime;
        if (now < _wfNextSweep)
            return true;

        _wfNextSweep = now + WfSweepEvery;
        return false;
    }
}
