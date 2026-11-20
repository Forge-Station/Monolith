using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Traders;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Traders;

/// <summary>
/// A killable trader has no weapon and never fights. Hurt, he stops trading with whoever did it and runs to the
/// far end of his own ship, then keeps away from them for as long as they stay near.
/// </summary>
public sealed partial class TraderFleeSystem : EntitySystem
{
    /// <summary>
    /// Blackboard key the trader's HTN reads its destination from.
    /// </summary>
    public const string TargetKey = "WFTraderFleeTarget";

    /// <summary>
    /// Within this he moves again when the attacker comes closer, beyond it he stays where he is.
    /// </summary>
    private const float NearRange = 10f;

    /// <summary>
    /// Beyond this the attacker no longer counts, once the calm delay has run out.
    /// </summary>
    private const float CalmRange = 25f;

    /// <summary>
    /// How close to its tile a run is over. A little wider than the HTN's own movement range.
    /// </summary>
    private const float ArriveRange = 2.5f;

    /// <summary>
    /// How much each tile of running costs a destination against each tile it puts between him and the attacker.
    /// </summary>
    private const float TravelWeight = 0.35f;

    /// <summary>
    /// How far, in tiles each way, he looks for somewhere to run.
    /// </summary>
    private const float FleeReach = 48f;

