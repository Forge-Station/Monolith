using System.Collections.Generic;
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Ghost;
using Content.Shared.Gravity;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Tiles;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Safety;

/// <summary>Keeps vehicle interiors and their occupants safe from cleanup, edits, deletion, ship sales and strays.</summary>
public sealed partial class VehicleSafetySystem : EntitySystem
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    /// <summary>Ambient light of every interior map; the cabins have no light fixtures.</summary>
    private static readonly Color InteriorAmbientLight = Color.FromHex("#8A8A92");

    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private VehicleInteriorSystem _interior = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private VehiclePullingSystem _pulling = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<MapId, Entity<VehicleInteriorComponent>> _interiors = new();
    private readonly List<(EntityUid Mob, Entity<VehicleInteriorComponent> Interior)> _strays = new();
    private readonly HashSet<EntityUid> _occupiedVehicles = new();
    private readonly List<EntityUid> _preserved = new();
    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleInteriorLoadedEvent>(OnInteriorLoaded);
        SubscribeLocalEvent<VehicleEnterComponent, EntityTerminatingEvent>(OnVehicleTerminating);
        SubscribeLocalEvent<VehicleEnterComponent, ShipyardOrganicsSearchEvent>(OnOrganicsSearch);
        SubscribeLocalEvent<ShipSoldEvent>(OnShipSold);
    }

    private void OnInteriorLoaded(ref VehicleInteriorLoadedEvent ev)
    {
        EnsureComp<CleanupImmuneComponent>(ev.Grid);

        var shield = EnsureComp<ProtectedGridComponent>(ev.Grid);
        shield.PreventFloorRemoval = true;
        shield.PreventFloorPlacement = true;
        shield.PreventRCDUse = true;
        shield.PreventExplosions = true;
        Dirty(ev.Grid, shield);

        // The source maps do not all switch gravity on, and there is no generator in a cabin.
        var gravity = EnsureComp<GravityComponent>(ev.Grid);
        gravity.Inherent = true;
        gravity.Enabled = true;
        Dirty(ev.Grid, gravity);

        if (TryComp(ev.MapUid, out MapComponent? map))
            _map.SetAmbientLight(map.MapId, InteriorAmbientLight);
    }

    /// <summary>Moves the occupants out before the interior map goes with the vehicle.</summary>
    private void OnVehicleTerminating(Entity<VehicleEnterComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!TryComp(ent, out VehicleInteriorComponent? interior) || interior.MapId == MapId.Nullspace)
            return;

        var xform = Transform(ent);

        // Nothing to save when the whole map is being deleted.
        if (xform.MapUid is not { } map || TerminatingOrDeleted(map))
            return;

        // A grid takes its children with it, so a vehicle on a dying grid (or in a container) drops them on the map instead.
        var destination = IsHeldByTerminatingParent(ent, xform)
            ? new EntityCoordinates(map, _transform.GetWorldPosition(xform))
            : xform.Coordinates;

        _interior.EjectAllOccupants(ent, destination);
    }

    private bool IsHeldByTerminatingParent(EntityUid vehicle, TransformComponent xform)
    {
        if (_container.IsEntityInContainer(vehicle))
            return true;

        var parent = xform.ParentUid;
        while (parent.IsValid())
        {
            if (TerminatingOrDeleted(parent))
                return true;

            parent = Transform(parent).ParentUid;
        }

        return false;
    }

    /// <summary>Counts a living, sapient being inside the vehicle's interior as aboard the hull the vehicle is parked on.</summary>
    private void OnOrganicsSearch(Entity<VehicleEnterComponent> ent, ref ShipyardOrganicsSearchEvent args)
    {
        if (args.Name != null ||
            !TryComp(ent, out VehicleInteriorComponent? interior) ||
            interior.MapId == MapId.Nullspace)
        {
            return;
        }

        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != interior.MapId || HasComp<GhostComponent>(uid))
                continue;

            if (_player.TryGetSessionByEntity(uid, out _) ||
                _mind.TryGetMind(uid, out _, out var mind) && !_mind.IsCharacterDeadPhysically(mind))
            {
                args.Name = Name(uid);
                return;
            }
        }
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;

        _interiors.Clear();
        var interiors = EntityQueryEnumerator<VehicleInteriorComponent>();
        while (interiors.MoveNext(out var uid, out var interior))
        {
            // An interior whose grid is gone is the interior system's to replace on the next entry.
            var grid = interior.Grid;
            if (interior.MapId != MapId.Nullspace && grid.IsValid() && !TerminatingOrDeleted(grid))
                _interiors[interior.MapId] = (uid, interior);
        }

        if (_interiors.Count == 0)
            return;

        _strays.Clear();
        _occupiedVehicles.Clear();
        foreach (var interior in _interiors.Values)
        {
            var map = interior.Comp.Map;
            if (map.IsValid() && !TerminatingOrDeleted(map))
                SweepMobs(map, interior);
        }

        foreach (var (mob, interior) in _strays)
        {
            ReturnToEntry(mob, interior);
        }

        _strays.Clear();

        foreach (var vehicle in _occupiedVehicles)
        {
            KeepHullFromCleanup(vehicle);
        }

        _occupiedVehicles.Clear();
    }

    /// <summary>Walks an interior's tree, into grids and containers only, for the mobs in it.</summary>
    private void SweepMobs(EntityUid parent, Entity<VehicleInteriorComponent> interior)
    {
        var children = Transform(parent).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!HasComp<MobStateComponent>(child))
            {
                if (HasComp<MapGridComponent>(child) || HasComp<ContainerManagerComponent>(child))
                    SweepMobs(child, interior);

                continue;
            }

            if (HasComp<GhostComponent>(child))
                continue;

            if (TryComp(child, out MindContainerComponent? mind) && mind.HasMind)
                _occupiedVehicles.Add(interior.Owner);

            if (IsInsideCabin(interior, child))
                continue;

            _strays.Add((child, interior));
        }
    }

    /// <summary>Puts the cleanup timer of a hull back to zero while someone is inside a vehicle parked on it.</summary>
    private void KeepHullFromCleanup(EntityUid vehicle)
    {
        if (Transform(vehicle).GridUid is { } hull && TryComp(hull, out GridCleanupGridComponent? cleanup))
            cleanup.CleanupAccumulator = TimeSpan.Zero;
    }

    /// <summary>Moves the IDs, PDAs and cash lying in each parked vehicle's interior to the console before a sale.</summary>
    private void OnShipSold(ref ShipSoldEvent args)
    {
        _preserved.Clear();

        var vehicles = EntityQueryEnumerator<VehicleInteriorComponent, TransformComponent>();
        while (vehicles.MoveNext(out _, out var interior, out var xform))
        {
            if (xform.GridUid != args.Shuttle || interior.MapId == MapId.Nullspace)
                continue;

            CollectPreserved(interior.Grid, _preserved);
            CollectPreserved(interior.Map, _preserved);
        }

        foreach (var uid in _preserved)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            _transform.SetCoordinates(uid, new EntityCoordinates(args.Console, 0, 0));
            _transform.AttachToGridOrMap(uid);
        }

        _preserved.Clear();
    }

    /// <summary>Adds what is kept on a sale among the children of <paramref name="parent"/>, looking inside containers.</summary>
    private void CollectPreserved(EntityUid parent, List<EntityUid> output)
    {
        if (!parent.IsValid() || TerminatingOrDeleted(parent))
            return;

        var children = Transform(parent).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            CollectPreservedIn(child, output);
        }
    }

    private void CollectPreservedIn(EntityUid entity, List<EntityUid> output)
    {
        if (TryComp(entity, out ShipyardSellConditionComponent? sell) && sell.PreserveOnSale)
        {
            output.Add(entity);
            return;
        }

        if (!TryComp(entity, out ContainerManagerComponent? containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                CollectPreservedIn(contained, output);
            }
        }
    }

    private bool IsInsideCabin(Entity<VehicleInteriorComponent> interior, EntityUid mob)
    {
        return IsInsideCabin(interior, _transform.GetWorldPosition(mob));
    }

    /// <summary>Whether <paramref name="position"/> is over a tile of the interior grid, and so a place the sweep leaves alone.</summary>
    public bool IsInsideCabin(Entity<VehicleInteriorComponent> interior, EntityCoordinates position)
    {
        return IsInsideCabin(interior, _transform.ToMapCoordinates(position).Position);
    }

    /// <summary>Whether a tile is part of the cabin: every tile that is not empty, the void included.</summary>
    private bool IsInsideCabin(Entity<VehicleInteriorComponent> interior, Vector2 worldPosition)
    {
        return TryComp(interior.Comp.Grid, out MapGridComponent? grid) &&
               _map.TryGetTileRef(interior.Comp.Grid, grid, worldPosition, out var tile) &&
               !tile.Tile.IsEmpty;
    }

    private void ReturnToEntry(EntityUid mob, Entity<VehicleInteriorComponent> interior)
    {
        if (TerminatingOrDeleted(mob))
            return;

        var entry = interior.Comp.Entry;
        if (!entry.IsValid(EntityManager))
            entry = new EntityCoordinates(interior.Comp.Grid, Vector2.Zero);

        if (_container.IsEntityInContainer(mob))
            _container.TryRemoveFromContainer(mob, force: true);

        _pulling.HandlePulling(mob, _transform.ToMapCoordinates(entry));

        // Over the void nothing slows a mob down, so one thrown out of the cabin goes on drifting; put back with its speed
        // it would leave again and be sent back every second, each time breaking whatever it was doing (leaving, say).
        if (TryComp(mob, out PhysicsComponent? body))
        {
            _physics.SetLinearVelocity(mob, Vector2.Zero, body: body);
            _physics.SetAngularVelocity(mob, 0f, body: body);
        }

        _popup.PopupEntity(Loc.GetString("wf-vehicle-safety-returned"), mob, mob);
    }
}
