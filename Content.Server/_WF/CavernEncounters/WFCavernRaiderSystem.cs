using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Stack;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.CavernEncounters;

/// <summary>Tunnel raiders run once badly hurt, away from whoever hurt them, and drop their scrap and cash when they die.</summary>
public sealed partial class WFCavernRaiderSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private WFCavernWalkerSystem _walker = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WoundDamageRoutingSystem _wounds = default!;

    /// <summary>A running raider picks a new spot this often, while whoever hurt him is near.</summary>
    private static readonly TimeSpan LookInterval = TimeSpan.FromSeconds(3);

    /// <summary>He keeps running while whoever hurt him is this close.</summary>
    private const float ThreatRange = 14f;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Before the wound system cancels the hit and loses who threw it.
        SubscribeLocalEvent<WFCavernRaiderComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            before: new[] { typeof(WoundDamageRoutingSystem) });
        SubscribeLocalEvent<WFCavernRaiderComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnBeforeDamage(Entity<WFCavernRaiderComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // The wound system's routed pass raises this again for the same hit.
        if (args.Cancelled || _wounds.IsRouting(ent) || !args.Damage.AnyPositive() || !_mobState.IsAlive(ent))
            return;

        ent.Comp.Taken += args.Damage.GetTotal().Float();
        if (args.Origin is { } source && source != ent.Owner && !TerminatingOrDeleted(source))
            ent.Comp.Attacker = _crew.Wielder(source);

        if (!ent.Comp.Fleeing && ent.Comp.Taken >= ent.Comp.FleeAt)
        {
            ent.Comp.Fleeing = true;
            Run(ent);
        }
    }

    private void OnMobStateChanged(Entity<WFCavernRaiderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || ent.Comp.Dropped)
            return;

        ent.Comp.Dropped = true;
        var coords = Transform(ent).Coordinates;
        foreach (var loot in ent.Comp.Loot)
        {
            Spawn(loot, coords);
        }

        var cash = _random.Next(ent.Comp.CashMin, ent.Comp.CashMax + 1);
        if (cash > 0)
            _stack.SpawnMultiple("SpaceCash", cash, coords);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFCavernRaiderComponent>();
        while (query.MoveNext(out var uid, out var raider))
        {
            if (!raider.Fleeing || now < raider.NextLook || !_mobState.IsAlive(uid))
                continue;

            if (raider.Attacker is { } attacker && !TerminatingOrDeleted(attacker)
                && Transform(attacker).MapUid == Transform(uid).MapUid
                && Vector2.Distance(_transform.GetWorldPosition(attacker), _transform.GetWorldPosition(uid)) <= ThreatRange)
                Run((uid, raider));
        }
    }

    /// <summary>Sends a raider running directly away from whoever last hurt him, or anywhere if nobody did.</summary>
    public void Run(Entity<WFCavernRaiderComponent> ent)
    {
        ent.Comp.NextLook = _timing.CurTime + LookInterval;

        var xform = Transform(ent);
        var here = _transform.GetWorldPosition(xform);
        var away = new Vector2(_random.NextFloat(-1f, 1f), _random.NextFloat(-1f, 1f));

        if (ent.Comp.Attacker is { } attacker && !TerminatingOrDeleted(attacker) && Transform(attacker).MapUid == xform.MapUid)
            away = here - _transform.GetWorldPosition(attacker);

        if (away.LengthSquared() < 0.01f)
            away = Vector2.UnitX;

        _walker.WalkTo(ent, xform.Coordinates.Offset(Vector2.Normalize(away) * ent.Comp.FleeDistance));
    }
}
