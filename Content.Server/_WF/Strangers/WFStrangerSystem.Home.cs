using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Chat;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.Strangers;

/// <summary>
/// Where strangers go when nobody has their attention: roamers wander the world, the rest keep to home, go out to meet
/// visitors on a leash and walk back after.
/// </summary>
public sealed partial class WFStrangerSystem
{
    /// <summary>How far past <see cref="WFStrangerHomeComponent.WarnRadius"/> a resident goes after anyone, in multiples of it.</summary>
    private const float LeashFactor = 2f;

    /// <summary>How long a wander leg may take before he gives it up and lingers where he got to.</summary>
    private static readonly TimeSpan WanderPatience = TimeSpan.FromSeconds(60);

    /// <summary>How soon he tries again after a wander leg that went nowhere.</summary>
    private static readonly TimeSpan FailedLegRetry = TimeSpan.FromSeconds(5);

    /// <summary>How long a resident leaves someone be after they've led him past his leash.</summary>
    private static readonly TimeSpan LeashCooldown = TimeSpan.FromSeconds(90);

    /// <summary>The home he spawned in, if any.</summary>
    private EntityUid? FindHome(EntityUid uid)
    {
        var here = _transform.GetMapCoordinates(uid);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var home in _lookup.GetEntitiesInRange<WFStrangerHomeComponent>(here, 24f))
        {
            var distance = Vector2.Distance(_transform.GetMapCoordinates(home).Position, here.Position);
            if (distance > home.Comp.Radius + 1f || distance >= bestDistance)
                continue;
            best = home;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>Gives a stranger a home to keep to and return to; null turns him loose to roam.</summary>
    public void SetHome(Entity<WFStrangerComponent> ent, EntityUid? home)
    {
        ent.Comp.Home = home;
        ent.Comp.NextWander = _timing.CurTime;
        StopWandering(ent.Comp);
    }

    /// <summary>His home, if he has one that still exists.</summary>
    private Entity<WFStrangerHomeComponent>? HomeOf(WFStrangerComponent stranger)
    {
        return stranger.Home is { } home && TryComp(home, out WFStrangerHomeComponent? comp) ? (home, comp) : null;
    }

    /// <summary>Whether somebody is close enough to home for a resident to bother with.</summary>
    private bool WithinLeash(Entity<WFStrangerHomeComponent> home, EntityUid who)
    {
        var centre = _transform.GetMapCoordinates(home);
        var there = _transform.GetMapCoordinates(who);
        return centre.MapId == there.MapId && Vector2.Distance(centre.Position, there.Position) <= home.Comp.WarnRadius * LeashFactor;
    }

    /// <summary>A resident doesn't go after people on a ship of their own, as he can't follow aboard.</summary>
    private bool Reachable(EntityUid ent, EntityUid who)
    {
        return Transform(who).GridUid is not { } grid || grid == Transform(ent).GridUid || !HasComp<ShuttleComponent>(grid);
    }

    /// <summary>
    /// A player in sight of the home whom this resident should go and meet: the nearest free one among those who live
    /// here, so two never walk up to the same visitor.
    /// </summary>
    private EntityUid? Visitor(Entity<WFStrangerComponent> ent, TransformComponent xform, Entity<WFStrangerHomeComponent> home, TimeSpan now)
    {
        if (Nearest(ent, xform, now, home) is not { } visitor)
            return null;

        var mine = Vector2.Distance(_transform.GetWorldPosition(xform), _transform.GetWorldPosition(visitor));
        var mates = EntityQueryEnumerator<WFStrangerComponent, TransformComponent>();
        while (mates.MoveNext(out var mate, out var stranger, out var mateXform))
        {
            if (mate == ent.Owner || stranger.Home != ent.Comp.Home || !_mobs.IsAlive(mate))
                continue;
            if (stranger.Target == visitor)
                return null;
            if (stranger.Target != null || stranger.Hostile || stranger.Node != null || stranger.Behaviour != WFStrangerBehaviour.Approach)
                continue;

            var theirs = Vector2.Distance(_transform.GetWorldPosition(mateXform), _transform.GetWorldPosition(visitor));
            if ((theirs < mine || theirs == mine && mate.Id < ent.Owner.Id)
                && Usable((mate, stranger), visitor, now, home)
                && _examine.InRangeUnOccluded(_transform.GetMapCoordinates(mate, mateXform), _transform.GetMapCoordinates(visitor), stranger.SightRange, null))
                return null;
        }
        return visitor;
    }

    /// <summary>Says hello, once per visitor, as he sets off to meet them.</summary>
    private void Greet(Entity<WFStrangerComponent> ent, Entity<WFStrangerHomeComponent> home, EntityUid visitor, TimeSpan now)
    {
        if (ent.Comp.Greeted.ContainsKey(visitor))
            return;
        ent.Comp.Greeted[visitor] = now;
        if (home.Comp.Greetings.Count > 0)
            _chat.TrySendInGameICMessage(ent, Loc.GetString(_random.Pick(home.Comp.Greetings)), InGameICChatType.Speak, false);
    }

    /// <summary>
    /// Walks a leg at a time: off to somewhere open within <paramref name="range"/> of himself, or of his home if he has
    /// one, then he lingers there a good while before the next. Strayed from home, he heads straight back.
    /// </summary>
    private void Wander(Entity<WFStrangerComponent> ent, TransformComponent xform, TimeSpan now, EntityUid? home, float range)
    {
        if (ent.Comp.WanderUntil is { } until)
        {
            // Still on his way: leave the leg be until he gets there, or gives up on it.
            if (now < until && TryComp(ent, out NPCSteeringComponent? steering) && steering.Status == SteeringStatus.Moving)
                return;
            // A leg with no way there: he tries another spot shortly rather than standing about.
            var noPath = !TryComp(ent, out NPCSteeringComponent? ended) || ended.Status == SteeringStatus.NoPath;
            ent.Comp.WanderUntil = null;
            _steering.Unregister(ent);
            ent.Comp.NextWander = now + (noPath ? FailedLegRetry : Linger(ent.Comp));
            return;
        }

        var straying = home is { } place
                       && Vector2.Distance(_transform.GetWorldPosition(place), _transform.GetWorldPosition(xform)) > range + 1f;
        if (now < ent.Comp.NextWander && !straying)
            return;
        ent.Comp.NextWander = now + Linger(ent.Comp);
        if (xform.GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return;

        var origin = home is { } centre ? _transform.GetWorldPosition(centre) : _transform.GetWorldPosition(xform);
        var inverse = _transform.GetInvWorldMatrix(grid);
        var boundsCentre = Vector2.Zero;
        var boundsRadius = 0f;
        var bounded = xform.MapUid is { } map && _bounds.TryGetBounds(map, out boundsCentre, out boundsRadius);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var spot = origin + _random.NextAngle().ToVec() * _random.NextFloat(range * 0.3f, range);
            if (bounded && Vector2.Distance(spot, boundsCentre) > boundsRadius * 0.9f)
                continue;
            var tile = Vector2.Transform(spot, inverse).Floored();
            if (_maps.GetTileRef(grid, gridComp, tile).Tile.IsEmpty || _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile).MoveNext(out _))
                continue;
            Walk(ent, new EntityCoordinates(grid, (Vector2) tile + new Vector2(0.5f)));
            ent.Comp.WanderUntil = now + WanderPatience;
            return;
        }
    }

    /// <summary>How long he stands about between legs: mostly half a minute to a minute and a half, now and then longer.</summary>
    private TimeSpan Linger(WFStrangerComponent stranger)
    {
        var seconds = _random.NextFloat((float) stranger.LingerMin.TotalSeconds, (float) stranger.LingerMax.TotalSeconds);
        if (_random.Prob(0.2f))
            seconds *= 2f;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Drops the wander leg he was on, for when someone has his attention or he's done with them.</summary>
    private void StopWandering(WFStrangerComponent stranger)
    {
        stranger.WanderUntil = null;
    }
}
