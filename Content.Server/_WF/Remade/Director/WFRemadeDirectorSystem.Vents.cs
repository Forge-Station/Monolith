using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Tools.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Vent travel. The station's pumps and scrubbers are the Remade's tunnels: one slips in out of sight, is held inside its
/// Monolith while it travels, rattles each vent it passes where the crew can hear, and bursts out near its prey, unseen.
/// No Remade is in the world while it travels. Vents only link on the same grid.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    private const string HoldId = "wf_remade_vent_hold";
    private const int MaxSquads = 4;
    private const int MaxRattles = 3;
    private const int MaxWaits = 8;
    private const float VentSpeed = 8f;
    private const float EnterSeconds = 1.5f;
    // A trip was only taken from an origin 30 m from the prey to an exit 20 m from it, so on a small hull it never happened.
    private const float TripMin = 12f;
    private const float OriginMin = 18f;
    private const float RouteWidth = 12f;
    private static readonly TimeSpan TripRate = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan VentCache = TimeSpan.FromSeconds(10);

    private readonly Dictionary<EntityUid, (TimeSpan Expires, List<EntityUid> Vents)> _vents = new();
    private TimeSpan _nextTrip;

    /// <summary>A place a Remade can be brought in: a vent, or bare corruption.</summary>
    private readonly record struct SpawnPoint(EntityUid? Vent, EntityCoordinates At);

    private void InitializeVents()
    {
    }

    /// <summary>The working vents on a grid, kept for a few seconds so a wave doesn't search the whole map.</summary>
    private List<EntityUid> VentsOn(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_vents.TryGetValue(grid, out var cached) && now < cached.Expires)
        {
            cached.Vents.RemoveAll(vent => TerminatingOrDeleted(vent));
            return cached.Vents;
        }

        foreach (var stale in _vents.Keys.Where(key => TerminatingOrDeleted(key)).ToList())
        {
            _vents.Remove(stale);
        }

        var list = new List<EntityUid>();
        var pumps = EntityQueryEnumerator<GasVentPumpComponent, TransformComponent>();
        while (pumps.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored && !IsWelded(uid))
                list.Add(uid);
        }

        var scrubbers = EntityQueryEnumerator<GasVentScrubberComponent, TransformComponent>();
        while (scrubbers.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored && !IsWelded(uid))
                list.Add(uid);
        }

        _vents[grid] = (now + VentCache, list);
        return list;
    }

    /// <summary>Forgets the vents it has found. For tests that build a deck and then add vents.</summary>
    public void ForgetVents()
    {
        _vents.Clear();
    }

    private bool IsWelded(EntityUid vent)
    {
        return TryComp<WeldableComponent>(vent, out var weld) && weld.IsWelded;
    }

    private float VentDistance(EntityUid a, EntityUid b)
    {
        return (WorldPos(a) - WorldPos(b)).Length();
    }

    /// <summary>Vents and corruption near the prey that no crewman can see.</summary>
    private List<SpawnPoint> ArrivalPoints(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntityUid prey, EntityUid grid)
    {
        var points = new List<SpawnPoint>();
        var preyPosition = WorldPos(prey);
        foreach (var vent in VentsOn(grid))
        {
            var xform = Transform(vent);
            if (InArrivalBand(xform, preyPosition) && !SeenByAnyCrew(xform))
                points.Add(new SpawnPoint(vent, xform.Coordinates));
        }

        foreach (var uid in ent.Comp2.Corruption)
        {
            if (TryComp(uid, out TransformComponent? xform) && xform.GridUid == grid
                && InArrivalBand(xform, preyPosition) && !SeenByAnyCrew(xform))
            {
                points.Add(new SpawnPoint(null, xform.Coordinates));
            }
        }

        // Corruption tiles outnumber vents a hundred to one, so a wave almost never came by a vent: keep only as many tiles as vents.
        var vents = points.FindAll(point => point.Vent != null);
        if (vents.Count > 0)
        {
            var tiles = points.FindAll(point => point.Vent == null);
            _random.Shuffle(tiles);
            points = vents;
            points.AddRange(tiles.GetRange(0, Math.Min(tiles.Count, vents.Count)));
        }

        _random.Shuffle(points);
        return points;
    }

    private bool InArrivalBand(TransformComponent xform, Vector2 preyPosition)
    {
        var distance = (_transform.GetWorldPosition(xform) - preyPosition).Length();
        return distance is >= SpawnMin and <= SpawnMax;
    }

    private bool SeenByAnyCrew(TransformComponent xform)
    {
        var point = _transform.GetMapCoordinates(xform);
        foreach (var crewman in _crew)
        {
            if (_examine.InRangeUnOccluded(_transform.GetMapCoordinates(crewman), point, CrewSight, null))
                return true;
        }

        return false;
    }

    /// <summary>Far-off vents a wave can start from, so it has to travel.</summary>
    private List<EntityUid> OriginVents(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntityUid prey, EntityUid grid)
    {
        var preyPosition = WorldPos(prey);
        return VentsOn(grid)
            .Where(vent => (WorldPos(vent) - preyPosition).Length() >= OriginMin)
            .ToList();
    }

    /// <summary>Picks different places round the prey, as far apart in bearing as it can, so a wave comes from several sides.</summary>
    private List<SpawnPoint> SpreadPoints(List<SpawnPoint> points, EntityUid prey, int count)
    {
        var preyPosition = WorldPos(prey);
        float Bearing(SpawnPoint point)
        {
            var offset = _transform.ToMapCoordinates(point.At).Position - preyPosition;
            return MathF.Atan2(offset.Y, offset.X);
        }

        float Gap(float a, float b)
        {
            var gap = MathF.Abs(a - b) % MathF.Tau;
            return gap > MathF.PI ? MathF.Tau - gap : gap;
        }

        var chosen = new List<SpawnPoint> { points[0] };
        var bearings = new List<float> { Bearing(points[0]) };
        while (chosen.Count < count && chosen.Count < points.Count)
        {
            SpawnPoint best = default;
            var bestGap = -1f;
            foreach (var point in points)
            {
                if (chosen.Contains(point))
                    continue;

                var bearing = Bearing(point);
                var gap = bearings.Min(other => Gap(bearing, other));
                if (gap > bestGap)
                {
                    best = point;
                    bestGap = gap;
                }
            }

            chosen.Add(best);
            bearings.Add(Bearing(best));
        }

        return chosen;
    }

    private EntityUid? NearestVent(EntityUid grid, Vector2 position, float range)
    {
        EntityUid? nearest = null;
        var best = range * range;
        foreach (var vent in VentsOn(grid))
        {
            var distance = (WorldPos(vent) - position).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = vent;
            }
        }

        return nearest;
    }

    /// <summary>A vent well away from another that no crewman can see, to hide in.</summary>
    private EntityUid? FarVent(EntityUid grid, EntityUid from)
    {
        var origin = WorldPos(from);
        var options = VentsOn(grid)
            .Where(vent => vent != from && (WorldPos(vent) - origin).Length() >= 15f && !SeenByCrew(Transform(vent).Coordinates, CrewSight))
            .ToList();
        return options.Count == 0 ? null : _random.Pick(options);
    }

    /// <summary>Up to a few vents between two others, in order, that a hidden Remade passes on the way.</summary>
    private List<EntityUid> BuildRoute(EntityUid from, EntityUid to, EntityUid grid)
    {
        var start = WorldPos(from);
        var line = WorldPos(to) - start;
        var length = line.LengthSquared();
        var between = new List<(float Along, EntityUid Vent)>();
        if (length > 1f)
        {
            foreach (var vent in VentsOn(grid))
            {
                if (vent == from || vent == to)
                    continue;

                var position = WorldPos(vent);
                var along = Vector2.Dot(position - start, line) / length;
                if (along is < 0.1f or > 0.9f || (start + line * along - position).Length() > RouteWidth)
                    continue;

                between.Add((along, vent));
            }
        }

        between.Sort((a, b) => a.Along.CompareTo(b.Along));
        var count = Math.Min(MaxRattles, between.Count);
        var route = new List<EntityUid>(count);
        for (var i = 0; i < count; i++)
        {
            route.Add(between[(int) ((i + 0.5f) * between.Count / count)].Vent);
        }

        return route;
    }

    /// <summary>Sends a Remade through the vents from one to another. It must belong to a Monolith, which holds it on the way.</summary>
    public bool TryVentTravel(EntityUid remade, EntityUid from, EntityUid to)
    {
        return TryVentTravel(remade, from, to, Transform(to).Coordinates, _timing.CurTime, null);
    }

    private bool TryVentTravel(EntityUid remade, EntityUid from, EntityUid to, EntityCoordinates exit, TimeSpan now, TimeSpan? returnAt)
    {
        if (CompOrNull<WFRemadeComponent>(remade)?.Source is not { } holder
            || TerminatingOrDeleted(holder)
            || Transform(from).GridUid is not { } grid
            || HasComp<WFRemadeVentTripComponent>(remade))
        {
            return false;
        }

        var route = BuildRoute(from, to, grid);
        var trip = AddComp<WFRemadeVentTripComponent>(remade);
        var at = now + TimeSpan.FromSeconds(EnterSeconds);
        var previous = WorldPos(from);
        foreach (var vent in route)
        {
            var position = WorldPos(vent);
            at += TimeSpan.FromSeconds((position - previous).Length() / VentSpeed);
            trip.Hops.Add((vent, at));
            previous = position;
        }

        at += TimeSpan.FromSeconds((WorldPos(to) - previous).Length() / VentSpeed + 0.5f);
        trip.ExitAt = returnAt is { } later && later > at ? later : at;
        trip.ExitVent = to;
        trip.ExitAtCoordinates = exit;
        trip.Holder = holder;

        var container = _container.EnsureContainer<Container>(holder, HoldId);
        Freeze(remade);
        if (!_container.Insert(remade, container))
        {
            RemComp<WFRemadeVentTripComponent>(remade);
            Thaw(remade);
            return false;
        }

        var behaviour = EnsureComp<WFRemadeBehaviourComponent>(remade);
        behaviour.Mode = WFRemadeMode.Hidden;
        behaviour.Target = null;
        ClearKnown(remade, behaviour);

        if (TryComp<WFRemadeDirectorComponent>(holder, out var director))
            _audio.PlayPvs(director.RattleSound, Transform(from).Coordinates);

        return true;
    }

    /// <summary>Stops a Remade thinking or moving. It stays in the world, inert.</summary>
    private void Freeze(EntityUid remade)
    {
        if (TryComp<HTNComponent>(remade, out var htn))
            _htn.SetHTNEnabled((remade, htn), false);

        _npc.SleepNPC(remade);
    }

    private void Thaw(EntityUid remade)
    {
        if (TryComp<HTNComponent>(remade, out var htn))
            _htn.SetHTNEnabled((remade, htn), true);

        _npc.WakeNPC(remade);
    }

    private void UpdateTrips(TimeSpan now)
    {
        if (now < _nextTrip)
            return;

        _nextTrip = now + TripRate;
        var query = EntityQueryEnumerator<WFRemadeVentTripComponent>();
        while (query.MoveNext(out var uid, out var trip))
        {
            while (trip.Next < trip.Hops.Count && now >= trip.Hops[trip.Next].At)
            {
                Rattle(trip.Hops[trip.Next].Vent, trip.Holder);
                trip.Next++;
            }

            if (trip.Next >= trip.Hops.Count && now >= trip.ExitAt)
                ExitVent(uid, trip, now);
        }
    }

    private void Rattle(EntityUid vent, EntityUid holder)
    {
        if (TerminatingOrDeleted(vent) || !TryComp<WFRemadeDirectorComponent>(holder, out var director))
            return;

        director.Rattles++;
        _audio.PlayPvs(director.RattleSound, Transform(vent).Coordinates);

        // The grille shakes as it goes by, so a crewman who is looking at the vent sees it too.
        _jitter.DoJitter(vent, TimeSpan.FromSeconds(0.8), true, 6f, 14f, true);
    }

    /// <summary>It bursts out, unless a crewman is looking at the vent, in which case it waits and rattles a little longer.</summary>
    private void ExitVent(EntityUid remade, WFRemadeVentTripComponent trip, TimeSpan now)
    {
        var at = trip.ExitVent is { } vent && !TerminatingOrDeleted(vent) ? Transform(vent).Coordinates : trip.ExitAtCoordinates;

        // An ambusher stays in the exit vent and waits for a crewman instead of bursting out.
        if (trip.Ambush && trip.ExitVent is { } lair && !TerminatingOrDeleted(lair))
        {
            RemCompDeferred<WFRemadeVentTripComponent>(remade);
            BeginAmbush(remade, lair, trip.Holder, now);
            return;
        }

        if (trip.Waited < MaxWaits && SeenByCrew(at, CrewSight))
        {
            trip.Waited++;
            trip.ExitAt = now + TimeSpan.FromSeconds(2.5);
            if (trip.ExitVent is { } waiting)
                Rattle(waiting, trip.Holder);

            return;
        }

        RemCompDeferred<WFRemadeVentTripComponent>(remade);
        if (TerminatingOrDeleted(trip.Holder)
            || !_container.TryGetContainer(trip.Holder, HoldId, out var container)
            || !_container.Remove(remade, container, destination: at))
        {
            return;
        }

        Thaw(remade);
        DamageVent(trip.ExitVent);
        FinishHaul(remade);
        var behaviour = EnsureComp<WFRemadeBehaviourComponent>(remade);
        behaviour.ArrivalVent = trip.ExitVent;
        var prey = CompOrNull<WFRemadeDirectorComponent>(trip.Holder)?.Prey;
        Emerge(remade, behaviour, at, prey, now);
    }
}
