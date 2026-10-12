using System.Linq;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.NpcCrew.Systems;

public sealed partial class WFPilotDutySystem
{
    [Dependency] private SharedPhysicsSystem _stancePhysics = default!;
    [Dependency] private WFCrewAlertSystem _threats = default!;

    /// <summary>How far, in radians, a threat's bearing may move before a holding ship turns its guns again.</summary>
    private const double AimDeadband = 0.1;

    /// <summary>How many bearings around the hull are tried for clear lines of fire.</summary>
    private const int StanceSectors = 16;

    /// <summary>How far out, as a share of the attack orbit, the ship starts presenting its guns.</summary>
    private const float StanceRange = 1.5f;

    /// <summary>How long a hull's best bearings are kept; guns get shot off and walls blown open.</summary>
    private static readonly TimeSpan StanceKept = TimeSpan.FromSeconds(30);

    private readonly Dictionary<EntityUid, (TimeSpan Until, Angle[] Bearings)> _stances = new();
    private TimeSpan _nextStancePrune;

    /// <summary>
    /// Attack: once near the orbit, turns the side of the hull most of its guns can fire from towards the target
    /// instead of flying nose along the orbit. A hull whose guns bear equally all round flies as before.
    /// </summary>
    private void PresentGuns(Entity<WFPilotDutyComponent> ent, ShipSteererComponent steerer)
    {
        var duty = ent.Comp;
        if (duty.Orders != WFPilotOrder.Loiter || duty.OrbitKind != WFCrewObjectiveKind.Attack || duty.AwaitingTarget
            || Transform(ent).GridUid is not { } grid || !steerer.Coordinates.IsValid(EntityManager))
            return;

        var toTarget = _transform.ToMapCoordinates(steerer.Coordinates).Position - _transform.GetWorldPosition(grid);
        if (toTarget.Length() > steerer.Range * StanceRange || GunHeading(grid, toTarget) is not { } heading)
        {
            steerer.TargetRotation = TravelHeadingOffset(ent);
            return;
        }

        duty.HeadingOverride = heading;
        steerer.TargetRotation = 0f;
    }

    /// <summary>Hold with FaceThreats: turns the held heading so the hull's best armed side looks at the nearest threat; the station is not left.</summary>
    private void FaceThreatsHolding(Entity<WFPilotDutyComponent> ent, ShipSteererComponent steerer)
    {
        var duty = ent.Comp;
        if (!duty.FaceThreats || duty.Orders != WFPilotOrder.Hold || duty.Docked
            || Transform(ent).GridUid is not { } grid || !TryComp<WFCrewComponent>(ent, out var crew)
            || NearestThreat(grid, crew.Group) is not { } threat)
            return;

        var toThreat = _transform.GetWorldPosition(threat) - _transform.GetWorldPosition(grid);
        if (GunHeading(grid, toThreat) is not { } heading
            || Math.Abs(ShipSteeringSystem.ShortestAngleDistance(duty.HoldHeading, heading).Theta) < AimDeadband)
            return;

        duty.HoldHeading = heading;
        steerer.InRangeRotation = heading;
    }

