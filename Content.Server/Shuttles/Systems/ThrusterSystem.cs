using System.Numerics;
using Content.Server.Audio;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Content.Shared.Temperature;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics; // Forge-Change
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Shared.Localizations;
using Content.Shared.Power;
using Content.Server.Construction; // Frontier
using Content.Shared.DeviceLinking.Events; // Frontier

namespace Content.Server.Shuttles.Systems;

public sealed partial class ThrusterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private AmbientSoundSystem _ambient = default!;
    [Dependency] private FixtureSystem _fixtureSystem = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private EntityLookupSystem _lookup = default!; // Forge-Change

    // Forge-Change-start: conflict checks run on anchor / rotate / removal, not every tick.
    private readonly HashSet<EntityUid> _nozzleEnts = new();
    private readonly HashSet<Vector2i> _occupiedTiles = new();
    private readonly HashSet<Vector2i> _previousTiles = new();
    private readonly HashSet<Vector2i> _scanTiles = new();
    private readonly HashSet<Vector2i> _fixtureTiles = new();
    private readonly List<Vector2i> _nozzleTiles = new();
    private readonly List<EntityUid> _conflictList = new();
    private bool _refreshingConflicts;
    // Forge-Change-end

    // Essentially whenever thruster enables we update the shuttle's available impulses which are used for movement.
    // This is done for each direction available.

    public const string BurnFixture = "thruster-burn";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ThrusterComponent, ActivateInWorldEvent>(OnActivateThruster);
        SubscribeLocalEvent<ThrusterComponent, ComponentInit>(OnThrusterInit);
        SubscribeLocalEvent<ThrusterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ThrusterComponent, ComponentShutdown>(OnThrusterShutdown);
        SubscribeLocalEvent<ThrusterComponent, PowerChangedEvent>(OnPowerChange);
        SubscribeLocalEvent<ThrusterComponent, AnchorStateChangedEvent>(OnAnchorChange);
        SubscribeLocalEvent<ThrusterComponent, MoveEvent>(OnRotate);
        SubscribeLocalEvent<ThrusterComponent, IsHotEvent>(OnIsHotEvent);
        SubscribeLocalEvent<ThrusterComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<ThrusterComponent, EndCollideEvent>(OnEndCollide);

        SubscribeLocalEvent<ThrusterComponent, ExaminedEvent>(OnThrusterExamine);

        SubscribeLocalEvent<ShuttleComponent, TileChangedEvent>(OnShuttleTileChange);

        SubscribeLocalEvent<ThrusterComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<ThrusterComponent, UpgradeExamineEvent>(OnUpgradeExamine);
        SubscribeLocalEvent<ThrusterComponent, SignalReceivedEvent>(OnSignalReceived); // Frontier
        SubscribeLocalEvent<ThrusterComponent, DamageChangedEvent>(OnThrusterDamaged); // Forge-Change
    }

    // Frontier: signal handler
    private void OnSignalReceived(EntityUid uid, ThrusterComponent component, ref SignalReceivedEvent args)
    {
        if (args.Port == component.OffPort)
            component.Enabled = false;
        else if (args.Port == component.OnPort)
            component.Enabled = true;
        else if (args.Port == component.TogglePort)
            component.Enabled ^= true;
        else
            return; // Invalid port, don't change the thruster.

        if (!component.Enabled)
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad != 0 && apcPower.Load != 1)
                apcPower.Load = 1;
            DisableThruster(uid, component);
        }
        else if (CanEnable(uid, component))
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad != apcPower.Load)
                apcPower.Load = component.OriginalLoad;
            EnableThruster(uid, component);
        }
    }
    // End Frontier: signal handler

    private void OnThrusterExamine(EntityUid uid, ThrusterComponent component, ExaminedEvent args)
    {
        // Powered is already handled by other power components
        var enabled = Loc.GetString(component.Enabled ? "thruster-comp-enabled" : "thruster-comp-disabled");

        using (args.PushGroup(nameof(ThrusterComponent)))
        {
            args.PushMarkup(enabled);

            if (EntityManager.TryGetComponent(uid, out TransformComponent? xform) && // Forge-Change
                xform.Anchored)
            {
                // Forge-Change-start: check if the thruster is stacked
                var stacked = HasStackedThruster(uid, xform);
                if (stacked)
                    args.PushMarkup(Loc.GetString("thruster-comp-stacked"));

                if (component.Type != ThrusterType.Linear)
                    return;

                // Forge-Change-end
                var nozzleLocalization = ContentLocalizationManager.FormatDirection(xform.LocalRotation.Opposite().ToWorldVec().GetDir()).ToLower();
                var nozzleDir = Loc.GetString("thruster-comp-nozzle-direction",
                    ("direction", nozzleLocalization));

                args.PushMarkup(nozzleDir);

                // Forge-Change-start: check if the nozzle is blocked by a thruster
                if (stacked)
                    return;

                string nozzleText;
                if (!NozzleExposed(uid, xform))
                    nozzleText = Loc.GetString("thruster-comp-nozzle-not-exposed");
                else if (NozzleBlockedByThruster(uid, xform))
                    nozzleText = Loc.GetString("thruster-comp-nozzle-blocked");
                else
                    nozzleText = Loc.GetString("thruster-comp-nozzle-exposed");
                // Forge-Change-end

                args.PushMarkup(nozzleText);
            }
        }
    }

    private void OnIsHotEvent(EntityUid uid, ThrusterComponent component, IsHotEvent args)
    {
        args.IsHot = component.Type != ThrusterType.Angular && component.IsOn;
    }

    private void OnShuttleTileChange(EntityUid uid, ShuttleComponent component, ref TileChangedEvent args)
    {
        var xformQuery = GetEntityQuery<TransformComponent>(); // Forge-Change
        var thrusterQuery = GetEntityQuery<ThrusterComponent>(); // Forge-Change

        foreach (var change in args.Changes)
        {
            // Forge-Change: Plating placed over space blocks every thruster whose exhaust covers that tile.
            if (_turf.IsSpace(change.NewTile) || !_turf.IsSpace(change.OldTile))
                continue;

            var tilePos = change.GridIndices;
            var grid = Comp<MapGridComponent>(uid);

            // Forge-Change-start: Large thrusters are two tiles wide, so the anchor can sit diagonally off the blocked tile.
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    var checkPos = tilePos + new Vector2i(x, y);
                    var enumerator = _mapSystem.GetAnchoredEntities(uid, grid, checkPos);

                    while (enumerator.MoveNext(out var ent))
                    {
                        if (!thrusterQuery.TryGetComponent(ent.Value, out var thruster) || !thruster.RequireSpace)
                            continue;

                        var xform = xformQuery.GetComponent(ent.Value);
                        GetNozzleTiles(ent.Value, xform, _nozzleTiles);
                        if (!_nozzleTiles.Contains(tilePos))
                            continue;
            // Forge-Change-end

                        DisableThruster(ent.Value, thruster, xform.GridUid);
                    }
                }
            }
        }
    }

    private void OnActivateThruster(EntityUid uid, ThrusterComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        component.Enabled ^= true;

        if (!component.Enabled)
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad != 0 && apcPower.Load != 1) // Frontier
                apcPower.Load = 1;  // Frontier
            DisableThruster(uid, component);
            args.Handled = true;
        }
        else if (CanEnable(uid, component))
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad != apcPower.Load) // Frontier
                apcPower.Load = component.OriginalLoad; // Frontier
            EnableThruster(uid, component);
            args.Handled = true;
        }
    }

    /// <summary>
    /// If the thruster rotates change the direction where the linear thrust is applied
    /// </summary>
    private void OnRotate(EntityUid uid, ThrusterComponent component, ref MoveEvent args)
    {
        // TODO: Disable visualizer for old direction
        // TODO: Don't make them rotatable and make it require anchoring.

        // Forge-Change-start: a turn can point the nozzle at another thruster, or swing a wide body off one.
        if (TryComp(uid, out TransformComponent? movedXform))
            RefreshConflictsAfterMove(uid, movedXform, args);
        // Forge-Change-end

        if (!component.Enabled ||
            !EntityManager.TryGetComponent(uid, out TransformComponent? xform) ||
            !EntityManager.TryGetComponent(xform.GridUid, out ShuttleComponent? shuttleComponent))
        {
            return;
        }

        var canEnable = CanEnable(uid, component);

        // If it's not on then don't enable it inadvertantly (given we don't have an old rotation)
        if (!canEnable && !component.IsOn)
            return;

        // Enable it if it was turned off but new tile is valid
        if (!component.IsOn && canEnable)
        {
            EnableThruster(uid, component);
            return;
        }

        // Disable if new tile invalid
        if (component.IsOn && !canEnable)
        {
            DisableThruster(uid, component, args.OldPosition.EntityId, xform, args.OldRotation);
            return;
        }

        var oldDirection = (int)args.OldRotation.GetCardinalDir() / 2;
        var direction = (int)args.NewRotation.GetCardinalDir() / 2;
        var oldShuttleComponent = shuttleComponent;

        if (args.ParentChanged)
        {
            oldShuttleComponent = Comp<ShuttleComponent>(args.OldPosition.EntityId);

            // If no parent change doesn't matter for angular.
            if (component.Type == ThrusterType.Angular)
            {
                oldShuttleComponent.AngularThrust -= component.Thrust;
                DebugTools.Assert(oldShuttleComponent.AngularThrusters.Contains(uid));
                oldShuttleComponent.AngularThrusters.Remove(uid);

                shuttleComponent.AngularThrust += component.Thrust;
                DebugTools.Assert(!shuttleComponent.AngularThrusters.Contains(uid));
                shuttleComponent.AngularThrusters.Add(uid);
                return;
            }
        }

        if (component.Type == ThrusterType.Linear)
        {
            oldShuttleComponent.LinearThrust[oldDirection] -= component.Thrust;
            oldShuttleComponent.BaseLinearThrust[oldDirection] -= component.BaseThrust;
            DebugTools.Assert(oldShuttleComponent.LinearThrusters[oldDirection].Contains(uid));
            oldShuttleComponent.LinearThrusters[oldDirection].Remove(uid);

            shuttleComponent.LinearThrust[direction] += component.Thrust;
            shuttleComponent.BaseLinearThrust[direction] += component.BaseThrust;
            DebugTools.Assert(!shuttleComponent.LinearThrusters[direction].Contains(uid));
            shuttleComponent.LinearThrusters[direction].Add(uid);
        }
    }

    private void OnAnchorChange(EntityUid uid, ThrusterComponent component, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored && CanEnable(uid, component))
        {
            EnableThruster(uid, component);
        }
        else
        {
            DisableThruster(uid, component);
        }

        // Forge-Change-start: installing or unwrenching changes who shares the tile and who the nozzle hits.
        if (TryComp(uid, out TransformComponent? xform))
            RefreshConflictingThrusters(uid, xform);
        // Forge-Change-end
    }

    private void OnThrusterInit(EntityUid uid, ThrusterComponent component, ComponentInit args)
    {
        // Frontier: togglable thrusters
        if (TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) && component.OriginalLoad == 0)
        {
            component.OriginalLoad = apcPower.Load;
        }
        // End Frontier: togglable thrusters

        _ambient.SetAmbience(uid, false);

        if (!component.Enabled)
        {
            return;
        }

        if (CanEnable(uid, component))
        {
            EnableThruster(uid, component);
        }
    }

    private void OnMapInit(Entity<ThrusterComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextFire = _timing.CurTime + ent.Comp.FireCooldown;
    }

    private void OnThrusterShutdown(EntityUid uid, ThrusterComponent component, ComponentShutdown args)
    {
        DisableThruster(uid, component);

        // Forge-Change-start: removal is the moment a stacked or blocked neighbor may fire again.
        if (TryComp(uid, out TransformComponent? xform))
            RefreshConflictingThrusters(uid, xform);
        // Forge-Change-end
    }

    private void OnPowerChange(EntityUid uid, ThrusterComponent component, ref PowerChangedEvent args)
    {
        if (args.Powered && CanEnable(uid, component))
        {
            EnableThruster(uid, component);
        }
        else
        {
            DisableThruster(uid, component);
        }
    }

    /// <summary>
    /// Tries to enable the thruster and turn it on. If it's already enabled it does nothing.
    /// </summary>
    public void EnableThruster(EntityUid uid, ThrusterComponent component, TransformComponent? xform = null)
    {
        if (component.IsOn ||
            !Resolve(uid, ref xform))
        {
            // Forge-Change: a repaired engine can already be IsOn with a leftover exhaust plume.
            if (component.IsOn)
                SyncThrusterFiring(uid, component, xform);
            return;
        }

        component.IsOn = true;

        if (!EntityManager.TryGetComponent(xform.GridUid, out ShuttleComponent? shuttleComponent))
        {
            SyncThrusterFiring(uid, component, xform); // Forge-Change
            return;
        }

        // Logger.DebugS("thruster", $"Enabled thruster {uid}");

        switch (component.Type)
        {
            case ThrusterType.Linear:
                var direction = (int)xform.LocalRotation.GetCardinalDir() / 2;

                shuttleComponent.LinearThrust[direction] += component.Thrust;
                shuttleComponent.BaseLinearThrust[direction] += component.BaseThrust;
                DebugTools.Assert(!shuttleComponent.LinearThrusters[direction].Contains(uid));
                shuttleComponent.LinearThrusters[direction].Add(uid);

                // Don't just add / remove the fixture whenever the thruster fires because perf
                if (EntityManager.TryGetComponent(uid, out PhysicsComponent? physicsComponent) &&
                    component.BurnPoly.Count > 0)
                {
                    var shape = new PolygonShape();
                    shape.Set(component.BurnPoly);
                    _fixtureSystem.TryCreateFixture(uid, shape, BurnFixture, hard: false, collisionLayer: (int)CollisionGroup.FullTileMask, body: physicsComponent);
                }

                break;
            case ThrusterType.Angular:
                shuttleComponent.AngularThrust += component.Thrust;
                DebugTools.Assert(!shuttleComponent.AngularThrusters.Contains(uid));
                shuttleComponent.AngularThrusters.Add(uid);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (EntityManager.TryGetComponent(uid, out AppearanceComponent? appearance))
        {
            _appearance.SetData(uid, ThrusterVisualState.State, true, appearance);
        }

        if (_light.TryGetLight(uid, out var pointLightComponent))
        {
            _light.SetEnabled(uid, true, pointLightComponent);
        }

        _ambient.SetAmbience(uid, true);
        RefreshCenter(uid, shuttleComponent);
        SyncThrusterFiring(uid, component, xform); // Forge-Change: don't inherit a plume unless the shuttle is actually thrusting
    }

    /// <summary>
    /// Refreshes the center of thrust for movement calculations.
    /// </summary>
    private void RefreshCenter(EntityUid uid, ShuttleComponent shuttle)
    {
        // TODO: Only refresh relevant directions.
        var center = Vector2.Zero;
        var thrustQuery = GetEntityQuery<ThrusterComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        foreach (var dir in new[]
                     { Direction.South, Direction.East, Direction.North, Direction.West })
        {
            var index = (int)dir / 2;
            var pop = shuttle.LinearThrusters[index];
            var totalThrust = 0f;

            foreach (var ent in pop)
            {
                if (!thrustQuery.TryGetComponent(ent, out var thruster) || !xformQuery.TryGetComponent(ent, out var xform))
                    continue;

                center += xform.LocalPosition * thruster.Thrust;
                totalThrust += thruster.Thrust;
            }

            center /= pop.Count * totalThrust;
            shuttle.CenterOfThrust[index] = center;
        }
    }

    public void DisableThruster(EntityUid uid, ThrusterComponent component, TransformComponent? xform = null, Angle? angle = null)
    {
        if (!Resolve(uid, ref xform)) return;
        DisableThruster(uid, component, xform.GridUid, xform);
    }

    /// <summary>
    /// Tries to disable the thruster.
    /// </summary>
    public void DisableThruster(EntityUid uid, ThrusterComponent component, EntityUid? gridId, TransformComponent? xform = null, Angle? angle = null)
    {
        if (!component.IsOn ||
            !Resolve(uid, ref xform))
        {
            return;
        }

        component.IsOn = false;

        if (!EntityManager.TryGetComponent(gridId, out ShuttleComponent? shuttleComponent))
            return;

        // Logger.DebugS("thruster", $"Disabled thruster {uid}");

        switch (component.Type)
        {
            case ThrusterType.Linear:
                angle ??= xform.LocalRotation;
                var direction = (int)angle.Value.GetCardinalDir() / 2;

                shuttleComponent.LinearThrust[direction] -= component.Thrust;
                shuttleComponent.BaseLinearThrust[direction] -= component.BaseThrust;
                DebugTools.Assert(shuttleComponent.LinearThrusters[direction].Contains(uid));
                shuttleComponent.LinearThrusters[direction].Remove(uid);
                break;
            case ThrusterType.Angular:
                shuttleComponent.AngularThrust -= component.Thrust;
                DebugTools.Assert(shuttleComponent.AngularThrusters.Contains(uid));
                shuttleComponent.AngularThrusters.Remove(uid);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (EntityManager.TryGetComponent(uid, out AppearanceComponent? appearance))
        {
            _appearance.SetData(uid, ThrusterVisualState.State, false, appearance);
        }

        if (_light.TryGetLight(uid, out var pointLightComponent))
        {
            _light.SetEnabled(uid, false, pointLightComponent);
        }

        _ambient.SetAmbience(uid, false);

        if (EntityManager.TryGetComponent(uid, out PhysicsComponent? physicsComponent))
        {
            _fixtureSystem.DestroyFixture(uid, BurnFixture, body: physicsComponent);
        }

        component.Colliding.Clear();
        SetThrusterFiring(uid, component, false); // Forge-Change: DisableThruster used to leave Firing/Thrusting set
        RefreshCenter(uid, shuttleComponent);
    }

    public bool CanEnable(EntityUid uid, ThrusterComponent component)
    {
        if (!component.Enabled)
            return false;

        if (component.LifeStage > ComponentLifeStage.Running)
            return false;

        var xform = Transform(uid);

        if (!xform.Anchored || !this.IsPowered(uid, EntityManager))
        {
            return false;
        }

        // Forge-Change-start: two or more thrusters on one tile never produce thrust.
        if (HasStackedThruster(uid, xform))
            return false;
        // Forge-Change-end

        if (!component.RequireSpace)
            return true;

        // Forge-Change-start:
        // Plating blocks the nozzle here. Tile placement itself is handled by OnShuttleTileChange,
        // so neither check runs every tick. Wide thrusters must clear every tile in front of the body.
        return NozzleExposed(uid, xform) && !NozzleBlockedByThruster(uid, xform);
    }

    private bool NozzleExposed(EntityUid uid, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return true;

        GetNozzleTiles(uid, xform, _nozzleTiles);
        foreach (var tile in _nozzleTiles)
        {
            var tileRef = _mapSystem.GetTileRef(gridUid, grid, tile);
            if (!_turf.IsSpace(tileRef))
                return false;
        }

        return true;
    }
        // Forge-Change-end

    // Forge-Change-Start: stacked thrusters and thrusters exhausting into each other.
    /// <summary>
    /// Tiles directly in front of the thruster body, one step along the exhaust.
    /// A 1x1 thruster yields one tile. A 2x2 thruster yields both tiles along its nose.
    /// </summary>
    private void GetNozzleTiles(EntityUid uid, TransformComponent xform, List<Vector2i> tiles)
    {
        tiles.Clear();
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var nozzleVec = xform.LocalRotation.Opposite().ToWorldVec();
        var nozzleDir = new Vector2i((int)Math.Round(nozzleVec.X), (int)Math.Round(nozzleVec.Y));
        if (nozzleDir == Vector2i.Zero)
            return;

        _fixtureTiles.Clear();
        CollectFixtureTiles(uid, xform.LocalPosition, xform.LocalRotation, grid, _fixtureTiles);

        if (_fixtureTiles.Count == 0)
        {
            var anchor = _mapSystem.CoordinatesToTile(gridUid, grid, xform.Coordinates);
            tiles.Add(anchor + nozzleDir);
            return;
        }

        foreach (var bodyTile in _fixtureTiles)
        {
            var ahead = bodyTile + nozzleDir;
            if (_fixtureTiles.Contains(ahead) || tiles.Contains(ahead))
                continue;

            tiles.Add(ahead);
        }
    }

    private bool IsBlockingThruster(EntityUid uid)
    {
        if (!TryComp<ThrusterComponent>(uid, out var thruster))
            return false;

        if (thruster.LifeStage > ComponentLifeStage.Running || TerminatingOrDeleted(uid))
            return false;

        return Transform(uid).Anchored;
    }

    private bool HasStackedThruster(EntityUid uid, TransformComponent xform)
    {
        if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var tile = _mapSystem.CoordinatesToTile(gridUid, grid, xform.Coordinates);
        var anchored = _mapSystem.GetAnchoredEntities(gridUid, grid, tile);
        while (anchored.MoveNext(out var other))
        {
            if (other.Value != uid && IsBlockingThruster(other.Value))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when another anchored thruster occupies any exhaust tile.
    /// Wide thrusters count if their solid body covers that tile, even when their anchor is elsewhere.
    /// </summary>
    private bool NozzleBlockedByThruster(EntityUid uid, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        GetNozzleTiles(uid, xform, _nozzleTiles);
        foreach (var tile in _nozzleTiles)
        {
            var anchored = _mapSystem.GetAnchoredEntities(gridUid, grid, tile);
            while (anchored.MoveNext(out var other))
            {
                if (other.Value != uid && IsBlockingThruster(other.Value))
                    return true;
            }

            _nozzleEnts.Clear();
            _lookup.GetLocalEntitiesIntersecting(gridUid, tile, _nozzleEnts, gridComp: grid);
            foreach (var other in _nozzleEnts)
            {
                if (other == uid || !IsBlockingThruster(other))
                    continue;

                if (HardFixtureCoversTile(other, tile, grid))
                    return true;
            }
        }

        return false;
    }

    private bool HardFixtureCoversTile(EntityUid uid, Vector2i tile, MapGridComponent grid)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        _fixtureTiles.Clear();
        CollectFixtureTiles(uid, xform.LocalPosition, xform.LocalRotation, grid, _fixtureTiles);
        return _fixtureTiles.Contains(tile);
    }

    private void CollectFixtureTiles(EntityUid uid, Vector2 localPosition, Angle rotation, MapGridComponent grid, HashSet<Vector2i> tiles)
    {
        if (!TryComp<FixturesComponent>(uid, out var fixtures))
            return;

        var tileSize = grid.TileSize;
        var xf = new Transform(localPosition, rotation);
        foreach (var (id, fixture) in fixtures.Fixtures)
        {
            if (id == BurnFixture || !fixture.Hard)
                continue;

            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var aabb = fixture.Shape.ComputeAABB(xf, i);
                var x0 = (int)Math.Floor(aabb.Left / tileSize);
                var y0 = (int)Math.Floor(aabb.Bottom / tileSize);
                var x1 = (int)Math.Floor((aabb.Right - 0.001f) / tileSize);
                var y1 = (int)Math.Floor((aabb.Top - 0.001f) / tileSize);
                for (var x = x0; x <= x1; x++)
                {
                    for (var y = y0; y <= y1; y++)
                        tiles.Add(new Vector2i(x, y));
                }
            }
        }
    }

    private void RefreshConflictsAfterMove(EntityUid uid, TransformComponent xform, MoveEvent args)
    {
        Vector2i? previousAnchor = null;
        _previousTiles.Clear();

        if (args.OldPosition.EntityId == xform.ParentUid &&
            xform.GridUid is { } gridUid &&
            TryComp<MapGridComponent>(gridUid, out var grid))
        {
            previousAnchor = _mapSystem.CoordinatesToTile(gridUid, grid, args.OldPosition);
            _previousTiles.Add(previousAnchor.Value);
            CollectFixtureTiles(uid, args.OldPosition.Position, args.OldRotation, grid, _previousTiles);
        }

        RefreshConflictingThrusters(uid, xform, previousAnchor);
    }

    /// <summary>
    /// Re-evaluates thrusters that share a tile with <paramref name="uid"/> or exhaust into its body.
    /// Called when a thruster is anchored, rotated, or removed.
    /// </summary>
    private void RefreshConflictingThrusters(EntityUid uid, TransformComponent xform, Vector2i? previousAnchor = null)
    {
        if (_refreshingConflicts)
            return;

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        _refreshingConflicts = true;
        try
        {
            var anchor = _mapSystem.CoordinatesToTile(gridUid, grid, xform.Coordinates);
            _occupiedTiles.Clear();
            _occupiedTiles.Add(anchor);
            CollectFixtureTiles(uid, xform.LocalPosition, xform.LocalRotation, grid, _occupiedTiles);
            if (previousAnchor != null)
            {
                foreach (var tile in _previousTiles)
                    _occupiedTiles.Add(tile);
            }

            _scanTiles.Clear();
            foreach (var tile in _occupiedTiles)
            {
                for (var x = -2; x <= 2; x++)
                {
                    for (var y = -2; y <= 2; y++)
                        _scanTiles.Add(new Vector2i(tile.X + x, tile.Y + y));
                }
            }

            _conflictList.Clear();
            foreach (var tile in _scanTiles)
            {
                var anchored = _mapSystem.GetAnchoredEntities(gridUid, grid, tile);
                while (anchored.MoveNext(out var other))
                {
                    if (other.Value == uid || !TryComp<ThrusterComponent>(other.Value, out var thruster))
                        continue;

                    if (thruster.LifeStage > ComponentLifeStage.Running)
                        continue;

                    var otherXform = Transform(other.Value);
                    var otherAnchor = _mapSystem.CoordinatesToTile(gridUid, grid, otherXform.Coordinates);
                    var sameTile = otherAnchor == anchor || previousAnchor is { } oldAnchor && otherAnchor == oldAnchor;
                    var exhaustsIntoUs = false;
                    if (thruster.Type == ThrusterType.Linear && thruster.RequireSpace)
                    {
                        GetNozzleTiles(other.Value, otherXform, _nozzleTiles);
                        foreach (var nozzle in _nozzleTiles)
                        {
                            if (!_occupiedTiles.Contains(nozzle))
                                continue;

                            exhaustsIntoUs = true;
                            break;
                        }
                    }

                    if (sameTile || exhaustsIntoUs)
                        _conflictList.Add(other.Value);
                }
            }

            foreach (var other in _conflictList)
            {
                if (!TryComp<ThrusterComponent>(other, out var thruster))
                    continue;

                if (CanEnable(other, thruster))
                    EnableThruster(other, thruster);
                else
                    DisableThruster(other, thruster);
            }
        }
        finally
        {
            _refreshingConflicts = false;
        }
    }
    // Forge-Change-End

    #region Burning

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ThrusterComponent>();
        var curTime = _timing.CurTime;

        while (query.MoveNext(out var comp))
        {
            if (comp.NextFire > curTime)
                continue;

            comp.NextFire += comp.FireCooldown;

            if (!comp.Firing || comp.Colliding.Count == 0 || comp.Damage == null)
                continue;

            foreach (var uid in comp.Colliding.ToArray())
            {
                _damageable.TryChangeDamage(uid, comp.Damage);
            }
        }
    }

    private void OnStartCollide(EntityUid uid, ThrusterComponent component, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != BurnFixture)
            return;

        component.Colliding.Add(args.OtherEntity);
    }

    private void OnEndCollide(EntityUid uid, ThrusterComponent component, ref EndCollideEvent args)
    {
        if (args.OurFixtureId != BurnFixture)
            return;

        component.Colliding.Remove(args.OtherEntity);
    }

    /// <summary>
    /// Considers a thrust direction as being active.
    /// </summary>
    public void EnableLinearThrustDirection(ShuttleComponent component, DirectionFlag direction)
    {
        if ((component.ThrustDirections & direction) != 0x0)
            return;

        component.ThrustDirections |= direction;

        var index = GetFlagIndex(direction);
        var appearanceQuery = GetEntityQuery<AppearanceComponent>();
        var thrusterQuery = GetEntityQuery<ThrusterComponent>();

        foreach (var uid in component.LinearThrusters[index])
        {
            if (!thrusterQuery.TryGetComponent(uid, out var comp))
                continue;

            comp.Firing = true;
            appearanceQuery.TryGetComponent(uid, out var appearance);
            _appearance.SetData(uid, ThrusterVisualState.Thrusting, true, appearance);
        }
    }

    /// <summary>
    /// Disables a thrust direction.
    /// </summary>
    public void DisableLinearThrustDirection(ShuttleComponent component, DirectionFlag direction)
    {
        if ((component.ThrustDirections & direction) == 0x0)
            return;

        component.ThrustDirections &= ~direction;

        var index = GetFlagIndex(direction);
        var appearanceQuery = GetEntityQuery<AppearanceComponent>();
        var thrusterQuery = GetEntityQuery<ThrusterComponent>();

        foreach (var uid in component.LinearThrusters[index])
        {
            if (!thrusterQuery.TryGetComponent(uid, out var comp))
                continue;

            appearanceQuery.TryGetComponent(uid, out var appearance);
            comp.Firing = false;
            _appearance.SetData(uid, ThrusterVisualState.Thrusting, false, appearance);
        }
    }

    public void DisableLinearThrusters(ShuttleComponent component)
    {
        foreach (DirectionFlag dir in Enum.GetValues(typeof(DirectionFlag)))
        {
            DisableLinearThrustDirection(component, dir);
        }

        DebugTools.Assert(component.ThrustDirections == DirectionFlag.None);
    }

    public void SetAngularThrust(ShuttleComponent component, bool on)
    {
        var appearanceQuery = GetEntityQuery<AppearanceComponent>();
        var thrusterQuery = GetEntityQuery<ThrusterComponent>();

        if (on)
        {
            foreach (var uid in component.AngularThrusters)
            {
                if (!thrusterQuery.TryGetComponent(uid, out var comp))
                    continue;

                appearanceQuery.TryGetComponent(uid, out var appearance);
                comp.Firing = true;
                _appearance.SetData(uid, ThrusterVisualState.Thrusting, true, appearance);
            }
        }
        else
        {
            foreach (var uid in component.AngularThrusters)
            {
                if (!thrusterQuery.TryGetComponent(uid, out var comp))
                    continue;

                appearanceQuery.TryGetComponent(uid, out var appearance);
                comp.Firing = false;
                _appearance.SetData(uid, ThrusterVisualState.Thrusting, false, appearance);
            }
        }
    }

    private void OnRefreshParts(EntityUid uid, ThrusterComponent component, RefreshPartsEvent args)
    {
        if (component.IsOn) // safely disable thruster to prevent negative thrust
            DisableThruster(uid, component);

        var thrustRating = args.PartRatings[component.MachinePartThrust];

        component.Thrust = component.BaseThrust * MathF.Pow(component.PartRatingThrustMultiplier, thrustRating - 1);

        if (component.Enabled && CanEnable(uid, component))
            EnableThruster(uid, component);
    }

    private void OnUpgradeExamine(EntityUid uid, ThrusterComponent component, UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("thruster-comp-upgrade-thrust", component.Thrust / component.BaseThrust);
    }

    // Forge-Change-Start: repaired / re-powered thrusters used to keep a leftover exhaust plume (and burn damage)
    // even when the shuttle was docked and not thrusting.
    private void OnThrusterDamaged(EntityUid uid, ThrusterComponent component, DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            return;

        SyncThrusterFiring(uid, component);
    }

    /// <summary>
    /// Matches exhaust visuals and burn damage to whether this shuttle is actually thrusting that way.
    /// </summary>
    public void SyncThrusterFiring(EntityUid uid, ThrusterComponent? component = null, TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref component, ref xform, false))
            return;

        if (!component.IsOn || xform.GridUid == null || !TryComp(xform.GridUid.Value, out ShuttleComponent? shuttle))
        {
            SetThrusterFiring(uid, component, false);
            return;
        }

        if (component.Type == ThrusterType.Angular)
        {
            // Angular firing is reapplied every physics tick while rotating; turn it off on repair/power-up.
            SetThrusterFiring(uid, component, false);
            return;
        }

        var dirFlag = xform.LocalRotation.GetCardinalDir().AsFlag();
        SetThrusterFiring(uid, component, (shuttle.ThrustDirections & dirFlag) != DirectionFlag.None);
    }

    private void SetThrusterFiring(EntityUid uid, ThrusterComponent component, bool firing)
    {
        component.Firing = firing;
        if (TryComp(uid, out AppearanceComponent? appearance))
            _appearance.SetData(uid, ThrusterVisualState.Thrusting, firing, appearance);
    }
    // Forge-Change-End

    //private void OnEmpPulse(EntityUid uid, ThrusterComponent component, ref EmpPulseEvent args)
    //{
    //    if (component.Enabled && !component.ThrusterIgnoreEmp)
    //    {
    //        args.Affected = true;
    //        args.Disabled = true;
    //    }
    //}

    //[ByRefEvent]
    //public record struct ThrusterToggleAttemptEvent(bool Cancelled);

    #endregion

    private int GetFlagIndex(DirectionFlag flag)
    {
        return (int)Math.Log2((int)flag);
    }
}
