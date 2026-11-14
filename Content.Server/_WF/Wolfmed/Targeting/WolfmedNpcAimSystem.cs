using Content.Server.NPC.HTN;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Targeting;

/// <summary>
/// NPCs pick a new body part to aim at every second, by their targeting odds. Only players ever changed the aim, so
/// every mob hit the torso, and limbs, heads and the wounds they take were never reached.
/// </summary>
public sealed class WolfmedNpcAimSystem : EntitySystem
{
    /// <summary>How often an NPC's aim moves to another part.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedBodySystem _body = default!;

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;

        var query = EntityQueryEnumerator<HTNComponent, TargetingComponent>();
        while (query.MoveNext(out var uid, out _, out var targeting))
        {
            if (HasComp<ActorComponent>(uid))
                continue;

            if (_body.GetRandomBodyPart(uid, targeting) is { } part && SharedTargetingSystem.IsSelectable(part))
                targeting.Target = part;
        }
    }
}