    /// <summary>The nearest ship that fired on this one, or failing that the nearest zone intruder.</summary>
    private EntityUid? NearestThreat(EntityUid grid, string group)
    {
        var here = _transform.GetWorldPosition(grid);
        EntityUid? best = null;
        var bestZone = true;
        var bestDistance = float.MaxValue;
        foreach (var ship in _threats.GetHostileShips(grid, group))
        {
            var zone = _threats.IsZoneThreat(grid, group, ship);
            var distance = (_transform.GetWorldPosition(ship) - here).LengthSquared();
            if (zone && !bestZone || zone == bestZone && distance >= bestDistance)
                continue;
            best = ship;
            bestZone = zone;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>Whether a holding ship that faces threats is still short of the heading it is to hold.</summary>
    private bool TurningToThreat(WFPilotDutyComponent duty, ShipSteererComponent steerer, EntityUid grid)
    {
        if (!duty.FaceThreats)
            return false;
        var facing = _transform.GetWorldRotation(grid) + new Angle(Math.PI);
        return Math.Abs(ShipSteeringSystem.ShortestAngleDistance(facing, duty.HoldHeading).Theta) > steerer.RotationTolerance;
    }

    /// <summary>
    /// The heading, as the steering holds one, that turns a hull's best armed side along the world vector to its
    /// target. Of equally good sides it is the one that takes the least turning. Null when no side is better armed.
    /// </summary>
    public Angle? GunHeading(EntityUid grid, Vector2 toTarget)
    {
        var bearings = GunBearings(grid);
        if (bearings.Length == 0 || toTarget.LengthSquared() < 1f)
            return null;

        var facing = _transform.GetWorldRotation(grid) + new Angle(Math.PI);
        var bearing = toTarget.ToWorldAngle();
        Angle? best = null;
        var least = double.MaxValue;
        foreach (var side in bearings)
        {
            var heading = bearing - side;
            var turn = Math.Abs(ShipSteeringSystem.ShortestAngleDistance(facing, heading).Theta);
            if (turn >= least)
                continue;
            least = turn;
            best = heading;
        }

        return best;
    }

    /// <summary>
    /// The bearings off a grid's own north from which the most of its guns have a clear
    /// line of fire past their own ship. Empty when it has no guns or they bear equally all round.
    /// </summary>
    public Angle[] GunBearings(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_stances.TryGetValue(grid, out var known) && now < known.Until)
            return known.Bearings;

        var bearings = FindGunBearings(grid);
        _stances[grid] = (now + StanceKept, bearings);
        return bearings;
    }

    private Angle[] FindGunBearings(EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var map))
            return Array.Empty<Angle>();

        var xform = Transform(grid);
        var rotation = _transform.GetWorldRotation(xform);
        var reach = map.LocalAABB.Size.Length();
        var clear = new int[StanceSectors];
        var guns = EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
        while (guns.MoveNext(out var gun, out _, out var gunXform))
        {
            if (gunXform.GridUid != grid || !gunXform.Anchored)
                continue;

            var at = _transform.GetWorldPosition(gunXform);
            for (var i = 0; i < StanceSectors; i++)
            {
                // The same test fire control makes before it lets a gun fire: nothing of its own ship in the way.
                var direction = rotation.RotateVec(SectorBearing(i).ToWorldVec());
                var ray = new CollisionRay(at, direction, (int) (CollisionGroup.Opaque | CollisionGroup.Impassable));
                if (!_stancePhysics.IntersectRayWithPredicate(xform.MapID, ray, (gun, grid),
                        (hit, state) => hit == state.gun || Transform(hit).GridUid != state.grid, reach).Any())
                    clear[i]++;
            }
        }

        // Neighbouring bearings count for half, so the middle of a wide arc beats its edge.
        var scores = new int[StanceSectors];
        for (var i = 0; i < StanceSectors; i++)
            scores[i] = clear[i] * 2 + clear[(i + 1) % StanceSectors] + clear[(i + StanceSectors - 1) % StanceSectors];

        var most = scores.Max();
        if (most == scores.Min())
            return Array.Empty<Angle>();

        var bearings = new List<Angle>();
        for (var i = 0; i < StanceSectors; i++)
        {
            if (scores[i] == most)
                bearings.Add(SectorBearing(i) - new Angle(Math.PI));
        }
        return bearings.ToArray();
    }

    /// <summary>A sector's direction in the grid's own frame, as a world angle.</summary>
    private static Angle SectorBearing(int sector) => new(Math.Tau * sector / StanceSectors);

    private void PruneStances(TimeSpan now)
    {
        if (_stances.Count == 0 || now < _nextStancePrune)
            return;
        _nextStancePrune = now + StanceKept;
        foreach (var (grid, entry) in _stances.ToArray())
        {
            if (now >= entry.Until || TerminatingOrDeleted(grid))
                _stances.Remove(grid);
        }
    }
}
