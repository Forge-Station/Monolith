using System.Linq;
using Robust.Shared.Audio.Systems;
using Content.Server._CE.ZLevels.Core;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Atmos.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Tag;
using Content.Shared.Doors.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets.Infestation;

/// <summary>Slow, bounded surface growth grips landed hulls and seeds their exposed perimeter.</summary>
public sealed partial class WFCarcinomaInfestationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private CEZLevelsSystem _z = default!;
    [Dependency] private WFPlanetBiomassSystem _biomass = default!;
    [Dependency] private TagSystem _tags = default!;
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly EntProtoId MeatWall = "WallMeat";
    [Dependency] private SharedAudioSystem _audio = default!;
    /// <summary>Most tendrils one hull can carry.</summary>
    public const int MaxTendrils = 4;
    private static readonly EntProtoId Tendril = "WFCarcinomaHullTendril";
    private static readonly EntProtoId Flesh = "ChimeraFleshKudzu";
    private static readonly Vector2i[] Neighbors = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFCarcinomaTendrilComponent, EntityTerminatingEvent>(OnTendrilGone);
        SubscribeLocalEvent<WFCarcinomaTendrilComponent, DamageChangedEvent>(OnTendrilDamaged);
        SubscribeLocalEvent<WFCarcinomaInfestationComponent, WFLiftoffAttemptEvent>(OnLiftoff);
        SubscribeLocalEvent<WFCarcinomaInfestationComponent, ExaminedEvent>(OnExamine);
    }

    private void OnLiftoff(Entity<WFCarcinomaInfestationComponent> ent, ref WFLiftoffAttemptEvent args)
    {
        Prune(ent, ent.Comp);
        if (ent.Comp.Tendrils.Count == 0) return;
        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-carcinoma-tethered", ("count", ent.Comp.Tendrils.Count));
    }

    private void OnExamine(Entity<WFCarcinomaInfestationComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Tendrils.Count > 0)
            args.PushMarkup(Loc.GetString("wf-carcinoma-tethered", ("count", ent.Comp.Tendrils.Count)));
    }

    private void OnTendrilDamaged(Entity<WFCarcinomaTendrilComponent> ent, ref DamageChangedEvent args)
    {
        if (args.Damageable.TotalDamage >= 40) QueueDel(ent);
    }

    private void OnTendrilGone(Entity<WFCarcinomaTendrilComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Hull is not { } hull || !TryComp<WFCarcinomaInfestationComponent>(hull, out var state)) return;
        state.Tendrils.Remove(ent);
        if (state.Tendrils.Count != 0) return;
        Release(hull, state);
        // Clearing a hull grants a window to spool up, even if the last growth deadline was imminent.
        state.NextGrowth = _timing.CurTime + TimeSpan.FromSeconds(45);
    }

    /// <summary>Whether the hull rests on the carcinoma world's ground layer.</summary>
    private bool IsLandedHere(EntityUid hull)
    {
        var map = Transform(hull).MapUid;
        return TryComp<WFPlanetLayerComponent>(map, out var layer)
            && layer.Network is { } net && TryGetEntity(net, out var network)
            && TryComp<WFPlanetNetworkComponent>(network, out var planet)
            && planet.Surface.Id == "WFSurfaceCarcinoma" && planet.GroundMap == map
            && _z.WfHasSkidGround(hull);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextSweep) return;
        _nextSweep = _timing.CurTime + TimeSpan.FromSeconds(2);
        var query = EntityQueryEnumerator<MapGridComponent, PhysicsComponent>();
        while (query.MoveNext(out var hull, out var grid, out var body))
        {
            if (HasComp<MapComponent>(hull) || HasComp<WFDetachedTerrainComponent>(hull)) continue;
            var hasState = TryComp<WFCarcinomaInfestationComponent>(hull, out var state);
            if (!IsLandedHere(hull))
            {
                if (hasState)
                {
                    foreach (var tendril in state!.Tendrils.ToArray()) QueueDel(tendril);
                    state.Tendrils.Clear();
                    Release(hull, state);
                    RemCompDeferred<WFCarcinomaInfestationComponent>(hull);
                }
                continue;
            }
            if (!hasState)
            {
                state = AddComp<WFCarcinomaInfestationComponent>(hull);
                state.NextGrowth = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(45, 75));
            }
            Prune(hull, state!);
            if (_timing.CurTime < state!.NextGrowth) continue;
            state.NextGrowth = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(45, 90));
            // No sudden static-body switch while a wreck is still sliding or rotating.
            if (body.LinearVelocity.LengthSquared() > 0.25f || MathF.Abs(body.AngularVelocity) > 0.05f) continue;
            Grow(hull, grid, state);
        }
    }

    /// <summary>Forgets tendrils that died or left the hull, releasing it once none remain.</summary>
    private void Prune(EntityUid hull, WFCarcinomaInfestationComponent state)
    {
        foreach (var tendril in state.Tendrils.ToArray())
        {
            if (!TerminatingOrDeleted(tendril) && Transform(tendril).GridUid == hull) continue;
            state.Tendrils.Remove(tendril);
            if (!TerminatingOrDeleted(tendril)) QueueDel(tendril);
        }
        if (state.Tendrils.Count == 0) Release(hull, state);
    }

    /// <summary>Restores the hull's body type if the tendrils had pinned it.</summary>
    private void Release(EntityUid hull, WFCarcinomaInfestationComponent state)
    {
        if (!state.Held || TerminatingOrDeleted(hull)) return;
        state.Held = false;
        if (!HasComp<ForceAnchorComponent>(hull) && TryComp<PhysicsComponent>(hull, out var body)
            && body.BodyType == BodyType.Static)
            _physics.SetBodyType(hull, state.PreviousBodyType, body: body);
    }

    /// <summary>Convert only the immediate 3x3 hull neighborhood, never the terrain underneath.</summary>
    private void ConvertNearbyWalls(EntityUid hull, MapGridComponent grid, Vector2i centre)
    {
        var walls = new HashSet<EntityUid>();
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            var anchored = _map.GetAnchoredEntitiesEnumerator(hull, grid, centre + new Vector2i(x, y));
            while (anchored.MoveNext(out var entity))
            {
                if (entity is { } wall && _tags.HasTag(wall, WallTag) && !HasComp<DoorComponent>(wall)
                    && MetaData(wall).EntityPrototype?.ID != MeatWall.Id)
                    walls.Add(wall);
            }
        }
        // Finish enumeration before replacing anchored entities. Keep the opening sealed with its new wall.
        foreach (var wall in walls)
        {
            Spawn(MeatWall, Transform(wall).Coordinates);
            Del(wall);
        }
    }

    /// <summary>Grows one tendril on the exposed perimeter, pins the hull and seeds one flesh tile.</summary>
    private void Grow(EntityUid hull, MapGridComponent grid, WFCarcinomaInfestationComponent state)
    {
        var perimeter = _map.GetAllTiles(hull, grid).Where(tile => !tile.Tile.IsEmpty
            && Neighbors.Any(dir => !_map.TryGetTileRef(hull, grid, tile.GridIndices + dir, out var neighbor) || neighbor.Tile.IsEmpty)).ToList();
        if (perimeter.Count == 0) return;
        _random.Shuffle(perimeter);
        foreach (var tile in perimeter)
        {
            var anchored = _map.GetAnchoredEntitiesEnumerator(hull, grid, tile.GridIndices);
            var occupied = false;
            while (anchored.MoveNext(out var entity)) occupied |= HasComp<WFCarcinomaTendrilComponent>(entity);
            if (occupied || state.Tendrils.Count >= MaxTendrils) continue;
            var tendril = Spawn(Tendril, _map.GridTileToLocal(hull, grid, tile.GridIndices));
            Comp<WFCarcinomaTendrilComponent>(tendril).Hull = hull;
            state.Tendrils.Add(tendril);
            _audio.PlayPvs(Comp<WFCarcinomaTendrilComponent>(tendril).DeploySound, tendril);
            ConvertNearbyWalls(hull, grid, tile.GridIndices);
            if (!state.Held && TryComp<PhysicsComponent>(hull, out var physics))
            {
                state.PreviousBodyType = physics.BodyType;
                state.Held = true;
                _physics.SetLinearVelocity(hull, System.Numerics.Vector2.Zero, body: physics);
                _physics.SetAngularVelocity(hull, 0, body: physics);
                _physics.SetBodyType(hull, BodyType.Static, body: physics);
            }
            break;
        }
        // Open doors have AirBlocked=false. Interior tiles can only be reached by native spread.
        foreach (var tile in perimeter)
        {
            if (_biomass.HullCount(hull) >= WFPlanetBiomassSystem.MaxHullBiomass) break;
            var anchored = _map.GetAnchoredEntitiesEnumerator(hull, grid, tile.GridIndices);
            var blocked = false;
            while (anchored.MoveNext(out var entity))
                blocked |= HasComp<WFPlanetBiomassRestrictionComponent>(entity)
                    || TryComp<AirtightComponent>(entity, out var air) && air.AirBlocked;
            if (blocked) continue;
            Spawn(Flesh, _map.GridTileToLocal(hull, grid, tile.GridIndices));
            break;
        }
    }
}
