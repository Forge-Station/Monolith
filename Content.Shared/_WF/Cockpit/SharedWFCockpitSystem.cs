using Content.Shared.Buckle.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Shared._WF.Cockpit;

/// <summary>Uses replicated piloting and buckle state to gate the cockpit presentation.</summary>
public sealed class SharedWFCockpitSystem : EntitySystem
{
    /// <summary>Requires the current helm and an enabled seat on the same live grid.</summary>
    public bool CanEnter(EntityUid? actor, EntityUid? console)
    {
        return actor is { } pilot && console is { } helm &&
            !TerminatingOrDeleted(pilot) && !TerminatingOrDeleted(helm) &&
            (!TryComp<MobStateComponent>(pilot, out var state) || state.CurrentState == MobState.Alive) &&
            TryComp<PilotComponent>(pilot, out var piloting) && piloting.Console == helm &&
            TryComp<BuckleComponent>(pilot, out var buckle) && buckle.BuckledTo is { } seat &&
            !TerminatingOrDeleted(seat) && HasComp<WFCockpitSeatComponent>(seat) &&
            TryComp<StrapComponent>(seat, out var strap) && strap.Enabled &&
            TryComp<TransformComponent>(seat, out var seatTransform) &&
            TryComp<TransformComponent>(helm, out var consoleTransform) &&
            seatTransform.GridUid is { } grid && grid == consoleTransform.GridUid;
    }
}
