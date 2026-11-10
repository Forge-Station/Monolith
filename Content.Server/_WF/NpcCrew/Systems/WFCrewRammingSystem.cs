using Content.Server._WF.NpcCrew.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Counts a ship rammed at speed as attacked by the ship that hit it. A bump is nobody's attack.</summary>
public sealed partial class WFCrewRammingSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewSystem _crew = default!;

    /// <summary>The closing speed, in metres a second, from which a collision is a ramming.</summary>
    public const float RamSpeed = 8f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipCollisionEvent>(OnCollision);
    }

    private void OnCollision(ref WFShipCollisionEvent args)
    {
        // The faster ship did the ramming.
        if (args.Speed > args.OtherSpeed)
            Ram(args.Other, args.Grid, args.ClosingSpeed);
        else
            Ram(args.Grid, args.Other, args.ClosingSpeed);
    }

    /// <summary>Reports a collision to the struck ship's crew, if it was hard enough to be a ramming.</summary>
    public bool Ram(EntityUid struck, EntityUid rammer, float closingSpeed)
    {
        if (closingSpeed < RamSpeed || !_escorts.IsInvolved(struck) || _escorts.AreInFormation(struck, rammer))
            return false;

        // A crew at its own helm doesn't ram on purpose: its clumsiness starts no fights.
        var query = EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out var uid, out var crew))
        {
            if (_crew.HomeGrid(uid, crew) == rammer && _mobState.IsAlive(uid) && !HasComp<ActorComponent>(uid))
                return false;
        }

        var ev = new WFCrewHullHitEvent(struck, rammer);
        RaiseLocalEvent(ref ev);
        return true;
    }
}