    private static readonly TimeSpan ThinkInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StuckTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CalmDelay = TimeSpan.FromSeconds(20);

    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TraderSystem _trader = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private WFCrewSystem _crew = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Before the wound system cancels the hit and loses who threw it, and after crew friendly fire has
        // had its say on whether the hit lands at all.
        SubscribeLocalEvent<TraderComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            before: [typeof(WoundDamageRoutingSystem)],
            after: [typeof(WFCrewFriendlyFireSystem)]);
    }

    private void OnBeforeDamage(Entity<TraderComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!ent.Comp.Killable || args.Cancelled || !args.Damage.AnyPositive()
            || args.Origin is not { } source || source == ent.Owner
            || TerminatingOrDeleted(source) || !_mobState.IsAlive(ent))
            return;

        // A beam names the gun as its origin; the attacker is whoever holds it.
        var attacker = _crew.Wielder(source);
        if (attacker == ent.Owner || TerminatingOrDeleted(attacker))
            return;

        Spook(ent, attacker, true);
    }

    /// <summary>
    /// Sends him running from <paramref name="threat"/>. One who <paramref name="hurt"/> him is never traded with again;
    /// one who only frightened him is.
    /// </summary>
    public void Spook(Entity<TraderComponent> ent, EntityUid threat, bool hurt)
    {
        var flee = EnsureComp<TraderFleeComponent>(ent);
        if (hurt)
            flee.Attackers.Add(threat);
        flee.Threat = threat;
        flee.HurtAt = _timing.CurTime;
        flee.CalmAt = null;
        flee.NextThink = TimeSpan.Zero;

        // Whoever he was serving, he is done.
        if (ent.Comp.Customer != null)
            _trader.EndConversation(ent, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TraderFleeComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var flee, out var htn))
        {
            if (flee.Threat == null || now < flee.NextThink)
                continue;

            flee.NextThink = now + ThinkInterval;
            Think(uid, flee, htn, now);
        }
    }

    /// <summary>
    /// Works out whether the trader is still running, where to, and whether he has to pick somewhere new.
    /// </summary>
    private void Think(EntityUid uid, TraderFleeComponent flee, HTNComponent htn, TimeSpan now)
    {
        if (flee.Threat is not { } threat || _mobState.IsDead(uid))
        {
            Calm(flee, htn);
            return;
        }

        // Down, he cannot run: drop the route and take it up again on his feet.
        if (!_mobState.IsAlive(uid))
        {
            ClearTarget(flee, htn);
            return;
        }

        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            ClearTarget(flee, htn);
            return;
        }

        // Dead, gone or downed, the attacker is no threat.
        if (TerminatingOrDeleted(threat) || HasComp<MobStateComponent>(threat) && !_mobState.IsAlive(threat))
        {
            Calm(flee, htn);
            return;
        }

        var traderWorld = _transform.GetWorldPosition(xform);
        var threatXform = Transform(threat);
        var distance = float.MaxValue;
        var threatWorld = Vector2.Zero;
        if (threatXform.MapID == xform.MapID)
        {
            threatWorld = _transform.GetWorldPosition(threatXform);
            distance = (threatWorld - traderWorld).Length();
        }

        // Far off: wait a while before relaxing, in case they come back.
        if (distance > CalmRange)
        {
            flee.CalmAt ??= now + CalmDelay;
            if (now >= flee.CalmAt)
                Calm(flee, htn);

            return;
        }

        flee.CalmAt = null;
        if (!HasComp<ActiveNPCComponent>(uid))
            _npc.WakeNPC(uid, htn);

        if (flee.Target is { } target)
        {
            if (xform.Coordinates.TryDistance(EntityManager, target, out var left) && left <= ArriveRange)
            {
                ClearTarget(flee, htn);
            }
            else if (!htn.Blackboard.ContainsKey(TargetKey))
            {
                // The route fell through: that tile is not coming back.
                flee.Rejected.Add(flee.TargetTile);
                ClearTarget(flee, htn);
            }
            else if ((_map.GridTileToWorldPos(grid, gridComp, flee.TargetTile) - threatWorld).Length() < distance)
            {
                // They have got between him and where he was going.
                ClearTarget(flee, htn);
            }
            else if ((xform.LocalPosition - flee.TargetFrom).Length() >= 1f)
            {
                flee.TargetFrom = xform.LocalPosition;
                flee.TargetSince = now;
            }
            else if (now - flee.TargetSince > StuckTime)
            {
                flee.Rejected.Add(flee.TargetTile);
                ClearTarget(flee, htn);
            }
        }

        // Just hurt, he runs however far off the shot came from.
        if (flee.Target == null && (distance <= NearRange || now - flee.HurtAt < TimeSpan.FromSeconds(8)))
            PickTarget(uid, flee, htn, grid, gridComp, traderWorld, threatWorld, distance, now);
    }

    /// <summary>
    /// Sends him to the safe tile of his ship that is farthest from the attacker, preferring ones he can
    /// reach without running past them. Does nothing when no tile is farther from them than he already is.
    /// </summary>
    private void PickTarget(EntityUid uid,
        TraderFleeComponent flee,
        HTNComponent htn,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2 traderWorld,
        Vector2 threatWorld,
        float distance,
        TimeSpan now)
    {
        var scored = new List<(Vector2i Tile, float Score)>();
        // Only the tiles round him: a planet's ground is one grid of hundreds of thousands of tiles.
        var local = Transform(uid).LocalPosition;
        var reach = new Box2(local - new Vector2(FleeReach), local + new Vector2(FleeReach));
        var tiles = _map.GetLocalTilesIntersecting(grid, gridComp, reach);
        while (tiles.MoveNext(out var tile))
        {
            var indices = tile.GridIndices;
            if (flee.Rejected.Contains(indices))
                continue;

            var world = _map.GridTileToWorldPos(grid, gridComp, indices);
            var away = (world - threatWorld).Length();
            if (away < distance + 1f)
                continue;

            scored.Add((indices, away - TravelWeight * (world - traderWorld).Length()));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        foreach (var (indices, _) in scored)
        {
            if (!_planner.IsSafePost(grid, indices, gridComp))
                continue;

            var coordinates = _map.GridTileToLocal(grid, gridComp, indices);
            flee.Target = coordinates;
            flee.TargetTile = indices;
            flee.TargetSince = now;
            flee.TargetFrom = Transform(uid).LocalPosition;
            _npc.SetBlackboard(uid, TargetKey, coordinates, htn);

            // A run already under way carries on to the new tile; an idle trader starts one on the next plan.
            if (TryComp<NPCSteeringComponent>(uid, out var steering))
                _steering.Register(uid, coordinates, steering);

            _htn.Replan(htn);
            return;
        }
    }

    private static void ClearTarget(TraderFleeComponent flee, HTNComponent htn)
    {
        flee.Target = null;
        htn.Blackboard.Remove<EntityCoordinates>(TargetKey);
    }

    /// <summary>
    /// Stops the run and forgets the threat. Who hurt him is remembered.
    /// </summary>
    private static void Calm(TraderFleeComponent flee, HTNComponent htn)
    {
        ClearTarget(flee, htn);
        flee.Threat = null;
        flee.CalmAt = null;
        flee.Rejected.Clear();
    }
}
