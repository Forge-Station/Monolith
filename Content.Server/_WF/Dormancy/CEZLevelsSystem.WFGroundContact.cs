using Robust.Shared.Physics;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>Ticks between ground contact samples of a hull that isn't moving.</summary>
    private const int WfRestingContactTicks = 60;

    /// <summary>
    /// Whether this tick's ground contact sample can be skipped: a static or sleeping hull's footprint hasn't moved, so
    /// it is sampled every <see cref="WfRestingContactTicks"/> ticks, spread out by entity, instead of every tick.
    /// </summary>
    private bool WfSkipGroundContact(EntityUid grid)
    {
        if (!_physQuery.TryComp(grid, out var body) || body.BodyType != BodyType.Static && body.Awake)
            return false;

        return (_timing.CurTick.Value + (uint) grid.Id) % WfRestingContactTicks != 0;
    }
}
