using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.LandingGuide;
using Content.Shared._WF.Planets;
using Content.Shared._WF.ShipSquatters;
using Content.Shared.Doors.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.ShipSquatters;

public sealed partial class WFShipSquatterSystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private WFLandingSurveySystem _survey = default!;

    /// <summary>The squatter himself.</summary>
    public static readonly EntProtoId SquatterProto = "WFShipSquatter";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>When each unguarded ship was last seen with someone minding it.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _idleSince = new();

    /// <summary>When each idle ship next rolls for a squatter.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextRoll = new();

    private readonly List<MapCoordinates> _guards = new();
    private TimeSpan _nextCheck;

    /// <summary>When a ship went unguarded, if it still is.</summary>
    public TimeSpan? IdleSince(EntityUid ship) => _idleSince.TryGetValue(ship, out var since) ? since : null;

    /// <summary>
    /// Watches player ships landed on a planet's ground: once one has gone unguarded for the idle time it rolls now and
    /// then for a squatter, up to a cap for the world.
    /// </summary>
    private void UpdateSpawns()
    {
        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;
        if (!_config.GetCVar(WFShipSquatterCVars.Enabled))
        {
            _idleSince.Clear();
            _nextRoll.Clear();
            return;
        }

        var idle = TimeSpan.FromMinutes(_config.GetCVar(WFShipSquatterCVars.IdleMinutes));
        var rolls = TimeSpan.FromSeconds(_config.GetCVar(WFShipSquatterCVars.RollSeconds));
        var chance = _config.GetCVar(WFShipSquatterCVars.Chance);
        CollectGuards();

        var ships = EntityQueryEnumerator<ShuttleDeedComponent, ShuttleComponent, MapGridComponent, TransformComponent>();
        while (ships.MoveNext(out var ship, out _, out _, out var grid, out var xform))
        {
            if (!IsEligible(ship, xform) || GuardedNow(grid, xform))
            {
                _idleSince.Remove(ship);
                _nextRoll.Remove(ship);
                continue;
            }

            if (!_idleSince.TryGetValue(ship, out var since))
            {
                _idleSince[ship] = now;
                continue;
            }

            if (now - since < idle || _nextRoll.TryGetValue(ship, out var roll) && now < roll)
                continue;

            _nextRoll[ship] = now + rolls;
            if (_random.Prob(chance) && !WorldIsFull(xform.MapUid!.Value))
                TryMoveIn(ship, out _);
        }

        foreach (var ship in _idleSince.Keys.Where(ship => TerminatingOrDeleted(ship)).ToList())
        {
            _idleSince.Remove(ship);
            _nextRoll.Remove(ship);
        }
    }

    /// <summary>
    /// Whether a ship may have a squatter at all: a player's ship (it holds a deed) sitting on the ground of a planet,
    /// with none yet. Never a station, an NPC crew's ship, an encounter's wreck or a flying hull.
    /// </summary>
    public bool IsEligible(EntityUid ship, TransformComponent? xform = null)
    {
        if (!Resolve(ship, ref xform) || !HasComp<ShuttleDeedComponent>(ship) || HasComp<WFSquattedShipComponent>(ship)
            || HasComp<StationMemberComponent>(ship) || HasComp<WFEncounterGridComponent>(ship)
            || HasComp<WFCrewShipProfileComponent>(ship) || HasComp<FTLComponent>(ship))
            return false;

        return xform.MapUid is { } map && HasComp<WFPlanetLayerComponent>(map) && _survey.IsGroundLayer(map);
    }

    /// <summary>Whether a living player or NPC crewman is aboard or within the guard range.</summary>
    public bool IsGuarded(EntityUid ship)
    {
        if (!TryComp(ship, out MapGridComponent? grid))
            return true;

        CollectGuards();
        return GuardedNow(grid, Transform(ship));
    }

    private bool GuardedNow(MapGridComponent grid, TransformComponent xform)
    {
        var box = grid.LocalAABB;
        var centre = Vector2.Transform(box.Center, _transform.GetWorldMatrix(xform));
        var reach = box.Size.Length() / 2f + _config.GetCVar(WFShipSquatterCVars.GuardRange);
        foreach (var guard in _guards)
        {
            if (guard.MapId == xform.MapID && Vector2.Distance(guard.Position, centre) <= reach)
                return true;
        }

        return false;
    }

    /// <summary>Everyone who minds a ship by being near it: players, sleepers with a mind, and NPC crew.</summary>
    private void CollectGuards()
    {
        _guards.Clear();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var mob, out var xform))
        {
            if (mob.CurrentState != MobState.Dead && xform.MapID != MapId.Nullspace)
                _guards.Add(_transform.GetMapCoordinates(uid, xform));
        }

        // Someone asleep in a bunk with their player gone still has a mind.
        var minds = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (minds.MoveNext(out var uid, out var mind, out var mob, out var xform))
        {
            if (mob.CurrentState != MobState.Dead && mind.HasMind && xform.MapID != MapId.Nullspace)
                _guards.Add(_transform.GetMapCoordinates(uid, xform));
        }

        var crew = EntityQueryEnumerator<WFCrewComponent, MobStateComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out _, out var mob, out var xform))
        {
            if (mob.CurrentState != MobState.Dead && xform.MapID != MapId.Nullspace)
                _guards.Add(_transform.GetMapCoordinates(uid, xform));
        }
    }

    /// <summary>Whether the world's squatter cap is reached; a world is a planet's whole z-network.</summary>
    public bool WorldIsFull(EntityUid map)
    {
        var cap = _config.GetCVar(WFShipSquatterCVars.Cap);
        var world = WorldOf(map);
        var count = 0;
        var squatters = EntityQueryEnumerator<WFShipSquatterComponent, TransformComponent>();
        while (squatters.MoveNext(out var uid, out _, out var xform))
        {
            if (_mobs.IsAlive(uid) && xform.MapUid is { } there && WorldOf(there) == world && ++count >= cap)
                return true;
        }

        return cap <= 0;
    }

    private EntityUid WorldOf(EntityUid map)
    {
        return TryComp(map, out WFPlanetLayerComponent? layer) && layer.Network is { } net && TryGetEntity(net, out var network)
            ? network.Value
            : map;
    }

    /// <summary>
    /// Puts a squatter aboard: on a clear tile with air indoors, as far from the airlocks as the ship allows, with his
    /// bed made there. Nothing is opened, broken or moved to do it.
    /// </summary>
    public bool TryMoveIn(EntityUid ship, out EntityUid squatter)
    {
        squatter = EntityUid.Invalid;
        if (!TryComp(ship, out MapGridComponent? grid) || !TryPickBed(ship, grid, out var tile))
            return false;

        var at = _maps.GridTileToLocal(ship, grid, tile);
        squatter = Spawn(SquatterProto, at);
        var comp = Comp<WFShipSquatterComponent>(squatter);
        comp.Ship = ship;
        comp.Bed = at;
        EnsureComp<WFSquattedShipComponent>(ship).Squatter = squatter;
        foreach (var item in comp.BedItems)
        {
            Spawn(item, at);
        }

        return true;
    }

    private bool TryPickBed(EntityUid ship, MapGridComponent grid, out Vector2i bed)
    {
        bed = default;
        var airlocks = new List<Vector2i>();
        var query = EntityQueryEnumerator<AirlockComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == ship)
                airlocks.Add(_maps.CoordinatesToTile(ship, grid, xform.Coordinates));
        }

        var options = new List<(Vector2i Tile, float Away)>();
        var tiles = _maps.GetAllTiles(ship, grid);
        while (tiles.MoveNext(out var tile))
        {
            var indices = tile.Value.GridIndices;
            if (!_planner.IsSafePost(ship, indices, grid) || !Indoors(ship, grid, indices))
                continue;

            var away = airlocks.Count == 0 ? float.MaxValue : airlocks.Min(door => Vector2.Distance((Vector2) door, (Vector2) indices));
            options.Add((indices, away));
        }

        if (options.Count == 0)
            return false;

        // Clear of the hatches if there is room for it, else as clear as the ship allows.
        var far = options.Where(option => option.Away >= 4f).ToList();
        var pool = far.Count > 0 ? far : options.OrderByDescending(option => option.Away).Take(5).ToList();
        bed = _random.Pick(pool).Tile;
        return true;
    }
}
