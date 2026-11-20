using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Director;
using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Variants;

/// <summary>
/// A Remade that is steering for a target but gets nowhere attacks what is in its way, with its normal melee, until it breaks.
/// The pathfinder can't see a railing (it samples each tile on a quarter grid and the rail is thinner than the gap), so steering
/// walks a Remade straight into one and never runs its own climb or smash; this covers that and anything else the navmesh missed.
/// </summary>
public sealed class WFStuckBreakerSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(30);
    private static readonly float[] Sides = { 0f, 0.3f, -0.3f };

    /// <summary>A target nearer than this is a fight, not a blockage.</summary>
    private const float MinGoal = 2f;

    private const int MaxWasted = 4;

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        var query = EntityQueryEnumerator<WFStuckBreakerComponent, ActiveNPCComponent, NPCSteeringComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out _, out var steering, out var xform))
        {
            Check(uid, comp, steering, xform, now);
        }
    }

    private void Check(EntityUid uid, WFStuckBreakerComponent comp, NPCSteeringComponent steering, TransformComponent xform, TimeSpan now)
    {
        var pos = _transform.GetWorldPosition(xform);
        var rested = now - comp.Seen > TimeSpan.FromSeconds(1);
        comp.Seen = now;

        if (rested
            || steering.Status != SteeringStatus.Moving
            || steering.DoAfterId != null
            || steering.DirectMove
            || _mobState.IsIncapacitated(uid)
            || HasComp<WFRemadePlayingDeadComponent>(uid)
            || !_blocker.CanMove(uid)
            || (pos - comp.Anchor).Length() >= comp.MoveDistance)
        {
            comp.Anchor = pos;
            comp.Since = now;
            comp.Working = null;
            comp.Wasted = 0;
            return;
        }

        var stuck = (float) (now - comp.Since).TotalSeconds;
        if (stuck < comp.StuckTime)
            return;

        var goal = _transform.ToMapCoordinates(steering.Coordinates);
        if (goal.MapId != xform.MapID || (goal.Position - pos).Length() < MinGoal)
            return;

        var obstacle = Find(uid, comp, xform, pos, goal.Position, steering, stuck - comp.StuckTime >= comp.WallTime);
        if (obstacle == null)
            return;

        Attack(uid, comp, obstacle.Value, now);
    }

    /// <summary>The nearest breakable thing in line with where it is heading. Walls come last, and only for those that break them.</summary>
    private EntityUid? Find(EntityUid uid, WFStuckBreakerComponent comp, TransformComponent xform, Vector2 pos, Vector2 goal, NPCSteeringComponent steering, bool wallsAllowed)
    {
        var headings = new List<Vector2> { Vector2.Normalize(goal - pos) };
        if (steering.CurrentPath.TryPeek(out var node))
        {
            var next = _transform.ToWorldPosition(new EntityCoordinates(node.GraphUid, node.Box.Center));
            if ((next - pos).Length() > 0.3f)
                headings.Insert(0, Vector2.Normalize(next - pos));
        }

        var (_, mask) = _physics.GetHardCollision(uid);
        var hits = new List<(EntityUid Hit, float Distance)>();
        foreach (var heading in headings)
        {
            var across = new Vector2(-heading.Y, heading.X);
            foreach (var side in Sides)
            {
                var ray = new CollisionRay(pos + across * side, heading, mask);
                foreach (var hit in _physics.IntersectRay(xform.MapID, ray, comp.Reach, uid, false))
                {
                    hits.Add((hit.HitEntity, hit.Distance));
                }
            }
        }

        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var now = _timing.CurTime;
        EntityUid? wall = null;
        foreach (var (hit, _) in hits)
        {
            if (!IsBreakable(comp, hit, now))
                continue;

            if (!_tag.HasTag(hit, WallTag))
                return hit;

            wall ??= hit;
        }

        return comp.BreaksWalls && wallsAllowed ? wall : null;
    }

    private bool IsBreakable(WFStuckBreakerComponent comp, EntityUid hit, TimeSpan now)
    {
        if (TerminatingOrDeleted(hit)
            || HasComp<MobStateComponent>(hit)
            || !Transform(hit).Anchored
            || !HasComp<DestructibleComponent>(hit)
            || comp.GaveUp.TryGetValue(hit, out var until) && now < until)
        {
            return false;
        }

        return comp.Spare == null || !_whitelist.IsValid(comp.Spare, hit);
    }

    /// <summary>One swing at the obstacle. It lets go of one that four swings in a row didn't scratch.</summary>
    private void Attack(EntityUid uid, WFStuckBreakerComponent comp, EntityUid target, TimeSpan now)
    {
        if (!_melee.TryGetWeapon(uid, out var weaponUid, out var weapon) || weapon.NextAttack > now)
            return;

        var damage = TryComp<DamageableComponent>(target, out var damageable) ? damageable.TotalDamage.Float() : 0f;
        if (comp.Working == target)
            comp.Wasted = damage <= comp.WorkedDamage ? comp.Wasted + 1 : 0;
        else
            comp.Wasted = 0;

        comp.Working = target;
        comp.WorkedDamage = damage;
        if (comp.Wasted >= MaxWasted)
        {
            foreach (var old in comp.GaveUp.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList())
            {
                comp.GaveUp.Remove(old);
            }

            comp.GaveUp[target] = now + GiveUp;
            comp.Working = null;
            comp.Wasted = 0;
            return;
        }

        TryComp<CombatModeComponent>(uid, out var combat);
        var wasInCombat = combat?.IsInCombatMode ?? true;
        if (combat != null && !wasInCombat)
            _combat.SetInCombatMode(uid, true, combat);

        _melee.AttemptLightAttack(uid, weaponUid, weapon, target);

        if (combat != null && !wasInCombat)
            _combat.SetInCombatMode(uid, false, combat);
    }
}
