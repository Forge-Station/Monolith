using Content.Shared._NF.Emp.Components;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using Robust.Shared.Log;

namespace Content.Shared._NF.Emp.Systems;

public sealed partial class EmpBlastSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EmpBlastComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, EmpBlastComponent component, ComponentStartup args)
    {
        component.StartTime = _timing.RealTime;

        /// Forge-Change-Start
        if (TryComp<EmpBlastRangeComponent>(uid, out var rangeComp))
        {
            component.VisualRange = rangeComp.Range;
        }
        /// Forge-Change-End

        // try to get despawn time or keep default duration time
        if (TryComp<TimedDespawnComponent>(uid, out var despawn))
        {
            component.VisualDuration = despawn.Lifetime;
        }
    }
}
