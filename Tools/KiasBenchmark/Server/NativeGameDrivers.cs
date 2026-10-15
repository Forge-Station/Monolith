using System.Numerics;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mind;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Item.ItemToggle;
using Content.Server.Medical;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server.Benchmark;

[RegisterComponent]
public sealed partial class NativeLabCollisionObserverComponent : Component { }

[RegisterComponent]
public sealed partial class NativeLabGunObserverComponent : Component { }

[RegisterComponent]
public sealed partial class NativeLabProjectileObserverComponent : Component { }

[RegisterComponent]
public sealed partial class NativeLabRadioObserverComponent : Component { }

[RegisterComponent]
public sealed partial class NativeLabNavigationObserverComponent : Component { }

public sealed class NativeGameObserverSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    public readonly Dictionary<EntityUid, uint> FtlCompleted = new();
    public readonly Dictionary<EntityUid, (uint Tick, EntityUid[] Projectiles)> Shots = new();
    public readonly Dictionary<EntityUid, (uint Tick, EntityUid Target, string Damage)> Hits = new();
    public readonly Dictionary<EntityUid, (uint Tick, EntityUid Other, float Speed)> Collisions = new();
    public sealed record CollisionContacts(uint Tick, List<object> Contacts);
    public readonly Dictionary<EntityUid, CollisionContacts> CollisionGeometry = new();
    public readonly Dictionary<EntityUid, int> RadioReceived = new();
    public readonly Dictionary<EntityUid, uint> NavigationCompleted = new();
    public readonly Dictionary<EntityUid, uint> ExpiredProjectiles = new();
    public readonly Dictionary<EntityUid, (uint Tick, EntityUid Grid, EntityUid Interceptor, float Distance)> InterceptedProjectiles = new();
    public readonly Dictionary<EntityUid, (uint Tick, EntityUid Alarm)> FireAlarms = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtl);
        SubscribeLocalEvent<NativeLabGunObserverComponent, AmmoShotEvent>(OnShot);
        SubscribeLocalEvent<NativeLabProjectileObserverComponent, Content.Shared.Projectiles.ProjectileHitEvent>(OnHit);
        SubscribeLocalEvent<NativeLabCollisionObserverComponent, StartCollideEvent>(OnCollision,
            before: new[] { typeof(ShuttleSystem) });
        SubscribeLocalEvent<NativeLabRadioObserverComponent, Content.Server.Radio.RadioReceiveEvent>(OnRadio);
        SubscribeLocalEvent<NativeLabNavigationObserverComponent, Content.Server._Mono.NPC.HTN.Operators.SteeringDoneEvent>(OnNavigation);
        SubscribeLocalEvent<NativeLabProjectileObserverComponent, Robust.Shared.Spawners.TimedDespawnEvent>(OnProjectileExpired);
        SubscribeLocalEvent<Content.Server.Atmos.Monitor.Components.FireAlarmComponent, Content.Server.Atmos.Monitor.Systems.AtmosAlarmEvent>(OnAtmosAlarm);
    }

    private void OnShot(EntityUid uid, NativeLabGunObserverComponent component, AmmoShotEvent args)
    {
        var projectiles = args.FiredProjectiles.ToArray();
        Shots[uid] = (_timing.CurTick.Value, projectiles);
        foreach (var projectile in projectiles) EnsureComp<NativeLabProjectileObserverComponent>(projectile);
    }

    private void OnHit(EntityUid uid, NativeLabProjectileObserverComponent component, ref Content.Shared.Projectiles.ProjectileHitEvent args)
        => Hits[uid] = (_timing.CurTick.Value, args.Target, args.Damage.GetTotal().ToString());
    private void OnProjectileExpired(EntityUid uid, NativeLabProjectileObserverComponent component, ref Robust.Shared.Spawners.TimedDespawnEvent args)
        => ExpiredProjectiles[uid] = _timing.CurTick.Value;
    private void OnAtmosAlarm(EntityUid source, Content.Server.Atmos.Monitor.Components.FireAlarmComponent component,
        Content.Server.Atmos.Monitor.Systems.AtmosAlarmEvent args)
    {
        if (args.AlarmType == Content.Shared.Atmos.Monitor.AtmosAlarmType.Danger
            && Transform(source).GridUid is { } grid) FireAlarms[grid] = (_timing.CurTick.Value, source);
    }

    private void OnFtl(ref FTLCompletedEvent args) => FtlCompleted[args.Entity] = _timing.CurTick.Value;
    private void OnRadio(EntityUid uid, NativeLabRadioObserverComponent component, ref Content.Server.Radio.RadioReceiveEvent args)
        => RadioReceived[uid] = RadioReceived.GetValueOrDefault(uid) + 1;
    private void OnNavigation(EntityUid uid, NativeLabNavigationObserverComponent component, ref Content.Server._Mono.NPC.HTN.Operators.SteeringDoneEvent args)
        => NavigationCompleted[uid] = _timing.CurTick.Value;
    private void OnCollision(EntityUid uid, NativeLabCollisionObserverComponent component, ref StartCollideEvent args)
    {
        if (!HasComp<MapGridComponent>(args.OtherEntity)) return;
        if (!CollisionGeometry.TryGetValue(uid, out var trace) || trace.Tick != _timing.CurTick.Value)
        {
            trace = new(_timing.CurTick.Value, new());
            CollisionGeometry[uid] = trace;
        }
        if (trace.Contacts.Count < 16) trace.Contacts.Add(new { other = args.OtherEntity.ToString(), worldNormal = args.WorldNormal.ToString(),
            worldPoints = args.WorldPoints.ToArray().Select(point => point.ToString()).ToArray(),
            normalVelocity = MathF.Abs(Vector2.Dot(args.OurBody.LinearVelocity - args.OtherBody.LinearVelocity, args.WorldNormal)),
            ourAngularVelocity = args.OurBody.AngularVelocity, otherAngularVelocity = args.OtherBody.AngularVelocity });
        Collisions[uid] = (_timing.CurTick.Value, args.OtherEntity,
            (args.OurBody.LinearVelocity - args.OtherBody.LinearVelocity).Length());
    }
}

public static partial class NativeLab
{
    private sealed record PendingDriver(Input Input, uint Started, uint Deadline, Func<object?> Observe, Func<object>? Diagnostics);
    private static readonly List<PendingDriver> PendingGameDrivers = new();
    private static readonly Dictionary<EntityUid, EntityUid> ForeignGrids = new();
    private static readonly Dictionary<EntityUid, EntityUid> OperatedDoors = new();
    private static readonly Dictionary<EntityUid, Tile> BreachedFloors = new();
    public static Func<EntityUid, string, string, bool, object?>? RoomProbe;
    public static Func<EntityUid, EntityUid, string, bool, object?>? WorldProbe;
    private static readonly Dictionary<EntityUid, EntityUid> Visitors = new(), Fauna = new();
    private static readonly Dictionary<EntityUid, EntityUid> VisitorGuns = new();
    private static readonly Dictionary<EntityUid, EntityUid[]> AnomalyPulseEntities = new();
    private static readonly Dictionary<EntityUid, Dictionary<Vector2i, Tile>> AnomalyFloors = new();
    private static readonly Dictionary<EntityUid, (EntityUid Grid, EntityUid Actor, EntityUid Drive, EntityUid Console)> FtlVisitors = new();

    private static object NavigateNative(Ship ship, Input input)
    {
        var transforms = _em.System<SharedTransformSystem>();
        var consoles = new List<EntityUid>();
        var query = _em.AllEntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
            if (transform.GridUid == ship.Grid && _em.HasComponent<Content.Server.NPC.HTN.HTNComponent>(uid)
                && _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().IsPowered(uid)) consoles.Add(uid);
        var console = consoles.OrderBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.X)
            .ThenBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.Y).First();
        _em.EnsureComponent<NativeLabNavigationObserverComponent>(console);
        var origin = transforms.GetWorldPosition(ship.Grid);
        var rotation = transforms.GetWorldRotation(ship.Grid);
        var destination = origin + new Vector2(80, 0);
        var started = _timing.CurTick.Value;
        WorldProbe?.Invoke(ship.Grid, console, input.Type, true);
        _em.EventBus.RaiseLocalEvent(console, new Content.Shared._Mono.Shuttles.ShuttleConsoleAutopilotPositionMessage {
            Actor = ship.Actor, Entity = _em.GetNetEntity(console),
            UiKey = Content.Shared.Shuttles.Components.ShuttleConsoleUiKey.Key,
            Coordinates = new MapCoordinates(destination, transforms.GetMapCoordinates(ship.Grid).MapId),
            Angle = rotation - MathF.PI });
        return AwaitNative(input, 12000, () =>
        {
            var body = _em.GetComponent<PhysicsComponent>(ship.Grid);
            var actual = transforms.GetWorldPosition(ship.Grid);
            if (!_em.System<NativeGameObserverSystem>().NavigationCompleted.TryGetValue(console, out var completed)
                || completed < started || Vector2.Distance(actual, origin) < 20
                || Vector2.Distance(actual, destination) > 45 || body.LinearVelocity.Length() > .2f
                || _em.HasComponent<Content.Shared.Shuttles.Components.FTLComponent>(ship.Grid)) return null;
            var observation = WorldProbe?.Invoke(ship.Grid, console, input.Type, false);
            if (WorldProbe != null && observation == null) return null;
            var displacement = Vector2.Distance(actual, origin);
            transforms.SetWorldPosition(ship.Grid, origin);
            transforms.SetWorldRotation(ship.Grid, rotation);
            return new { actualNativeAutopilotArrivalTick = completed, physicalDisplacement = displacement,
                arrivalDistance = Vector2.Distance(actual, destination), nativeArrivalRange = 40,
                fixturePositionResetAfterObservedArrival = true, observation };
        }, () => new { position = transforms.GetWorldPosition(ship.Grid).ToString(), destination = destination.ToString(),
            velocity = _em.GetComponent<PhysicsComponent>(ship.Grid).LinearVelocity.ToString() });
    }

    private static object VisitByFtl(Ship ship, Input input)
    {
        var outbound = input.Type == "ftl.visitor_out";
        var transforms = _em.System<SharedTransformSystem>();
        if (!outbound)
        {
            var foreign = CreateForeign(ship, new Vector2(1500, 1500));
            _em.SpawnEntity("APCHyperCapacity", new EntityCoordinates(foreign, new Vector2(2.5f, .5f)));
            var drive = _em.SpawnEntity("MachineFTLDrive", new EntityCoordinates(foreign, new Vector2(.5f, .5f)));
            var console = _em.SpawnEntity("ComputerShuttle", new EntityCoordinates(foreign, new Vector2(1.5f, .5f)));
            var actor = _em.SpawnEntity("MobHuman", new EntityCoordinates(foreign, new Vector2(1.5f, 1.5f)));
            AttachTrackedMind(actor, input.Id + "/pilot");
            _em.EnsureComponent<Content.Shared.Shuttles.Components.IFFComponent>(foreign);
            _em.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(foreign).CompanyName = "None";
            _em.EnsureComponent<Content.Shared.Shuttles.Components.ShuttleFactionComponent>(foreign).Faction = "NanoTrasen";
            FtlVisitors.Add(ship.Grid, (foreign, actor, drive, console));
        }
        var fixture = FtlVisitors[ship.Grid];
        WorldProbe?.Invoke(ship.Grid, fixture.Grid, input.Type, true);
        var destination = transforms.GetWorldPosition(ship.Grid) + (outbound ? new Vector2(1500, 1500) : new Vector2(750, 750));
        var map = transforms.GetMapCoordinates(ship.Grid).MapId;
        var requested = false;
        uint started = 0;
        return AwaitNative(input, 12000, () =>
        {
            var shuttle = _em.System<ShuttleSystem>();
            if (!requested)
            {
                var power = _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>();
                if (!power.IsPowered(fixture.Drive) || !power.IsPowered(fixture.Console)) return null;
                var distance = Vector2.Distance(transforms.GetWorldPosition(fixture.Grid), destination);
                if (distance > shuttle.GetFTLRange(fixture.Grid)
                    || !shuttle.CanFTL(fixture.Grid, out _) || !shuttle.CanFTLTo(fixture.Grid, map, fixture.Console))
                    throw new InvalidOperationException("Native visitor console rejected FTL range/mass/map constraints.");
                started = _timing.CurTick.Value;
                _em.EventBus.RaiseLocalEvent(fixture.Console, new Content.Shared.Shuttles.Events.ShuttleConsoleFTLPositionMessage {
                    Actor = fixture.Actor, Entity = _em.GetNetEntity(fixture.Console),
                    UiKey = Content.Shared.Shuttles.Components.ShuttleConsoleUiKey.Key,
                    Coordinates = new MapCoordinates(destination + _em.GetComponent<PhysicsComponent>(fixture.Grid).LocalCenter, map), Angle = Angle.Zero });
                if (!_em.HasComponent<Content.Shared.Shuttles.Components.FTLComponent>(fixture.Grid))
                    throw new InvalidOperationException("Powered visitor console did not start native FTL.");
                requested = true;
            }
            if (!_em.System<NativeGameObserverSystem>().FtlCompleted.TryGetValue(fixture.Grid, out var completed)
                || completed < started || transforms.GetMapCoordinates(fixture.Grid).MapId != map
                || Vector2.Distance(transforms.GetWorldPosition(fixture.Grid), destination) > 10) return null;
            var observation = WorldProbe?.Invoke(ship.Grid, fixture.Grid, input.Type, false);
            if (WorldProbe != null && observation == null) return null;
            if (outbound) { _em.DeleteEntity(fixture.Grid); FtlVisitors.Remove(ship.Grid); }
            return new { actualConsoleFtlCompleted = true, requestTick = started, completedTick = completed,
                intoSecondStationaryGridRadius = !outbound, poweredNativeDrive = true, observation };
        }, () => new { requested, started, drivePowered = _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().IsPowered(fixture.Drive),
            consolePowered = _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().IsPowered(fixture.Console),
            currentPosition = transforms.GetWorldPosition(fixture.Grid).ToString(), destination = destination.ToString() });
    }

    private sealed record VentFixture(EntityUid Vent, EntityCoordinates ActorPosition,
        Dictionary<Vector2i, Content.Shared.Atmos.GasMixture> Air, bool LockoutDisabled, TimeSpan LockoutUntil)
    {
        public bool Completed;
    }
    private static readonly Dictionary<EntityUid, VentFixture> VentFixtures = new();

#pragma warning disable RA0002
    private static HashSet<Content.Server.Atmos.TileAtmosphere>? NativeVentRoom(
        Content.Server.Atmos.Components.GridAtmosphereComponent state, Vector2i cell,
        Content.Server.Atmos.EntitySystems.AtmosphereSystem atmos)
    {
        if (!state.Tiles.TryGetValue(cell, out var start)) return null;
        var visited = new HashSet<Content.Server.Atmos.TileAtmosphere> { start };
        var pending = new Queue<Content.Server.Atmos.TileAtmosphere>();
        pending.Enqueue(start);
        while (pending.TryDequeue(out var tile))
        {
            if (tile.Space || tile.Air == null || state.InvalidatedCoords.Contains(tile.GridIndices)
                || tile.Air.Pressure < 80 || !atmos.IsMixtureProbablySafe(tile.Air)) return null;
            for (var direction = 0; direction < Content.Shared.Atmos.Atmospherics.Directions; direction++)
            {
                if ((tile.AdjacentBits & (Content.Shared.Atmos.AtmosDirection)(1 << direction)) == 0) continue;
                var adjacent = tile.AdjacentTiles[direction];
                if (adjacent == null) return null;
                if (visited.Add(adjacent)) pending.Enqueue(adjacent);
                if (visited.Count > 625) return null;
            }
        }
        return visited;
    }

    private static object ExerciseVent(Ship ship, Input input)
    {
        var atmos = _em.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
        var state = _em.GetComponent<Content.Server.Atmos.Components.GridAtmosphereComponent>(ship.Grid);
        var transforms = _em.System<SharedTransformSystem>();
        var nodes = _em.System<Content.Server.NodeContainer.EntitySystems.NodeContainerSystem>();
        if (input.Type == "atmos.restore")
        {
            var saved = VentFixtures[ship.Grid];
            var restored = false;
            return AwaitNative(input, 1800, () =>
            {
                if (!saved.Completed) return null;
                if (!restored)
                {
                    foreach (var (cell, mixture) in saved.Air)
                        (atmos.GetTileMixture((ship.Grid, null, null), null, cell, false)
                            ?? throw new InvalidOperationException("Native room air vanished before restoration.")).CopyFrom(mixture);
                    var vent = _em.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentPumpComponent>(saved.Vent);
                    vent.IsPressureLockoutManuallyDisabled = saved.LockoutDisabled;
                    vent.ManualLockoutReenabledAt = saved.LockoutUntil;
                    transforms.SetCoordinates(ship.Actor, saved.ActorPosition);
                    VentFixtures.Remove(ship.Grid);
                    restored = true;
                }
                return saved.Air.Keys.All(cell => state.Tiles.TryGetValue(cell, out var tile)
                        && tile.Air is { Pressure: >= 80 and <= 125 })
                    ? new { nativeRoomAirRestored = true, restoredCells = saved.Air.Count,
                        restoredManualOverride = true, restorationWaitedForObservedNativeFlow = true } : null;
            });
        }
        var candidates = new List<EntityUid>();
        var inventory = new List<object>();
        var rooms = new Dictionary<EntityUid, HashSet<Content.Server.Atmos.TileAtmosphere>>();
        var query = _em.AllEntityQueryEnumerator<Content.Server.Atmos.Piping.Unary.Components.GasVentPumpComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vent, out var transform))
        {
            if (transform.GridUid != ship.Grid) continue;
            var cell = new Vector2i((int)MathF.Floor(transform.LocalPosition.X), (int)MathF.Floor(transform.LocalPosition.Y));
            var powered = _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().IsPowered(uid);
            var hasPipe = nodes.TryGetNode(uid, vent.Inlet, out Content.Server.NodeContainer.Nodes.PipeNode? pipe);
            var room = NativeVentRoom(state, cell, atmos);
            state.Tiles.TryGetValue(cell, out var airTile);
            inventory.Add(new { vent = uid.ToString(), cell = cell.ToString(), powered, vent.Enabled,
                direction = vent.PumpDirection.ToString(), hasPipe, pipePressure = hasPipe ? pipe!.Air.Pressure : (float?)null,
                roomCells = room?.Count, pressure = airTile?.Air?.Pressure, temperature = airTile?.Air?.Temperature,
                space = airTile?.Space, invalidated = state.InvalidatedCoords.Contains(cell) });
            if (vent.Enabled
                && vent.PumpDirection == Content.Shared.Atmos.Piping.Unary.Components.VentPumpDirection.Releasing
                && powered && hasPipe && pipe!.Air.Pressure > 80 && room != null)
            { candidates.Add(uid); rooms.Add(uid, room); }
        }
        var selected = candidates.OrderBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.X)
            .ThenBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.Y).FirstOrDefault();
        if (!selected.Valid) throw new InvalidOperationException("No powered native vent with a pressurized pipe and safe sealed room: "
            + JsonSerializer.Serialize(inventory));
        var component = _em.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentPumpComponent>(selected);
        var position = _em.GetComponent<TransformComponent>(selected).LocalPosition;
        var start = state.Tiles[new Vector2i((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y))];
        var visited = rooms[selected];
        var air = visited.ToDictionary(tile => tile.GridIndices, tile => tile.Air!.Clone());
        if (visited.Any(tile => tile.Air!.Pressure < 80))
            throw new InvalidOperationException("Native vent fixture must begin with an already pressurized room.");
        VentFixtures.Add(ship.Grid, new(selected, _em.GetComponent<TransformComponent>(ship.Actor).Coordinates,
            air, component.IsPressureLockoutManuallyDisabled, component.ManualLockoutReenabledAt));
        foreach (var tile in visited) tile.Air!.RemoveRatio(1 - 60 / tile.Air.Pressure);
        transforms.SetCoordinates(ship.Actor, _em.GetComponent<TransformComponent>(selected).Coordinates);
        var begun = false;
        var flowObserved = false;
        nodes.TryGetNode(selected, component.Inlet, out Content.Server.NodeContainer.Nodes.PipeNode? supply);
        var previousSupply = supply!.Air.TotalMoles;
        var previousRoom = visited.Sum(tile => tile.Air!.TotalMoles);
        return AwaitNative(input, 1800, () =>
        {
            var roomMoles = visited.Sum(tile => tile.Air!.TotalMoles);
            var supplyMoles = supply.Air.TotalMoles;
            flowObserved |= roomMoles > previousRoom + .001f && supplyMoles < previousSupply - .001f;
            previousRoom = roomMoles; previousSupply = supplyMoles;
            if (!begun)
            {
                if (!component.UnderPressureLockout) return null;
                var action = new Content.Shared.DoAfter.DoAfterArgs(_em, ship.Actor, component.ManualLockoutDisableDoAfter,
                    new Content.Shared.Atmos.Piping.Unary.VentScrewedDoAfterEvent(), selected, selected)
                    { BreakOnDamage = true, NeedHand = true, BreakOnMove = true, BreakOnWeightlessMove = true };
                if (!_em.System<Content.Shared.DoAfter.SharedDoAfterSystem>().TryStartDoAfter(action))
                    throw new InvalidOperationException("Native vent pressure-lockout interaction was rejected.");
                begun = true;
            }
            if (!component.IsPressureLockoutManuallyDisabled || !flowObserved || start.Air!.Pressure < 80) return null;
            VentFixtures[ship.Grid].Completed = true;
            return new { nativeManualVentInteraction = true, nativePipeToRoomFlow = true, roomCells = visited.Count,
                initialPressure = 60, recoveredPressure = start.Air.Pressure, supplyPressure = supply.Air.Pressure };
        }, () => new { begun, flowObserved, pressure = start.Air!.Pressure, component.UnderPressureLockout,
            component.IsPressureLockoutManuallyDisabled, supplyPressure = supply.Air.Pressure });
    }
#pragma warning restore RA0002

    private static readonly Dictionary<EntityUid, List<(EntityUid Device, bool Disabled)>> CollisionShields = new();
    private sealed record CollisionEntity(EntityUid Uid, string Prototype, Vector2 Position, Angle Rotation,
        DamageSpecifier? Damage);
    private sealed record CollisionRepair(Dictionary<Vector2i, Tile> Tiles, List<CollisionEntity> Entities);
    private static readonly Dictionary<EntityUid, CollisionRepair> CollisionRepairs = new();
    private static readonly Dictionary<EntityUid, (Dictionary<Vector2i, Tile> Tiles, List<EntityUid> Walls)> CollisionExtensions = new();

    private static object ExerciseCollision(Ship ship, Input input)
    {
        if (ForeignGrids.ContainsKey(ship.Grid)) throw new InvalidOperationException("Collision fixture overlaps another native fixture.");
        var transforms = _em.System<SharedTransformSystem>();
        var physics = _em.System<SharedPhysicsSystem>();
        var observer = _em.System<NativeGameObserverSystem>();
        if (input.FixtureRotationDegrees is { } fixtureRotation)
            transforms.SetWorldRotation(ship.Grid, Angle.FromDegrees(fixtureRotation));
        var origin = transforms.GetWorldPosition(ship.Grid);
        var rotation = transforms.GetWorldRotation(ship.Grid);
        var impactDirection = rotation.RotateVec(-Vector2.UnitX);
        if (input.FixtureRotationDegrees is { } requested
            && Math.Abs(((rotation.Degrees - requested + 180) % 360 + 360) % 360 - 180) > .001)
            throw new InvalidOperationException("Collision fixture did not apply the requested rotation.");
        physics.SetLinearVelocity(ship.Grid, Vector2.Zero);
        physics.SetAngularVelocity(ship.Grid, 0);
        var bounds = _em.GetComponent<MapGridComponent>(ship.Grid).LocalAABB;
        var maps = _em.System<SharedMapSystem>();
        var ownMap = _em.GetComponent<MapGridComponent>(ship.Grid);
        KiasDriver?.Invoke(ship.Grid, "collision.prepare", true);
        var extensionTiles = new Dictionary<Vector2i, Tile>();
        var extensionWalls = new List<EntityUid>();
        var edge = (int)MathF.Ceiling(bounds.Right);
        var fixtureFloor = maps.GetTileRef(ship.Grid, ownMap, ship.FireTile).Tile;
        for (var x = edge; x < edge + 12; x++)
        for (var y = -2; y <= 2; y++)
        {
            var cell = new Vector2i(x, y);
            extensionTiles.Add(cell, maps.GetTileRef(ship.Grid, ownMap, cell).Tile);
            maps.SetTile((ship.Grid, ownMap), cell, fixtureFloor);
        }
        for (var y = 0; y < 3; y++)
            extensionWalls.Add(_em.SpawnEntity("WallPlastitanium", new EntityCoordinates(ship.Grid, new Vector2(edge + 11.5f, y + .5f))));
        CollisionExtensions.Add(ship.Grid, (extensionTiles, extensionWalls));
        var collisionEdge = edge + 12;
        var originalTiles = maps.GetAllTiles(ship.Grid, ownMap).ToDictionary(tile => tile.GridIndices, tile => tile.Tile);
        var repairEntities = new List<CollisionEntity>();
        var repairQuery = _em.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (repairQuery.MoveNext(out var repairUid, out var metadata, out var repairTransform))
            if (repairTransform.ParentUid == ship.Grid && repairTransform.Anchored
                && metadata.EntityPrototype is { } prototype)
                if (!extensionWalls.Contains(repairUid)) repairEntities.Add(new(repairUid, prototype.ID, repairTransform.LocalPosition, repairTransform.LocalRotation,
                    _em.TryGetComponent<DamageableComponent>(repairUid, out var savedDamage)
                        ? new DamageSpecifier { DamageDict = new(savedDamage.Damage.DamageDict) } : null));
        CollisionRepairs.Add(ship.Grid, new(originalTiles, repairEntities));
        var shields = new List<(EntityUid Device, bool Disabled)>();
        var shieldQuery = _em.AllEntityQueryEnumerator<Content.Shared._Crescent.ShipShields.ShipShieldEmitterComponent, TransformComponent>();
        while (shieldQuery.MoveNext(out var shield, out _, out var shieldTransform))
            if (shieldTransform.GridUid == ship.Grid)
            {
                shields.Add((shield, _em.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(shield).PowerDisabled));
                _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().SetPowerDisabled(shield, true);
            }
        CollisionShields.Add(ship.Grid, shields);
        var foreign = CreateForeign(ship, rotation.RotateVec(new Vector2(collisionEdge + 1, 0)));
        transforms.SetWorldRotation(foreign, rotation);
        ForeignGrids.Add(ship.Grid, foreign);
        _em.EnsureComponent<NativeLabCollisionObserverComponent>(ship.Grid);
        _em.EnsureComponent<NativeLabCollisionObserverComponent>(foreign);
        var original = new Dictionary<EntityUid, float>();
        var query = _em.AllEntityQueryEnumerator<DamageableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var damage, out var transform))
            if (transform.GridUid == ship.Grid && damage.Damage.DamageDict.ContainsKey("Structural")) original.Add(uid, (float)damage.TotalDamage);
        var started = _timing.CurTick.Value;
        var phase = 0;
        uint lowContact = 0, highStart = 0;
        float lowSpeed = 0;
        object? lowObservation = null;
        WorldProbe?.Invoke(ship.Grid, foreign, "collision.low", true);
        physics.SetLinearVelocity(foreign, impactDirection * .5f);
        return AwaitNative(input, 1200, () =>
        {
            if (phase == 0)
            {
                if (!observer.Collisions.TryGetValue(ship.Grid, out var contact) || contact.Tick < started || contact.Other != foreign) return null;
                if (contact.Speed <= .1f || contact.Speed >= 1) throw new InvalidOperationException("Native low-speed collision crossed its control threshold.");
                lowContact = contact.Tick; lowSpeed = contact.Speed;
                phase = 1;
            }
            if (phase == 1)
            {
                if (_timing.CurTick.Value < lowContact + 30) return null;
                if (original.Any(pair => !_em.EntityExists(pair.Key) || (float)_em.GetComponent<DamageableComponent>(pair.Key).TotalDamage > pair.Value + .001f))
                    throw new InvalidOperationException("Native low-speed collision unexpectedly damaged the ship.");
                if (originalTiles.Any(pair => maps.GetTileRef(ship.Grid, ownMap, pair.Key).Tile != pair.Value))
                    throw new InvalidOperationException("Native low-speed collision unexpectedly damaged a hull tile.");
                lowObservation = WorldProbe?.Invoke(ship.Grid, foreign, "collision.low", false);
                if (WorldProbe != null && lowObservation == null) return null;
                physics.SetLinearVelocity(ship.Grid, Vector2.Zero);
                physics.SetAngularVelocity(ship.Grid, 0);
                transforms.SetWorldPosition(ship.Grid, origin);
                transforms.SetWorldRotation(ship.Grid, rotation);
                transforms.SetWorldPosition(foreign, origin + rotation.RotateVec(new Vector2(collisionEdge + 4, 0)));
                transforms.SetWorldRotation(foreign, rotation);
                for (var x = 0; x < 3; x++)
                for (var y = 0; y < 3; y++)
                    _em.SpawnEntity("WallPlastitanium", new EntityCoordinates(foreign, new Vector2(x + .5f, y + .5f)));
                physics.SetAngularVelocity(foreign, 0);
                physics.SetLinearVelocity(foreign, impactDirection * 40);
                highStart = _timing.CurTick.Value;
                WorldProbe?.Invoke(ship.Grid, foreign, "collision.high", true);
                phase = 2;
                return null;
            }
            var high = observer.Collisions.GetValueOrDefault(ship.Grid);
            var foreignContact = observer.Collisions.GetValueOrDefault(foreign);
            if (foreignContact.Other == ship.Grid && foreignContact.Tick > high.Tick)
                high = (foreignContact.Tick, foreign, foreignContact.Speed);
            if (high.Tick <= highStart || high.Other != foreign) return null;
            if (high.Speed < 1) throw new InvalidOperationException("Native high-speed collision did not cross the monitor threshold.");
            var damaged = original.Where(pair => !_em.EntityExists(pair.Key)
                || (float)_em.GetComponent<DamageableComponent>(pair.Key).TotalDamage > pair.Value + .001f).Select(pair => pair.Key.ToString()).ToArray();
            var lostTiles = originalTiles.Where(pair => maps.GetTileRef(ship.Grid, ownMap, pair.Key).Tile.IsEmpty)
                .Select(pair => pair.Key.ToString()).ToArray();
            if (damaged.Length == 0 && lostTiles.Length == 0) return null;
            var highObservation = WorldProbe?.Invoke(ship.Grid, foreign, "collision.high", false);
            if (WorldProbe != null && highObservation == null) return null;
            ExpectedOfflineGrids[ship.Grid] = _timing.CurTick.Value + 2400;
            return new { actualNativeGridContacts = true, lowContact, lowSpeed, highContact = high.Tick,
                highSpeed = high.Speed, nativeDamagedOrDestroyedStructures = damaged, nativeDestroyedHullTiles = lostTiles,
                collisionObservedOnEitherPhysicalBody = true, nativeRamWalls = 9,
                sameGridSacrificialHullFixture = true, fixtureFloorTiles = extensionTiles.Count,
                fixtureRotationDegrees = rotation.Degrees, requestedFixtureRotationDegrees = input.FixtureRotationDegrees,
                impactDirection = impactDirection.ToString(),
                worldTravelAlignedWithLocalHull = true, ramTransverseOffset = 0,
                nativeContactGeometry = observer.CollisionGeometry.GetValueOrDefault(foreign),
                foreignStartsOutsideFixture = true, fixtureRightEdge = collisionEdge,
                ownShieldsDisabledForHullDamageControl = shields.Count, lowObservation, highObservation };
        }, () => new { phase, lowContact, lowSpeed, highStart,
            nativeContactGeometry = observer.CollisionGeometry.GetValueOrDefault(foreign),
            fixtureRotationDegrees = rotation.Degrees,
            ownRotationDegrees = transforms.GetWorldRotation(ship.Grid).Degrees,
            impactDirection = impactDirection.ToString(),
            lastContactTick = observer.Collisions.GetValueOrDefault(ship.Grid).Tick,
            lastContactSpeed = observer.Collisions.GetValueOrDefault(ship.Grid).Speed,
            foreignContactTick = observer.Collisions.GetValueOrDefault(foreign).Tick,
            foreignContactSpeed = observer.Collisions.GetValueOrDefault(foreign).Speed,
            originalStructureCount = original.Count,
            ownPosition = transforms.GetWorldPosition(ship.Grid).ToString(),
            foreignPosition = _em.TryGetComponent<TransformComponent>(foreign, out var foreignTransform)
                ? foreignTransform.LocalPosition.ToString() : null,
            foreignVelocity = _em.TryGetComponent<PhysicsComponent>(foreign, out var foreignBody)
                ? foreignBody.LinearVelocity.ToString() : null,
            foreignMass = foreignBody?.FixturesMass,
            foreignExists = _em.EntityExists(foreign) });
    }

    private sealed record FireFixture(Vector2i Cell, EntityUid Operator, EntityUid First, EntityUid Second,
        EntityUid Extinguisher, Dictionary<Vector2i, Content.Shared.Atmos.GasMixture> Air)
    {
        public bool Completed;
    }
    private static readonly Dictionary<EntityUid, FireFixture> FireFixtures = new();

#pragma warning disable RA0002
    private static object ExerciseFire(Ship ship, Input input)
    {
        var atmos = _em.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
        var transforms = _em.System<SharedTransformSystem>();
        var state = _em.GetComponent<Content.Server.Atmos.Components.GridAtmosphereComponent>(ship.Grid);
        var map = _em.GetComponent<MapGridComponent>(ship.Grid);
        if (input.Type == "fire.clear")
        {
            var saved = FireFixtures[ship.Grid];
            return AwaitNative(input, 1200, () =>
            {
                if (!saved.Completed) return null;
                foreach (var (cell, mixture) in saved.Air)
                {
                    (atmos.GetTileMixture((ship.Grid, null, null), null, cell, false)
                        ?? throw new InvalidOperationException("Native fire room air vanished before cleanup.")).CopyFrom(mixture);
                    atmos.HotspotExtinguish(ship.Grid, cell);
                }
                foreach (var entity in new[] { saved.First, saved.Second, saved.Extinguisher, saved.Operator })
                    if (entity.Valid && _em.EntityExists(entity)) _em.DeleteEntity(entity);
                var effects = new List<EntityUid>();
                var query = _em.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var metadata, out var transform))
                    if (transform.GridUid == ship.Grid && metadata.EntityPrototype?.ID is "Foam" or "ExtinguisherSpray") effects.Add(uid);
                foreach (var effect in effects) _em.DeleteEntity(effect);
                var alarm = _em.System<NativeGameObserverSystem>().FireAlarms[ship.Grid].Alarm;
                var alarms = _em.System<Content.Server.Atmos.Monitor.Systems.AtmosAlarmableSystem>();
                alarms.ResetAllOnNetwork(alarm);
                if (alarms.TryGetHighestAlert(alarm, out var remaining)
                    && remaining == Content.Shared.Atmos.Monitor.AtmosAlarmType.Danger)
                    throw new InvalidOperationException("Native fire alarm did not reset after observed suppression.");
                KiasDriver?.Invoke(ship.Grid, "fire.cleanup", true);
                FireFixtures.Remove(ship.Grid);
                return new { nativeFireFixtureCleared = true, restoredRoomCells = saved.Air.Count,
                    cleanupWaitedForObservedSuppression = true, nativeFireAlarmReset = true };
            });
        }
        var mount = new Vector2(7.5f, 7.5f);
        var blocked = atmos.IsTileAirBlocked(ship.Grid, new Vector2i(7, 7), mapGridComp: map);
        var position = mount + (blocked ? Angle.FromDegrees(-90).ToWorldVec() : Vector2.Zero);
        var cellIndex = new Vector2i((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y));
        var room = NativeVentRoom(state, cellIndex, atmos)
            ?? throw new InvalidOperationException("Controlled native fire fixture requires an intact, pressurized, sealed room at the suppression outlet.");
        var before = room.ToDictionary(tile => tile.GridIndices, tile => tile.Air!.Clone());
        var coordinate = new EntityCoordinates(ship.Grid, position);
        var first = _em.SpawnEntity("MobHuman", coordinate);
        var second = _em.SpawnEntity("MobHuman", new EntityCoordinates(ship.Grid, position + new Vector2(.1f, 0)));
        var extinguisher = EntityUid.Invalid;
        var fireOperator = EntityUid.Invalid;
        object? prepared = KiasDriver?.Invoke(ship.Grid, "fire.prepare", true);
        if (KiasDriver == null)
        {
            fireOperator = _em.SpawnEntity("MobHuman", coordinate);
            AttachTrackedMind(fireOperator, input.Id + "/fire-operator");
            extinguisher = _em.SpawnEntity("FireExtinguisher", coordinate);
            if (!_em.System<SharedHandsSystem>().TryPickupAnyHand(fireOperator, extinguisher))
                throw new InvalidOperationException("Native fire control cannot hold its extinguisher.");
            _em.EventBus.RaiseLocalEvent(extinguisher, new Content.Shared.Interaction.ActivateInWorldEvent(fireOperator, extinguisher, true));
            var actorCell = room.FirstOrDefault(tile => Vector2.DistanceSquared(new Vector2(tile.GridIndices.X + .5f, tile.GridIndices.Y + .5f), position) <= 4
                && tile.GridIndices != cellIndex) ?? throw new InvalidOperationException("Native fire fixture lacks a nearby safe operating tile.");
            transforms.SetCoordinates(fireOperator, new EntityCoordinates(ship.Grid, new Vector2(actorCell.GridIndices.X + .5f, actorCell.GridIndices.Y + .5f)));
        }
        var fixture = new FireFixture(cellIndex, fireOperator, first, second, extinguisher, before);
        FireFixtures.Add(ship.Grid, fixture);
        var flammable = _em.System<Content.Server.Atmos.EntitySystems.FlammableSystem>();
        flammable.AdjustFireStacks(first, 2);
        flammable.Ignite(first, first);
        if (!_em.GetComponent<Content.Shared.Atmos.Components.FlammableComponent>(first).OnFire
            || _em.GetComponent<Content.Shared.Atmos.Components.FlammableComponent>(second).OnFire)
            throw new InvalidOperationException("Native fire propagation control did not start with exactly one ignited entity.");
        var gas = atmos.GetTileMixture((ship.Grid, null, null), null, cellIndex, false)!;
        gas.SetMoles(Content.Shared.Atmos.Gas.Plasma, 5);
        gas.Temperature = 2000;
        atmos.HotspotExpose((ship.Grid, null), cellIndex, 2000, 100);
        var hotspotObserved = atmos.IsHotspotActive(ship.Grid, cellIndex);
        var propagationObserved = false;
        var actuated = false;
        object? suppression = null;
        var started = _timing.CurTick.Value;
        float usedWater = 0;
        return AwaitNative(input, 1200, () =>
        {
            if (!_em.EntityExists(first) || !_em.EntityExists(second))
                throw new InvalidOperationException("Native fire propagation fixture was destroyed before suppression was verified.");
            var firstBurning = _em.GetComponent<Content.Shared.Atmos.Components.FlammableComponent>(first).OnFire;
            var secondBurning = _em.GetComponent<Content.Shared.Atmos.Components.FlammableComponent>(second).OnFire;
            propagationObserved |= firstBurning && secondBurning;
            hotspotObserved |= atmos.IsHotspotActive(ship.Grid, cellIndex);
            var observer = _em.System<NativeGameObserverSystem>();
            if (!hotspotObserved || !propagationObserved || !observer.FireAlarms.TryGetValue(ship.Grid, out var alarm) || alarm.Tick < started) return null;
            if (KiasDriver != null)
            {
                if (!actuated)
                {
                    if (KiasDriver(ship.Grid, "fire.suppress", true) == null) return null;
                    actuated = true;
                }
                suppression ??= KiasDriver(ship.Grid, "fire.verify", false);
                if (suppression == null) return null;
            }
            else
            {
                var solutions = _em.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
                if (!solutions.TryGetSolution(extinguisher, "spray", out _, out var water))
                    throw new InvalidOperationException("Native extinguisher has no water solution.");
                if (firstBurning || secondBurning || atmos.IsHotspotActive(ship.Grid, cellIndex))
                    _em.System<Content.Server.Fluids.EntitySystems.SpraySystem>().Spray(
                        (extinguisher, _em.GetComponent<Content.Server.Fluids.Components.SprayComponent>(extinguisher)),
                        fireOperator, transforms.ToMapCoordinates(coordinate));
                usedWater = 100 - (float)water.Volume;
                if (usedWater <= 0) return null;
            }
            if (firstBurning || secondBurning || atmos.IsHotspotActive(ship.Grid, cellIndex)) return null;
            fixture.Completed = true;
            return new { actualNativeHotspot = hotspotObserved, nativePropagationToSecondEntity = propagationObserved,
                nativeFireAlarm = alarm.Alarm.ToString(), alarmTick = alarm.Tick,
                nativeHotspotAndEntitiesExtinguished = true, prepared, suppression, manualExtinguisherWaterUsed = usedWater,
                intendedBaselineDivergence = KiasDriver == null };
        }, () => new { hotspotObserved, propagationObserved, actuated, suppression, usedWater,
            fireAlarmObserved = _em.System<NativeGameObserverSystem>().FireAlarms.TryGetValue(ship.Grid, out var alert)
                && alert.Tick >= started,
            hotspotActive = atmos.IsHotspotActive(ship.Grid, cellIndex), pressure = gas.Pressure, gas.Temperature });
    }
#pragma warning restore RA0002
    private static readonly Dictionary<EntityUid, (EntityUid Jammer, EntityUid Sender, EntityUid Receiver, EntityUid Actor, EntityUid Grid)> RadioFixtures = new();

    private static object ProbeRadio(Ship ship, Input input)
    {
        var restoring = input.Type == "radio.restore";
        if (!restoring)
        {
            var foreign = CreateForeign(ship, new Vector2(300, 0));
            var position = new EntityCoordinates(foreign, new Vector2(.5f, .5f));
            var actor = _em.SpawnEntity("MobHuman", new EntityCoordinates(foreign, new Vector2(.5f, 1.5f)));
            var jammer = _em.SpawnEntity("RadioJammer", position);
            var sender = _em.SpawnEntity("RadioHandheld", position);
            var receiver = _em.SpawnEntity("RadioHandheld", position);
            _em.EnsureComponent<NativeLabRadioObserverComponent>(receiver);
            RadioFixtures.Add(ship.Grid, (jammer, sender, receiver, actor, foreign));
            _em.EventBus.RaiseLocalEvent(sender, new Content.Shared.Interaction.ActivateInWorldEvent(actor, sender, true));
            _em.EventBus.RaiseLocalEvent(receiver, new Content.Shared.Interaction.ActivateInWorldEvent(actor, receiver, true));
            var observer = _em.System<NativeGameObserverSystem>();
            var before = observer.RadioReceived.GetValueOrDefault(receiver);
            _em.System<Content.Server.Radio.EntitySystems.RadioSystem>().SendRadioMessage(
                actor, "native-lab-control-" + input.Id, "Handheld", sender);
            if (observer.RadioReceived.GetValueOrDefault(receiver) != before + 1)
                throw new InvalidOperationException("Native unjammed radio control failed before activation.");
            _em.EventBus.RaiseLocalEvent(jammer, new Content.Shared.Interaction.ActivateInWorldEvent(actor, jammer, true));
            if (!_em.HasComponent<Content.Shared.Radio.Components.ActiveRadioJammerComponent>(jammer))
                throw new InvalidOperationException("Native battery-powered jammer failed to enable.");
        }
        var fixture = RadioFixtures[ship.Grid];
        if (restoring) _em.EventBus.RaiseLocalEvent(fixture.Jammer,
            new Content.Shared.Interaction.ActivateInWorldEvent(fixture.Actor, fixture.Jammer, true));
        return AwaitNative(input, 120, () =>
        {
            var active = _em.HasComponent<Content.Shared.Radio.Components.ActiveRadioJammerComponent>(fixture.Jammer);
            if (active == restoring) return null;
            var observer = _em.System<NativeGameObserverSystem>();
            var before = observer.RadioReceived.GetValueOrDefault(fixture.Receiver);
            _em.System<Content.Server.Radio.EntitySystems.RadioSystem>().SendRadioMessage(
                fixture.Actor, "native-lab-" + input.Id, "Handheld", fixture.Sender);
            var after = observer.RadioReceived.GetValueOrDefault(fixture.Receiver);
            if (restoring ? after != before + 1 : after != before)
                throw new InvalidOperationException("Native radio reception disagrees with jamming state.");
            if (restoring)
            {
                _em.DeleteEntity(fixture.Grid);
                RadioFixtures.Remove(ship.Grid);
            }
            return new { actualNativeRadioPath = true, activeJammer = active, deliveredMessages = after - before,
                ballisticPdcJamming = "INCOMPATIBLE: native radio jammer does not jam ballistic 20mm projectiles" };
        });
    }

    private static object ProgressAnomaly(Ship ship, Input input)
    {
        if (input.Prototype != "AnomalyFlesh") throw new InvalidOperationException("Native anomaly progression requires explicit AnomalyFlesh.");
        var anomaly = _em.SpawnEntity(input.Prototype, new EntityCoordinates(ship.Grid, new Vector2(-10.5f, .5f)));
        var excluded = new[] { "ElectricityAnomalyComponent", "ElectrifiedComponent", "EmpOnTriggerComponent", "GravityAnomalyComponent", "GravityWellComponent", "RadiationSourceComponent", "RandomWalkComponent" };
        if (_em.GetComponents(anomaly).Any(value => excluded.Contains(value.GetType().Name)))
            throw new InvalidOperationException("Native anomaly has a prohibited electronics hazard.");
        Anomalies.Add(ship.Grid, anomaly);
        var maps = _em.System<SharedMapSystem>();
        var grid = _em.GetComponent<MapGridComponent>(ship.Grid);
        var floors = new Dictionary<Vector2i, Tile>();
        for (var x = -17; x <= -5; x++)
        for (var y = -6; y <= 6; y++)
        {
            var cell = new Vector2i(x, y);
            floors.Add(cell, maps.GetTileRef(ship.Grid, grid, cell).Tile);
        }
        AnomalyFloors.Add(ship.Grid, floors);
        var component = _em.GetComponent<Content.Shared.Anomaly.Components.AnomalyComponent>(anomaly);
        var system = _em.System<Content.Shared.Anomaly.SharedAnomalySystem>();
        system.ChangeAnomalyStability(anomaly, component.GrowthThreshold - .1f - component.Stability);
        WorldProbe?.Invoke(ship.Grid, anomaly, input.Type, true);
        system.ChangeAnomalyStability(anomaly, .2f);
        var before = component.Severity;
        var existing = new HashSet<EntityUid>();
        var query = _em.AllEntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform)) if (transform.GridUid == ship.Grid) existing.Add(uid);
        system.DoAnomalyPulse(anomaly);
        var spawned = new List<EntityUid>();
        query = _em.AllEntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform)) if (transform.GridUid == ship.Grid && !existing.Contains(uid)) spawned.Add(uid);
        AnomalyPulseEntities.Add(ship.Grid, spawned.ToArray());
        return AwaitNative(input, 600, () =>
        {
            if (component.Severity <= before) throw new InvalidOperationException("Native flesh pulse failed to increase severity.");
            var observation = WorldProbe?.Invoke(ship.Grid, anomaly, input.Type, false);
            if (WorldProbe != null && observation == null) return null;
            return new { actualNativePulse = true, severityBefore = before, severityAfter = component.Severity,
                nativeSpawnedEntities = spawned.Count, excludedHazardsAbsent = true, observation };
        });
    }
    private static readonly Dictionary<EntityUid, (Dictionary<EntityUid, EntityCoordinates> Origins, EntityUid Grid)> CrewTrips = new();
    private static readonly Dictionary<EntityUid, EntityUid> CrewTripPlatforms = new();

    private static object MoveCrew(Ship ship, Input input)
    {
        var returning = input.Type == "crew.return";
        var transforms = _em.System<SharedTransformSystem>();
        WorldProbe?.Invoke(ship.Grid, ship.Actor, input.Type, true);
        if (!returning)
        {
            if (!CrewTripPlatforms.TryGetValue(ship.Grid, out var foreign))
            {
                foreign = CreateForeign(ship, new Vector2(300, 300));
                CrewTripPlatforms.Add(ship.Grid, foreign);
            }
            transforms.SetWorldPosition(foreign, transforms.GetWorldPosition(ship.Grid) + new Vector2(300, 300));
            var members = new List<EntityUid> { ship.Actor };
            if (DeathActors.TryGetValue(ship.Grid, out var recovered) && _em.EntityExists(recovered)) members.Add(recovered);
            CrewTrips.Add(ship.Grid, (members.ToDictionary(uid => uid, uid => _em.GetComponent<TransformComponent>(uid).Coordinates), foreign));
            foreach (var member in members) transforms.SetCoordinates(member, new EntityCoordinates(foreign, new Vector2(1.5f, 1.5f)));
        }
        else foreach (var (member, origin) in CrewTrips[ship.Grid].Origins) transforms.SetCoordinates(member, origin);
        return AwaitNative(input, 600, () =>
        {
            var actualGrid = _em.GetComponent<TransformComponent>(ship.Actor).GridUid;
            if (returning ? actualGrid != ship.Grid : actualGrid != CrewTrips[ship.Grid].Grid) return null;
            var observation = WorldProbe?.Invoke(ship.Grid, ship.Actor, input.Type, false);
            if (WorldProbe != null && observation == null) return null;
            if (returning)
            {
                CrewTrips.Remove(ship.Grid);
            }
            return new { nativeGridTransfer = true, returned = returning,
                reusableTransferPlatform = CrewTripPlatforms[ship.Grid].ToString(),
                transferPlatformRetained = _em.EntityExists(CrewTripPlatforms[ship.Grid]), observation };
        });
    }

    private static object IntroduceLife(Ship ship, Input input)
    {
        var fauna = input.Type.StartsWith("fauna.", StringComparison.Ordinal);
        var clear = input.Type.EndsWith("clear", StringComparison.Ordinal);
        var fixtures = fauna ? Fauna : Visitors;
        if (clear)
        {
            var target = fixtures[ship.Grid];
            if (VisitorGuns.Remove(ship.Grid, out var gun) && _em.EntityExists(gun)) _em.DeleteEntity(gun);
            _em.DeleteEntity(target);
            fixtures.Remove(ship.Grid);
            return AwaitNative(input, 600, () =>
            {
                if (_em.EntityExists(target)) return null;
                var observation = WorldProbe?.Invoke(ship.Grid, target, input.Type, false);
                if (WorldProbe != null && observation == null) return null;
                return new { nativeDeleted = true, target = target.ToString(), observation };
            });
        }
        if (fixtures.ContainsKey(ship.Grid)) throw new InvalidOperationException("Native life fixture already exists.");
        var entity = _em.SpawnEntity(fauna ? "MobCarp" : "MobHuman", new EntityCoordinates(ship.Grid, new Vector2(-10.5f, .5f)));
        fixtures.Add(ship.Grid, entity);
        if (!fauna) AttachTrackedMind(entity, input.Id + "/visitor");
        if (input.Type == "crew.armed_threat")
        {
            var gun = _em.SpawnEntity("WeaponPistolMk58", _em.GetComponent<TransformComponent>(entity).Coordinates);
            if (!_em.System<SharedHandsSystem>().TryPickupAnyHand(entity, gun))
                throw new InvalidOperationException("Visitor cannot hold a native threat weapon.");
            VisitorGuns.Add(ship.Grid, gun);
        }
        WorldProbe?.Invoke(ship.Grid, entity, input.Type, true);
        return AwaitNative(input, 600, () =>
        {
            if (!_em.TryGetComponent<MobStateComponent>(entity, out var state) || state.CurrentState != MobState.Alive)
                throw new InvalidOperationException("Life fixture died before its native scanner postcondition.");
            var observation = WorldProbe?.Invoke(ship.Grid, entity, input.Type, false);
            if (WorldProbe != null && observation == null) return null;
            return new { nativeLivingEntity = entity.ToString(), prototype = fauna ? "MobCarp" : "MobHuman",
                nativeHeldGun = VisitorGuns.ContainsKey(ship.Grid), observation, baselineWithoutKias = WorldProbe == null };
        });
    }

    private static object OperateRoom(Ship ship, Input input)
    {
        var maps = _em.System<SharedMapSystem>();
        var grid = _em.GetComponent<MapGridComponent>(ship.Grid);
        RoomProbe?.Invoke(ship.Grid, input.Id, input.Type, true);
        if (input.Type.StartsWith("geometry.", StringComparison.Ordinal))
        {
            var cell = new Vector2i(-11, 1);
            var repair = input.Type == "geometry.repair";
            var tile = maps.GetTileRef(ship.Grid, grid, cell).Tile;
            if (!repair)
            {
                if (tile.IsEmpty || !BreachedFloors.TryAdd(ship.Grid, tile))
                    throw new InvalidOperationException("Breach requires an intact, previously saved floor.");
            }
            else if (!BreachedFloors.ContainsKey(ship.Grid))
                throw new InvalidOperationException("Repair has no saved native floor.");
            maps.SetTile((ship.Grid, grid), cell, repair ? BreachedFloors[ship.Grid] : Tile.Empty);
            if (repair) BreachedFloors.Remove(ship.Grid);
            return AwaitNative(input, 600, () =>
            {
                var actual = maps.GetTileRef(ship.Grid, grid, cell).Tile;
                if (actual.IsEmpty == repair) return null;
                var topology = RoomProbe?.Invoke(ship.Grid, input.Id, input.Type, false);
                if (RoomProbe != null && topology == null) return null;
                return new { nativeTileChanged = true, repaired = repair, cell = cell.ToString(), topology };
            });
        }

        var doors = _em.System<Content.Server.Doors.Systems.DoorSystem>();
        var opening = input.Type == "door.open";
        EntityUid door;
        if (opening)
        {
            var candidates = new List<EntityUid>();
            var query = _em.AllEntityQueryEnumerator<Content.Shared.Doors.Components.DoorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var component, out var transform))
                if (transform.GridUid == ship.Grid && component.State == Content.Shared.Doors.Components.DoorState.Closed
                    && Vector2.DistanceSquared(transform.LocalPosition, new Vector2(-10.5f, .5f)) <= 9)
                    candidates.Add(uid);
            door = candidates.OrderBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.X)
                .ThenBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.Y)
                .FirstOrDefault(uid => doors.CanOpen(uid));
            if (!door.Valid || !doors.TryOpen(door))
                throw new InvalidOperationException("No native closed door accepted opening.");
            OperatedDoors.Add(ship.Grid, door);
        }
        else
        {
            door = OperatedDoors[ship.Grid];
            if (_em.GetComponent<Content.Shared.Doors.Components.DoorComponent>(door).State != Content.Shared.Doors.Components.DoorState.Closed
                && !doors.TryClose(door)) throw new InvalidOperationException("Native door rejected closing.");
            OperatedDoors.Remove(ship.Grid);
        }
        return AwaitNative(input, 300, () =>
        {
            var state = _em.GetComponent<Content.Shared.Doors.Components.DoorComponent>(door).State;
            if (state != (opening ? Content.Shared.Doors.Components.DoorState.Open : Content.Shared.Doors.Components.DoorState.Closed)) return null;
            var topology = RoomProbe?.Invoke(ship.Grid, input.Id, input.Type, false);
            if (RoomProbe != null && topology == null) return null;
            return new { nativeDoor = door.ToString(), state = state.ToString(), topology };
        });
    }
    private static int _nativeCompleted;
    private static bool _crewRegistered;
    private static bool _physicsDiagnosticStarted;
    public static bool PauseRuntime { get; private set; }
    public static bool PauseRoomScans { get; private set; }
    private static string _physicsMode = "B";
    private static readonly Func<long>? PhysicsQueries = typeof(B2DynamicTree<Robust.Shared.Physics.Dynamics.FixtureProxy>)
        .GetProperty("DiagnosticQueryCount")?.GetMethod?.CreateDelegate<Func<long>>();
    private static readonly Func<bool>? PhysicsQueryCounting = typeof(B2DynamicTree<Robust.Shared.Physics.Dynamics.FixtureProxy>)
        .GetProperty("DiagnosticQueriesEnabled")?.GetMethod?.CreateDelegate<Func<bool>>();

    private static object PhysicsSnapshot()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var inventory = new Dictionary<(bool Fleet, bool Paused, string Prototype, Robust.Shared.Physics.BodyType Type),
            (int Bodies, int Awake, int Fixtures, int Collidable, int Proxies, int Contacts)>();
        var fleet = Ships.Values.Select(ship => ship.Grid).ToHashSet();
        var query = _em.AllEntityQueryEnumerator<PhysicsComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var body, out var meta))
        {
            var own = fleet.Contains(uid) || _em.TryGetComponent<TransformComponent>(uid, out var transform)
                && transform.GridUid is { } grid && fleet.Contains(grid);
            var key = (own, meta.EntityPaused, meta.EntityPrototype?.ID ?? "<no prototype>", body.BodyType);
            var row = inventory.GetValueOrDefault(key);
            row.Bodies++; if (body.Awake) row.Awake++;
            if (_em.TryGetComponent<FixturesComponent>(uid, out var fixtures))
            {
                row.Fixtures += fixtures.Fixtures.Count;
                foreach (var fixture in fixtures.Fixtures.Values) row.Proxies += fixture.ProxyCount;
            }
            if (body.CanCollide) row.Collidable++;
            row.Contacts += body.ContactCount;
            inventory[key] = row;
        }
        var rows = inventory.Select(pair => new { category = (pair.Key.Fleet ? "lab_fleet/" : "other/")
                + (pair.Key.Paused ? "paused/" : "active/") + pair.Key.Prototype + "/" + pair.Key.Type,
            pair.Value.Bodies, pair.Value.Awake, pair.Value.Fixtures, pair.Value.Collidable,
            pair.Value.Proxies, pair.Value.Contacts }).OrderBy(row => row.category).ToArray();
        return new { mode = _physicsMode, runtimePaused = PauseRuntime, engineTick = _timing.CurTick.Value, rows,
            fixtureTreeTraversals = PhysicsQueries?.Invoke(),
            queryCountersEnabled = PhysicsQueryCounting?.Invoke(),
            instrumentationMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            instrumentationBytes = GC.GetAllocatedBytesForCurrentThread() - bytes,
            scope = "All native PhysicsComponent bodies; Contacts counts body ends (pairs counted twice); proxies are live fixture proxies. fixtureTreeTraversals counts FixtureProxy B2 tree traversals, including collision broadphase queries." };
    }

    private static void BeginPhysicsDiagnostic()
    {
        _physicsMode = Environment.GetEnvironmentVariable("NATIVE_LAB_PHYSICS_MODE") ?? "B";
        if (_physicsMode is not ("A" or "B" or "C" or "D")) throw new InvalidOperationException("Invalid physics diagnostic variant.");
        AtomicJson("physics-before.json", PhysicsSnapshot());
        PauseRuntime = _physicsMode == "C";
        PauseRoomScans = Environment.GetEnvironmentVariable("NATIVE_LAB_ABLATION") == "RoomScanOff";
        if (_physicsMode != "D") return;
        var group = Environment.GetEnvironmentVariable("NATIVE_LAB_PHYSICS_DEVICE_GROUP") ?? "KiasPrototypes";
        if (group is not ("KiasPrototypes" or "BroadIntegrated")) throw new InvalidOperationException("Invalid diagnostic physics device group.");
        var affected = new List<object>();
        var query = _em.AllEntityQueryEnumerator<PhysicsComponent, FixturesComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var body, out var fixtures, out var meta))
        {
            if (!_em.GetComponents(uid).Any(component => component.GetType().Name == "KiasDeviceComponent")) continue;
            if (group == "KiasPrototypes" && !(meta.EntityPrototype?.ID.StartsWith("Kias", StringComparison.Ordinal) ?? false)) continue;
            affected.Add(new { entity = uid.ToString(), prototype = meta.EntityPrototype?.ID,
                diagnosticGroup = group,
                previouslyCollidable = body.CanCollide, fixtureCount = fixtures.Fixtures.Count,
                semanticRisk = "Diagnostic only: bullets, physical objects and interaction hitboxes may change; not a production fix." });
            _em.System<SharedPhysicsSystem>().SetCanCollide(uid, false);
        }
        File.WriteAllText(Path.Combine(_output, "physics-D-affected.json"), JsonSerializer.Serialize(affected));
        File.WriteAllText(Path.Combine(_output, "physics-after-D.json"), JsonSerializer.Serialize(PhysicsSnapshot()));
    }

    public static void SaveDiagnostic(string name, object value) => AtomicJson(name, value);
    private static string? _pendingProgressJson;
    private static long _progressFirstFailure, _progressLastAttempt;
    private static int _progressWriteFailures;
    private static string _progressWriteError = string.Empty;

    private static void FlushPendingProgress(bool force = false)
    {
        if (ProfileEnabled && _profileTick >= 0) return;
        if (_pendingProgressJson == null) return;
        if (_progressFirstFailure != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_progressFirstFailure).TotalSeconds >= 30)
            throw new IOException("Progress snapshot remained blocked for 30 seconds: " + _progressWriteError);
        if (!force && _progressLastAttempt != 0
            && System.Diagnostics.Stopwatch.GetElapsedTime(_progressLastAttempt).TotalSeconds < .25) return;
        _progressLastAttempt = System.Diagnostics.Stopwatch.GetTimestamp();
        var path = Path.Combine(_output, "progress.json");
        try
        {
            File.WriteAllText(path + ".tmp", _pendingProgressJson);
            File.Move(path + ".tmp", path, true);
            _pendingProgressJson = null;
            _progressFirstFailure = 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _progressFirstFailure = _progressFirstFailure == 0 ? _progressLastAttempt : _progressFirstFailure;
            _progressWriteFailures++;
            _progressWriteError = error.Message;
        }
    }

    private static void AtomicJson(string name, object value)
    {
        if (DeferProfileJson(name, value)) return;
        if (name == "progress.json")
        {
            _pendingProgressJson = JsonSerializer.Serialize(value);
            FlushPendingProgress(force: true);
            return;
        }
        var path = Path.Combine(_output, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
        File.Move(path + ".tmp", path, true);
    }
    private static StreamWriter? _outcomes;
    private static readonly Dictionary<EntityUid, EntityUid> DeathActors = new();
    private static readonly Dictionary<EntityUid, List<EntityUid>> GunFixtures = new();
    private static readonly Dictionary<EntityUid, Vector2> FtlOrigins = new();

    private static object ConsoleFtl(Ship ship, Input input)
    {
        var transforms = _em.System<SharedTransformSystem>();
        var shuttle = _em.System<ShuttleSystem>();
        if (!shuttle.TryGetFTLDrive(ship.Grid, out var drive, out var driveComponent)
            || !_em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().IsPowered(drive.Value))
            throw new InvalidOperationException("Native FTL requires the ship's powered drive.");
        var console = EntityUid.Invalid;
        var consoles = _em.AllEntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var transform))
            if (transform.GridUid == ship.Grid
                && _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().IsPowered(uid))
            { console = uid; break; }
        if (!console.Valid) throw new InvalidOperationException("Native FTL requires a powered shuttle console.");
        if (input.Type == "ftl.console_depart") FtlOrigins.Add(ship.Grid, transforms.GetWorldPosition(ship.Grid));
        if (!FtlOrigins.TryGetValue(ship.Grid, out var origin)) throw new InvalidOperationException("Missing FTL departure origin.");
        var destination = origin + (input.Type == "ftl.console_depart" ? new Vector2(1000, 1000) : Vector2.Zero);
        var map = transforms.GetMapCoordinates(ship.Grid).MapId;
        var distance = Vector2.Distance(transforms.GetWorldPosition(ship.Grid), destination);
        var range = shuttle.GetFTLRange(ship.Grid);
        if (distance > range || !shuttle.CanFTL(ship.Grid, out var reason) || !shuttle.CanFTLTo(ship.Grid, map, console))
            throw new InvalidOperationException("Native FTL constraints rejected the scheduled jump.");
        var started = _timing.CurTick.Value;
        var request = new Content.Shared.Shuttles.Events.ShuttleConsoleFTLPositionMessage {
            Actor = ship.Actor, Entity = _em.GetNetEntity(console),
            UiKey = Content.Shared.Shuttles.Components.ShuttleConsoleUiKey.Key,
            Coordinates = new MapCoordinates(destination + _em.GetComponent<PhysicsComponent>(ship.Grid).LocalCenter, map),
            Angle = Angle.Zero };
        _em.EventBus.RaiseLocalEvent(console, request);
        if (!_em.HasComponent<Content.Shared.Shuttles.Components.FTLComponent>(ship.Grid)) throw new InvalidOperationException("Native shuttle console did not accept FTL request.");
        return AwaitNative(input, 12000, () =>
        {
            if (!_em.System<NativeGameObserverSystem>().FtlCompleted.TryGetValue(ship.Grid, out var completed)
                || completed < started || transforms.GetMapCoordinates(ship.Grid).MapId != map
                || Vector2.Distance(transforms.GetWorldPosition(ship.Grid), destination) > 10) return null;
            if (input.Type == "ftl.console_return") FtlOrigins.Remove(ship.Grid);
            return new { actualFtlCompletedTick = completed, consoleInputTick = started,
                poweredDrive = drive.Value.ToString(), poweredConsole = console.ToString(), range, distance,
                driveStartupSeconds = driveComponent.StartupTime, driveHyperSpaceSeconds = driveComponent.HyperSpaceTime,
                actualPosition = transforms.GetWorldPosition(ship.Grid).ToString(),
                nativeConsolePath = true, nativeDurationUnchanged = true };
        });
    }

    private static object FireNativeGun(Ship ship, Input input)
    {
        if (ForeignGrids.ContainsKey(ship.Grid)) throw new InvalidOperationException("Foreign fixture already in use.");
        var foreign = CreateForeign(ship, new Vector2(-100, 35));
        ForeignGrids.Add(ship.Grid, foreign);
        var shooter = _em.SpawnEntity("MobHuman", new EntityCoordinates(foreign, new Vector2(.5f, .5f)));
        AttachTrackedMind(shooter, input.Ship + "/shooter");
        var target = _em.SpawnEntity("MobHuman", new EntityCoordinates(foreign, new Vector2(2.5f, .5f)));
        var weapon = _em.SpawnEntity("WeaponPistolMk58", _em.GetComponent<TransformComponent>(shooter).Coordinates);
        GunFixtures.Add(ship.Grid, new() { shooter, target, weapon });
        if (!_em.System<SharedHandsSystem>().TryPickup(shooter, weapon)) throw new InvalidOperationException("Shooter cannot hold native gun.");
        _em.System<SharedCombatModeSystem>().SetInCombatMode(shooter, true);
        var rack = new Content.Shared.Interaction.Events.UseInHandEvent(shooter);
        _em.EventBus.RaiseLocalEvent(weapon, rack);
        _em.EnsureComponent<NativeLabGunObserverComponent>(weapon);
        var gun = _em.GetComponent<GunComponent>(weapon);
        var before = gun.LastFire;
        var started = _timing.CurTick.Value;
        var damage = _em.GetComponent<DamageableComponent>(target).TotalDamage;
        var requested = false;
        return AwaitNative(input, 300, () =>
        {
            if (!requested && _timing.CurTick.Value >= started + 60)
            {
                requested = true;
                _em.System<SharedGunSystem>().AttemptShoot(shooter, weapon, gun, _em.GetComponent<TransformComponent>(target).Coordinates, target);
            }
            var observer = _em.System<NativeGameObserverSystem>();
            if (!observer.Shots.TryGetValue(weapon, out var shot) || shot.Tick < started || shot.Projectiles.Length == 0
                || gun.LastFire <= before || _em.GetComponent<DamageableComponent>(target).TotalDamage <= damage
                || !shot.Projectiles.Any(projectile => observer.Hits.TryGetValue(projectile, out var hit)
                    && hit.Tick >= shot.Tick && hit.Target == target)) return null;
            return new { realAmmoShotTick = shot.Tick, projectiles = shot.Projectiles.Select(uid => uid.ToString()).ToArray(),
                hits = shot.Projectiles.Where(observer.Hits.ContainsKey).Select(projectile => new {
                    projectile = projectile.ToString(), tick = observer.Hits[projectile].Tick,
                    target = observer.Hits[projectile].Target.ToString(), damage = observer.Hits[projectile].Damage }).ToArray(),
                targetDamage = _em.GetComponent<DamageableComponent>(target).TotalDamage.ToString(), actualProjectileImpact = true };
        }, () => new { requested, lastFire = gun.LastFire.ToString(), previousFire = before.ToString(),
            shotObserved = _em.System<NativeGameObserverSystem>().Shots.ContainsKey(weapon),
            canAttack = _em.System<Content.Shared.ActionBlocker.ActionBlockerSystem>().CanAttack(shooter),
            shooterState = _em.GetComponent<MobStateComponent>(shooter).CurrentState,
            gun.NextFire, gun.FireRateModified,
            shooterComponents = _em.GetComponents(shooter).Select(component => component.GetType().Name).ToArray(),
            targetDamage = _em.GetComponent<DamageableComponent>(target).TotalDamage.ToString() });
    }

    private sealed record PdcFixture(EntityUid Defender, EntityUid SourceGrid, EntityUid Attacker, EntityUid Server);
    private static readonly Dictionary<EntityUid, PdcFixture> PdcFixtures = new();
    private static readonly Dictionary<EntityUid, EntityUid> PdcRemovedMagazines = new();
    private static readonly Dictionary<EntityUid, (EntityUid Actor, EntityUid Grid)> PdcMagazineOperators = new();
    public static EntityUid PdcSource(EntityUid grid) => PdcFixtures[grid].SourceGrid;

    private static PdcFixture SpawnPdcSource(Ship ship, EntityUid defender)
    {
        var position = _em.GetComponent<TransformComponent>(defender).LocalPosition;
        var foreign = CreateForeign(ship, position + new Vector2(180, -1));
        _em.SpawnEntity("APCHyperCapacity", new EntityCoordinates(foreign, new Vector2(2.5f, .5f)));
        var server = _em.SpawnEntity("GunneryServerLow", new EntityCoordinates(foreign, new Vector2(1.5f, 1.5f)));
        var attacker = _em.SpawnEntity("WeaponTurretL85Autocannon", new EntityCoordinates(foreign, new Vector2(.5f, 1.5f)));
        _em.System<SharedTransformSystem>().SetLocalRotation(attacker, Angle.FromDegrees(180));
        _em.EnsureComponent<NativeLabGunObserverComponent>(attacker);
        return new(defender, foreign, attacker, server);
    }

    private static int NativeAmmoCount(EntityUid weapon)
    {
        var count = new GetAmmoCountEvent();
        _em.EventBus.RaiseLocalEvent(weapon, ref count);
        return count.Count;
    }

    private static object ChangePdcMagazine(Ship ship, Input input)
    {
        var weapon = PdcFixtures[ship.Grid].Defender;
        var slots = _em.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>();
        var transforms = _em.System<SharedTransformSystem>();
        if (input.Type == "pdc.unload")
        {
            if (PdcRemovedMagazines.ContainsKey(ship.Grid)
                || !slots.TryEject(weapon, "gun_magazine", null, out var magazine))
                throw new InvalidOperationException("Native PDC magazine ejection failed.");
            PdcRemovedMagazines.Add(ship.Grid, magazine.Value);
            var storage = CreateForeign(ship, new Vector2(-300, 300));
            var actor = _em.SpawnEntity("MobHuman", new EntityCoordinates(storage, new Vector2(.5f, .5f)));
            AttachTrackedMind(actor, input.Ship + "/pdc-operator");
            PdcMagazineOperators.Add(ship.Grid, (actor, storage));
            var origin = _em.GetComponent<TransformComponent>(actor).Coordinates;
            transforms.SetCoordinates(actor, _em.GetComponent<TransformComponent>(weapon).Coordinates);
            try
            {
                if (!_em.System<SharedHandsSystem>().TryPickupAnyHand(actor, magazine.Value))
                    throw new InvalidOperationException("Native auxiliary PDC operator could not secure the ejected cassette: "
                        + JsonSerializer.Serialize(new { actor = actor.ToString(),
                            state = _em.GetComponent<MobStateComponent>(actor).CurrentState,
                            crewState = _em.GetComponent<MobStateComponent>(ship.Actor).CurrentState }));
            }
            finally { transforms.SetCoordinates(actor, origin); }
            if (NativeAmmoCount(weapon) != 0) throw new InvalidOperationException("Native PDC weapon retained ammunition after magazine removal.");
            return new { nativeMagazineEjected = true, weaponAmmo = 0,
                defaultMagazineIsInfinite = true, nativeCassetteHeldOutsideFire = true,
                independentAuxiliaryOperator = actor.ToString(), storageGrid = storage.ToString(),
                crewState = _em.GetComponent<MobStateComponent>(ship.Actor).CurrentState,
                control = "Physical cassette removal; no claim of finite magazine exhaustion." };
        }
        var reload = PdcRemovedMagazines[ship.Grid];
        var auxiliary = PdcMagazineOperators[ship.Grid];
        if (!_em.EntityExists(reload)) throw new InvalidOperationException("The original native PDC cassette was destroyed before reload.");
        var operatorOrigin = _em.GetComponent<TransformComponent>(auxiliary.Actor).Coordinates;
        transforms.SetCoordinates(auxiliary.Actor, _em.GetComponent<TransformComponent>(weapon).Coordinates);
        var hands = _em.System<SharedHandsSystem>();
        var released = hands.IsHolding(auxiliary.Actor, reload) && hands.TryDrop(auxiliary.Actor, reload);
        var inserted = released && slots.TryInsert(weapon, "gun_magazine", reload, auxiliary.Actor);
        var magazineSlot = _em.GetComponent<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(weapon).Slots["gun_magazine"];
        inserted &= magazineSlot.Item == reload;
        transforms.SetCoordinates(auxiliary.Actor, operatorOrigin);
        if (!inserted) throw new InvalidOperationException("Native PDC cassette insertion was rejected: "
            + JsonSerializer.Serialize(new { cassette = reload.ToString(),
                slotLocked = _em.GetComponent<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(weapon).Slots["gun_magazine"].Locked,
                existingMagazine = _em.GetComponent<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(weapon).Slots["gun_magazine"].Item?.ToString() }));
        return AwaitNative(input, 120, () =>
        {
            var infinite = _em.TryGetComponent<BallisticAmmoProviderComponent>(reload, out var provider)
                && provider.InfiniteUnspawned;
            if (magazineSlot.Item != reload || !infinite && NativeAmmoCount(weapon) <= 0) return null;
            PdcRemovedMagazines.Remove(ship.Grid);
            PdcMagazineOperators.Remove(ship.Grid);
            _em.DeleteEntity(auxiliary.Actor);
            _em.DeleteEntity(auxiliary.Grid);
            return new { nativeMagazineReloaded = true, sameMagazineEntity = reload.ToString(),
                auxiliaryOperatorCleaned = !_em.EntityExists(auxiliary.Actor),
                weaponAmmo = NativeAmmoCount(weapon), nativeInfiniteProvider = infinite,
                ammunitionSemantics = "Native infinite providers report zero stored rounds; live firing after reload requires a separate hostile control." };
        }, () => new { cassetteExists = _em.EntityExists(reload), weaponAmmo = NativeAmmoCount(weapon) });
    }

    private static void PreparePdcFixture(Ship ship)
    {
        var query = _em.AllEntityQueryEnumerator<Content.Server._Mono.SpaceArtillery.Components.SpaceArtilleryComponent, TransformComponent>();
        EntityUid? original = null;
        var north = float.NegativeInfinity;
        while (query.MoveNext(out var uid, out _, out var transform))
            if (transform.GridUid == ship.Grid && transform.LocalPosition.Y > north)
            { original = uid; north = transform.LocalPosition.Y; }
        if (original is not { } old) throw new InvalidOperationException("Briar has no native artillery fixture.");
        var position = _em.GetComponent<TransformComponent>(old).Coordinates;
        var oldPrototype = _em.GetComponent<MetaDataComponent>(old).EntityPrototype?.ID;
        _em.DeleteEntity(old);
        var defender = _em.SpawnEntity("WeaponTurretL85Autocannon", position);
        _em.EnsureComponent<NativeLabGunObserverComponent>(defender);
        var shields = _em.AllEntityQueryEnumerator<Content.Shared._Crescent.ShipShields.ShipShieldEmitterComponent, TransformComponent>();
        while (shields.MoveNext(out var shield, out _, out var transform))
            if (transform.GridUid == ship.Grid) _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().SetPowerDisabled(shield, true);
        PdcFixtures.Add(ship.Grid, SpawnPdcSource(ship, defender));
        Bindings.Add(new { fixture = "native-pdc", originalPrototype = oldPrototype, replacementPrototype = "WeaponTurretL85Autocannon",
            localPosition = position.Position.ToString(), sourceOffset = "180m east", shieldPowerDisabled = true,
            authorMapUnchanged = true, symmetricNativeFixture = true });
    }

    private static object FirePdcThreat(Ship ship, Input input)
    {
        var observer = _em.System<NativeGameObserverSystem>();
        var fixture = PdcFixtures[ship.Grid];
        if (observer.Shots.ContainsKey(fixture.Attacker) || !_em.EntityExists(fixture.Attacker))
        {
            if (_em.EntityExists(fixture.SourceGrid)) _em.DeleteEntity(fixture.SourceGrid);
            fixture = SpawnPdcSource(ship, fixture.Defender);
            PdcFixtures[ship.Grid] = fixture;
        }
        var started = _timing.CurTick.Value;
        var defenderGun = _em.GetComponent<GunComponent>(fixture.Defender);
        var attackerGun = _em.GetComponent<GunComponent>(fixture.Attacker);
        var previousFire = defenderGun.LastFire;
        var friendly = input.Type == "pdc.friendly";
        var empty = input.Type == "pdc.empty";
        var blocked = input.Type == "pdc.blocked";
        if (empty && NativeAmmoCount(fixture.Defender) != 0) throw new InvalidOperationException("PDC empty-ammunition control is not empty.");
        var blockers = new List<EntityUid>();
        var blockerTiles = new Dictionary<Vector2i, Tile>();
        var backstop = friendly || empty || KiasDriver == null;
        if (blocked || backstop)
        {
            var maps = _em.System<SharedMapSystem>();
            var grid = _em.GetComponent<MapGridComponent>(ship.Grid);
            var position = _em.GetComponent<TransformComponent>(fixture.Defender).LocalPosition + new Vector2(blocked ? 2 : -2, 0);
            for (var offset = -2; offset <= 2; offset++)
            {
                var wallPosition = position + new Vector2(0, offset);
                var cell = new Vector2i((int)MathF.Floor(wallPosition.X), (int)MathF.Floor(wallPosition.Y));
                blockerTiles.Add(cell, maps.GetTileRef(ship.Grid, grid, cell).Tile);
                maps.SetTile((ship.Grid, grid), cell, maps.GetTileRef(ship.Grid, grid, ship.FireTile).Tile);
                var wall = _em.SpawnEntity("WallPlastitanium", new EntityCoordinates(ship.Grid, wallPosition));
                if (!_em.GetComponent<TransformComponent>(wall).Anchored) throw new InvalidOperationException("Native occlusion wall did not anchor.");
                blockers.Add(wall);
            }
        }
        _em.EnsureComponent<Content.Shared.Shuttles.Components.IFFComponent>(fixture.SourceGrid);
        _em.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(ship.Grid).CompanyName = "None";
        _em.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(fixture.SourceGrid).CompanyName = "None";
        _em.EnsureComponent<Content.Shared.Shuttles.Components.ShuttleFactionComponent>(ship.Grid).Faction = "NanoTrasen";
        _em.EnsureComponent<Content.Shared.Shuttles.Components.ShuttleFactionComponent>(fixture.SourceGrid).Faction = friendly ? "NanoTrasen" : "Syndicate";
        var automatic = KiasDriver?.Invoke(ship.Grid, input.Type, true);
        WorldProbe?.Invoke(ship.Grid, fixture.Attacker, "pdc.weapon_flash", true);
        var fireControl = _em.System<Content.Server._Mono.FireControl.FireControlSystem>();
        var requested = false;
        var incoming = new HashSet<EntityUid>();
        var center = _em.System<SharedTransformSystem>().GetWorldPosition(ship.Grid);
        var lastDistance = float.PositiveInfinity;
        return AwaitNative(input, 600, () =>
        {
            if (!requested)
            {
                fireControl.ForceServerReconnectionOnGrid(fixture.SourceGrid);
                if (_em.GetComponent<Content.Server._Mono.FireControl.FireControllableComponent>(fixture.Attacker).ControllingServer != fixture.Server
                    || !_em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().IsPowered(fixture.Attacker)) return null;
                if (!fireControl.AttemptFire(fixture.Attacker, fixture.Attacker,
                        _em.GetComponent<TransformComponent>(fixture.Defender).Coordinates))
                    throw new InvalidOperationException("Native powered attacker GCS rejected its real firing request.");
                requested = true;
            }
            if (observer.Shots.TryGetValue(fixture.Attacker, out var shot) && shot.Tick >= started)
                incoming.UnionWith(shot.Projectiles);
            foreach (var projectile in incoming)
                if (_em.EntityExists(projectile)) lastDistance = MathF.Min(lastDistance,
                    Vector2.Distance(_em.System<SharedTransformSystem>().GetWorldPosition(projectile), center));
            if (incoming.Count == 0 || _timing.CurTick.Value < started + 18 || incoming.Any(_em.EntityExists)) return null;
            var impacts = incoming.Where(observer.Hits.ContainsKey).Select(uid => observer.Hits[uid]).ToArray();
            var interceptions = incoming.Where(observer.InterceptedProjectiles.ContainsKey)
                .Select(uid => observer.InterceptedProjectiles[uid]).ToArray();
            var expired = incoming.Where(uid => observer.ExpiredProjectiles.ContainsKey(uid) && !observer.Hits.ContainsKey(uid)).ToArray();
            if (incoming.Any(uid => !observer.Hits.ContainsKey(uid) && !observer.InterceptedProjectiles.ContainsKey(uid)
                    && !observer.ExpiredProjectiles.ContainsKey(uid)))
                throw new InvalidOperationException("Native projectile disappeared without impact, interception or expiry evidence.");
            var defenderShot = observer.Shots.TryGetValue(fixture.Defender, out var interception) && interception.Tick >= started;
            var flashObservation = WorldProbe?.Invoke(ship.Grid, fixture.Attacker, "pdc.weapon_flash", false);
            if (WorldProbe != null && flashObservation == null) return null;
            if (KiasDriver != null && !friendly && !empty && !blocked)
            {
                if (defenderGun.LastFire <= previousFire || !defenderShot || interceptions.Length == 0
                    || interceptions.Any(hit => hit.Grid != ship.Grid
                        || hit.Distance <= _em.GetComponent<MapGridComponent>(ship.Grid).LocalAABB.Size.Length() / 2))
                    throw new InvalidOperationException("Native PDC failed to intercept the real hostile gun projectile before the hull: "
                        + JsonSerializer.Serialize(new { defenderShot, previousFire, defenderGun.LastFire, automatic,
                            impacts = impacts.Select(hit => new { hit.Tick, target = hit.Target.ToString(), hit.Damage }).ToArray(),
                            interceptions = interceptions.Select(hit => new { hit.Tick, hit.Distance }).ToArray(),
                            expired = expired.Select(uid => uid.ToString()).ToArray() }));
            }
            else if (impacts.Length == 0 || interceptions.Length != 0 || defenderShot)
                throw new InvalidOperationException("Native non-interception control must produce impacts without PDC fire: "
                    + JsonSerializer.Serialize(new { input.Type, incoming = incoming.Select(uid => uid.ToString()).ToArray(),
                        impacts = impacts.Select(hit => new { hit.Tick, target = hit.Target.ToString(), hit.Damage }).ToArray(),
                        defenderShot, previousFire, defenderGun.LastFire, lastDistance, automatic,
                        blockers = blockers.Select(uid => new { uid = uid.ToString(), exists = _em.EntityExists(uid),
                            damage = _em.TryGetComponent<DamageableComponent>(uid, out var damage) ? damage.TotalDamage.ToString() : null }).ToArray() }));
            if (blocked)
            {
                if (impacts.Any(hit => !blockers.Contains(hit.Target))) throw new InvalidOperationException("Native occlusion control did not hit its wall barrier.");
            }
            foreach (var wall in blockers) if (_em.EntityExists(wall)) _em.DeleteEntity(wall);
            foreach (var (cell, tile) in blockerTiles)
                _em.System<SharedMapSystem>().SetTile((ship.Grid, _em.GetComponent<MapGridComponent>(ship.Grid)), cell, tile);
            return new { realIncomingGunShot = true, automatic, exteriorWeaponFlash = flashObservation,
                incoming = incoming.Select(uid => uid.ToString()).ToArray(),
                actualInterception = KiasDriver != null && !friendly && !empty && !blocked, friendlySource = friendly,
                interceptionRate = (double)interceptions.Length / incoming.Count,
                leakedImpacts = impacts.Length, perfectInterceptionRequired = false,
                emptyAmmoControl = empty, physicalWallOcclusionControl = blocked, nativeBackstopBehindWeapon = backstop,
                nativeGcsPowerAndConnectionVerified = true, lastDistanceFromShip = lastDistance,
                impacts = impacts.Select(hit => new { hit.Tick, target = hit.Target.ToString(), hit.Damage }).ToArray(),
                interceptions = interceptions.Select(hit => new { hit.Tick, grid = hit.Grid.ToString(), interceptor = hit.Interceptor.ToString(), hit.Distance }).ToArray(),
                nativeExpiredMisses = expired.Select(uid => uid.ToString()).ToArray(),
                defenderFired = defenderShot, intendedBaselineDivergence = KiasDriver == null };
        }, () => new { attackerGun.LastFire, attackerGun.NextFire, incoming = incoming?.Select(uid => uid.ToString()).ToArray(),
            defenderFired = defenderGun.LastFire > previousFire, lastDistance,
            attackerAnchored = _em.GetComponent<TransformComponent>(fixture.Attacker).Anchored,
            attackerBattery = _em.TryGetComponent<Content.Shared.Power.Components.BatteryComponent>(fixture.Attacker, out var battery) ? battery.CurrentCharge : (float?) null });
    }
    private static object ExerciseRadiation(Ship ship, Input input)
    {
        var clear = input.Type == "radiation.clear";
        EntityUid source;
        if (clear)
        {
            source = Radiation[ship.Grid];
            _em.DeleteEntity(source);
            Radiation.Remove(ship.Grid);
        }
        else
        {
            source = _em.SpawnEntity(null, _em.GetComponent<TransformComponent>(ship.Actor).Coordinates);
            _em.AddComponent<Content.Shared.Radiation.Components.RadiationSourceComponent>(source).Intensity = 10;
            Radiation.Add(ship.Grid, source);
            WorldProbe?.Invoke(ship.Grid, source, input.Type, true);
        }
        return AwaitNative(input, 600, () =>
        {
            var dose = _em.GetComponent<Content.Server.Radiation.Components.RadiationReceiverComponent>(ship.Actor).CurrentRadiation;
            if (clear ? dose > .01f : dose < 1) return null;
            var detection = clear ? null : WorldProbe?.Invoke(ship.Grid, source, input.Type, false);
            if (!clear && WorldProbe != null && detection == null) return null;
            return new { nativeRadiation = true, cleared = clear, crewRadsPerSecond = dose,
                sourceExists = _em.EntityExists(source), scannerObservation = detection };
        }, () => new { sourceExists = _em.EntityExists(source), crewRadsPerSecond =
            _em.GetComponent<Content.Server.Radiation.Components.RadiationReceiverComponent>(ship.Actor).CurrentRadiation });
    }

    private static readonly Dictionary<EntityUid, (EntityUid Target, float Before)> StructureChecks = new();

    private static object ExerciseStructure(Ship ship, Input input)
    {
        var repair = input.Type == "hull.repair";
        if (!repair)
        {
            var candidates = new List<EntityUid>();
            var query = _em.AllEntityQueryEnumerator<DamageableComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var damage, out var transform))
                if (transform.GridUid == ship.Grid && damage.Damage.DamageDict.ContainsKey("Structural")
                    && _em.HasComponent<Content.Server.Destructible.DestructibleComponent>(uid)
                    && _em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID.StartsWith("Wall", StringComparison.Ordinal) == true)
                    candidates.Add(uid);
            var target = candidates.OrderBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.X)
                .ThenBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.Y).First();
            StructureChecks.Add(ship.Grid, (target, (float)_em.GetComponent<DamageableComponent>(target).Damage.DamageDict["Structural"]));
            WorldProbe?.Invoke(ship.Grid, target, input.Type, true);
        }
        var state = StructureChecks[ship.Grid];
        var structural = new DamageSpecifier();
        structural.DamageDict.Add("Structural", repair ? state.Before - _em.GetComponent<DamageableComponent>(state.Target).Damage.DamageDict["Structural"] : 100);
        _em.System<DamageableSystem>().TryChangeDamage(state.Target, structural);
        return AwaitNative(input, 600, () =>
        {
            var current = (float)_em.GetComponent<DamageableComponent>(state.Target).Damage.DamageDict["Structural"];
            if (repair ? MathF.Abs(current - state.Before) > .01f : current <= state.Before) return null;
            var detection = repair ? null : WorldProbe?.Invoke(ship.Grid, state.Target, input.Type, false);
            if (!repair && WorldProbe != null && detection == null) return null;
            if (repair) StructureChecks.Remove(ship.Grid);
            return new { nativeDamageSystem = true, repaired = repair, target = state.Target.ToString(),
                structuralDamageBefore = state.Before, structuralDamageAfter = current, integrityObservation = detection };
        }, () => new { target = state.Target.ToString(), prototype = _em.GetComponent<MetaDataComponent>(state.Target).EntityPrototype?.ID,
            damageBefore = state.Before, actualDamage = _em.GetComponent<DamageableComponent>(state.Target).Damage.DamageDict["Structural"].ToString() });
    }

    private static object ExerciseLight(Ship ship, Input input)
    {
        var enabled = input.Type.EndsWith("_on", StringComparison.Ordinal);
        var command = input.Type.StartsWith("light.command", StringComparison.Ordinal);
        if (command && KiasDriver != null)
            KiasDriver(ship.Grid, input.Type, true);
        else if (command)
            _em.System<Content.Shared.Light.EntitySystems.SharedPoweredLightSystem>().SetState(ship.Light, enabled);
        else
            _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().SetPowerDisabled(ship.Light, !enabled);
        return AwaitNative(input, 600, () =>
        {
            var light = _em.GetComponent<Content.Shared.Light.Components.PoweredLightComponent>(ship.Light);
            var power = _em.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(ship.Light);
            var illumination = _em.GetComponent<Robust.Server.GameObjects.PointLightComponent>(ship.Light);
            if (light.CurrentLit != enabled || illumination.Enabled != enabled || (command && light.On != enabled)) return null;
            return new { actualIllumination = illumination.Enabled, light.CurrentLit, light.On, power.Powered, power.PowerDisabled,
                kiasCommand = command && KiasDriver != null, nativeBaselineSwitch = command && KiasDriver == null };
        }, () => new { _em.GetComponent<Content.Shared.Light.Components.PoweredLightComponent>(ship.Light).CurrentLit,
            _em.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(ship.Light).Powered });
    }

    private static readonly Dictionary<EntityUid, (EntityUid Own, EntityUid Other, EntityUid Grid)> DockChecks = new();

    private static void PrepareDockFixture(Ship ship)
    {
        var transforms = _em.System<SharedTransformSystem>();

            var ownPorts = new List<EntityUid>();
            var query = _em.AllEntityQueryEnumerator<DockingComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var dock, out var transform))
                if (transform.GridUid == ship.Grid && !dock.Docked) ownPorts.Add(uid);
            var own = ownPorts.OrderBy(uid => uid.Id).First();
            var rotation = transforms.GetWorldRotation(own);
            var foreign = CreateForeign(ship, new Vector2(500, 500));
            var other = _em.SpawnEntity("AirlockShuttle", new EntityCoordinates(foreign, new Vector2(.5f, .5f)));
            transforms.SetWorldRotation(foreign, rotation + MathF.PI);
            transforms.SetWorldPosition(foreign, transforms.GetWorldPosition(own) + 5 * rotation.ToWorldVec()
                - (rotation + MathF.PI).RotateVec(new Vector2(.5f, .5f)));
            if (!_em.GetComponent<TransformComponent>(other).Anchored && !transforms.AnchorEntity(other))
                throw new InvalidOperationException("Native auxiliary docking port could not anchor.");
            DockChecks.Add(ship.Grid, (own, other, foreign));
        _em.EnsureComponent<JointComponent>(ship.Grid);
        _em.EnsureComponent<JointComponent>(DockChecks[ship.Grid].Grid);
    }

    private static object ExerciseDocking(Ship ship, Input input)
    {
        var docking = _em.System<DockingSystem>();
        var transforms = _em.System<SharedTransformSystem>();
        var undock = input.Type == "dock.undock";
        if (!DockChecks.ContainsKey(ship.Grid)) throw new InvalidOperationException("Dock fixture was not initialized before network clients.");
        var state = DockChecks[ship.Grid];
        var ownDock = _em.GetComponent<DockingComponent>(state.Own);
        var otherDock = _em.GetComponent<DockingComponent>(state.Other);
        WorldProbe?.Invoke(ship.Grid, state.Own, input.Type, true);
        if (undock)
        {
            if (!docking.CanUndock((state.Own, ownDock))) throw new InvalidOperationException("Native dock rejected undocking.");
            docking.Undock((state.Own, ownDock));
        }
        else
        {
            var rotation = transforms.GetWorldRotation(state.Own);
            transforms.SetWorldRotation(state.Grid, rotation + MathF.PI);
            transforms.SetWorldPosition(state.Grid, transforms.GetWorldPosition(state.Own) + rotation.ToWorldVec()
                - (rotation + MathF.PI).RotateVec(new Vector2(.5f, .5f)));
            if (!docking.CanDock((state.Own, ownDock), (state.Other, otherDock))) throw new InvalidOperationException("Native dock geometry rejected coupling.");
            docking.Dock((state.Own, ownDock), (state.Other, otherDock));
        }
        return AwaitNative(input, 600, () =>
        {
            if (undock ? ownDock.Docked || otherDock.Docked || ownDock.DockJoint != null || otherDock.DockJoint != null
                : ownDock.DockedWith != state.Other || otherDock.DockedWith != state.Own || ownDock.DockJoint == null || otherDock.DockJoint == null)
                return null;
            var observation = WorldProbe?.Invoke(ship.Grid, state.Own, input.Type, false);
            if (WorldProbe != null && observation == null) return null;
            if (undock)
            {
                var rotation = transforms.GetWorldRotation(state.Own);
                transforms.SetWorldPosition(state.Grid, transforms.GetWorldPosition(state.Own) + 5 * rotation.ToWorldVec()
                    - (rotation + MathF.PI).RotateVec(new Vector2(.5f, .5f)));
            }
            return new { nativeDockingSystem = true, docked = !undock, reciprocalPorts = true, physicsJointVerified = true, retainedAuxiliaryGrid = true,
                ownPort = state.Own.ToString(), otherPort = state.Other.ToString(), kiasObservation = observation };
        });
    }

    private static object ExerciseFiniteMagazine(Ship ship, Input input)
    {
        var foreign = CreateForeign(ship, new Vector2(-100, -100));
        var shooter = _em.SpawnEntity("MobHuman", new EntityCoordinates(foreign, new Vector2(.5f, .5f)));
        AttachTrackedMind(shooter, input.Ship + "/finite-ammo");
        var weapon = _em.SpawnEntity("WeaponPistolMk58", _em.GetComponent<TransformComponent>(shooter).Coordinates);
        var target = _em.SpawnEntity("WallReinforced", new EntityCoordinates(foreign, new Vector2(2.5f, .5f)));
        if (!_em.System<SharedHandsSystem>().TryPickup(shooter, weapon)) throw new InvalidOperationException("Native finite-ammo shooter cannot hold the gun.");
        _em.System<SharedCombatModeSystem>().SetInCombatMode(shooter, true);
        var use = new Content.Shared.Interaction.Events.UseInHandEvent(shooter);
        _em.EventBus.RaiseLocalEvent(weapon, use);
        _em.EnsureComponent<NativeLabGunObserverComponent>(weapon);
        var gun = _em.GetComponent<GunComponent>(weapon);
        var initial = NativeAmmoCount(weapon);
        if (initial <= 0 || initial > 50) throw new InvalidOperationException("Native finite magazine has an invalid starting count.");
        var previousFire = gun.LastFire;
        var shots = 0;
        var reloaded = false;
        var reloadCount = 0;
        uint emptySince = 0;
        return AwaitNative(input, 2400, () =>
        {
            if (gun.LastFire > previousFire) { shots++; previousFire = gun.LastFire; }
            var count = NativeAmmoCount(weapon);
            if (!reloaded && count == 0)
            {
                if (emptySince == 0) emptySince = _timing.CurTick.Value;
                _em.System<SharedGunSystem>().AttemptShoot(shooter, weapon, gun, _em.GetComponent<TransformComponent>(target).Coordinates, target);
                if (_timing.CurTick.Value - emptySince < 90) return null;
                if (gun.LastFire != previousFire || shots != initial) throw new InvalidOperationException("Native empty gun fired or ammunition accounting differs from actual shots.");
                var slots = _em.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>();
                if (!slots.TryEject(weapon, "gun_magazine", null, out var oldMagazine)) throw new InvalidOperationException("Finite native magazine ejection failed.");
                var magazinePrototype = _em.GetComponent<MetaDataComponent>(oldMagazine.Value).EntityPrototype?.ID
                    ?? throw new InvalidOperationException("Finite native magazine has no reloadable prototype.");
                _em.DeleteEntity(oldMagazine.Value);
                var magazine = _em.SpawnEntity(magazinePrototype, _em.GetComponent<TransformComponent>(shooter).Coordinates);
                if (_em.GetComponent<BallisticAmmoProviderComponent>(magazine).InfiniteUnspawned
                    || !slots.TryInsert(weapon, "gun_magazine", magazine, shooter)) throw new InvalidOperationException("Finite native magazine replacement failed.");
                var rack = new Content.Shared.Interaction.Events.UseInHandEvent(shooter);
                _em.EventBus.RaiseLocalEvent(weapon, rack);
                reloadCount = NativeAmmoCount(weapon);
                if (reloadCount <= 0) throw new InvalidOperationException("Native reload did not supply ammunition.");
                reloaded = true;
                return null;
            }
            if (reloaded && count < reloadCount && shots == initial + 1)
            {
                var hits = _em.System<NativeGameObserverSystem>().Hits.Values.Count(hit => hit.Target == target);
                if (hits == 0) return null;
                _em.DeleteEntity(foreign);
                return new { nativeFiniteMagazineExhausted = true, initialRounds = initial, actualShots = shots,
                    emptyControlTicks = 90, nativeMagazineReplaced = true, reloadCount, roundsAfterReloadShot = count,
                    actualProjectileImpacts = hits, scope = "Finite Mk58 pistol magazine; PDC infinite cassette remains a separate control." };
            }
            if (!reloaded || shots < initial + 1)
                _em.System<SharedGunSystem>().AttemptShoot(shooter, weapon, gun, _em.GetComponent<TransformComponent>(target).Coordinates, target);
            return null;
        }, () => new { initial, shots, reloaded, reloadCount, ammunition = NativeAmmoCount(weapon), gun.LastFire, gun.NextFire });
    }

    private static readonly Dictionary<EntityUid, (EntityUid Rescuer, EntityUid Tool)> Rescuers = new();

    private static bool _networkAdmitted;
    private static DateTime _networkAdmittedUtc;
    private static long _networkAdmittedStamp;
    private static long _networkChecks;

    private static void PrepareNetworkPlayers(bool required)
    {
        var count = int.Parse(Environment.GetEnvironmentVariable("NATIVE_LAB_CLIENTS") ?? "0");
        if (count == 0) return;
        if (count < 0 || count > Ships.Count) throw new InvalidOperationException("Invalid native network client count.");
        var players = IoCManager.Resolve<ISharedPlayerManager>();
        var minds = _em.System<SharedMindSystem>();
        var bindings = new List<object>();
        var ships = Ships.OrderBy(pair => pair.Key).ToArray();
        for (var index = 0; index < count; index++)
        {
            var name = $"KiasBench{index + 1:0000}";
            var session = players.NetworkedSessions.SingleOrDefault(value => value.Name == name || value.Name == "localhost@" + name);
            if (session?.Channel is not { IsConnected: true, IsHandshakeComplete: true }) continue;
            if (session.Data.ContentDataUncast == null) continue;
            var actor = ships[index].Value.Actor;
            if (session.AttachedEntity != actor)
            {
                if (!minds.TryGetMind(actor, out var mind, out var component))
                    throw new InvalidOperationException("Network actor lost its tracked mind.");
                minds.SetUserId(mind, session.UserId, component);
                if (session.Status != Robust.Shared.Enums.SessionStatus.InGame) players.JoinGame(session);
                players.SetAttachedEntity(session, actor);
            }
            if (session.AttachedEntity != actor || session.Status != Robust.Shared.Enums.SessionStatus.InGame)
                throw new InvalidOperationException("Real network session failed to attach to native crew.");
            bindings.Add(new { name, sessionName = session.Name, ship = ships[index].Key, actor = actor.ToString(),
                user = session.UserId.ToString(), session.Channel.ConnectionId, session.Ping,
                connected = session.Channel.IsConnected, status = session.Status.ToString() });
        }
        if (bindings.Count == count && !_networkAdmitted)
        {
            _networkAdmitted = true;
            _networkAdmittedUtc = DateTime.UtcNow;
            _networkAdmittedStamp = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        _networkChecks++;
        var snapshot = new { required = count, connected = bindings.Count,
            engineTick = _timing.CurTick.Value, bindings, utc = DateTime.UtcNow,
            admitted = _networkAdmitted, admittedUtc = _networkAdmitted ? (DateTime?) _networkAdmittedUtc : null,
            admissionElapsedSeconds = _networkAdmitted ? System.Diagnostics.Stopwatch.GetElapsedTime(_networkAdmittedStamp).TotalSeconds : 0,
            checks = _networkChecks };
        AtomicJson("network-sessions.json", snapshot);
        _networkHistory!.WriteLine(JsonSerializer.Serialize(snapshot));
        if (ProfileEnabled) _profileFlushStreams = true;
        else _networkHistory.Flush();
        if ((required || _networkAdmitted) && bindings.Count != count)
            throw new InvalidOperationException($"Real network clients unavailable: {bindings.Count}/{count}.");
    }

    private static void AttachTrackedMind(EntityUid actor, string identity)
    {
        var user = new NetUserId(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16)));
        var players = IoCManager.Resolve<ISharedPlayerManager>();
        players.CreateAndAddSession(user, identity);
        players.RemoveSession(user, removeData: false);
        _em.System<SharedMindSystem>().TransferTo(_em.System<SharedMindSystem>().CreateMind(user, identity), actor);
    }

    private static void PrepareCrewBackpack(EntityUid actor)
    {
        var inventory = _em.System<Content.Shared.Inventory.InventorySystem>();
        if (inventory.TryGetSlotEntity(actor, "back", out _)) return;
        var backpack = _em.SpawnEntity("ClothingBackpack", _em.GetComponent<TransformComponent>(actor).Coordinates);
        if (!inventory.TryEquip(actor, backpack, "back")) throw new InvalidOperationException("Native crew backpack equip failed.");
    }

    private static void PrepareCrewRegistration()
    {
        foreach (var ship in Ships.Values)
        {
            PrepareCrewBackpack(ship.Actor);
            RegisterCrew?.Invoke(ship.Grid, ship.Actor);
        }
        _crewRegistered = true;
    }

    private static void Heal(EntityUid actor)
    {
        var heal = new DamageSpecifier();
        foreach (var (kind, amount) in _em.GetComponent<DamageableComponent>(actor).Damage.DamageDict)
            heal.DamageDict.Add(kind, -amount);
        _em.System<DamageableSystem>().TryChangeDamage(actor, heal);
    }

    private static object ChangeCrew(Ship ship, Input input)
    {
        var death = input.Type is "crew.death" or "crew.revive";
        var actor = ship.Actor;
        if (death && !DeathActors.TryGetValue(ship.Grid, out actor))
        {
            if (input.Type == "crew.revive") throw new InvalidOperationException("Death actor was not created.");
            actor = _em.SpawnEntity("MobHuman", new EntityCoordinates(ship.Grid, new Vector2(-4.5f, .5f)));
            AttachTrackedMind(actor, input.Ship + "/death");
            PrepareCrewBackpack(actor);
            RegisterCrew?.Invoke(ship.Grid, actor);
            DeathActors.Add(ship.Grid, actor);
        }
        var target = actor;
        if (input.Type is "crew.critical" or "crew.death")
        {
            var desired = input.Type == "crew.death" ? MobState.Dead : MobState.Critical;
            var before = _em.GetComponent<MobStateComponent>(target).CurrentState;
            var beforeDamage = _em.GetComponent<DamageableComponent>(target).Damage.ToString();
            if (before != MobState.Alive)
            {
                var old = target;
                var oldCoordinates = _em.GetComponent<TransformComponent>(old).Coordinates;
                target = _em.SpawnEntity("MobHuman", oldCoordinates);
                var minds = _em.System<SharedMindSystem>();
                if (!minds.TryGetMind(old, out var mind, out _))
                    throw new InvalidOperationException("Crew replacement requires the existing real mind.");
                var inventory = _em.System<Content.Shared.Inventory.InventorySystem>();
                if (!inventory.TryUnequip(old, "back", out var backpack, force: true))
                {
                    AtomicJson($"crew-replacement-failed-{input.Id}.json", new { stage = "unequip", oldActor = old.ToString(),
                        oldCoordinates = oldCoordinates.ToString(), beforeState = before.ToString(), beforeDamage,
                        hasBackpack = inventory.TryGetSlotEntity(old, "back", out _) });
                    throw new InvalidOperationException("Crew replacement could not unequip the original registered backpack.");
                }
                if (!inventory.TryEquip(target, backpack.Value, "back"))
                {
                    inventory.CanEquip(target, backpack.Value, "back", out var reason);
                    AtomicJson($"crew-replacement-failed-{input.Id}.json", new { stage = "equip", oldActor = old.ToString(),
                        oldCoordinates = oldCoordinates.ToString(), newActor = target.ToString(), reason,
                        backpackCoordinates = _em.GetComponent<TransformComponent>(backpack.Value).Coordinates.ToString() });
                    throw new InvalidOperationException("Crew replacement could not equip the original registered backpack: " + reason);
                }
                _em.System<SharedTransformSystem>().SetCoordinates(target, new EntityCoordinates(ship.Grid, new Vector2(-4.5f, .5f)));
                minds.TransferTo(mind, target);
                if (!minds.TryGetMind(target, out var transferredMind, out _) || transferredMind != mind)
                    throw new InvalidOperationException("Crew replacement did not retain its original mind.");
                RegisterCrew?.Invoke(ship.Grid, target);
                if (death) DeathActors[ship.Grid] = target; else ship.Actor = target;
                _em.DeleteEntity(old);
                AtomicJson($"crew-replacement-{input.Id}.json", new { oldActor = old.ToString(), beforeState = before.ToString(),
                    beforeDamage, newActor = target.ToString(), normalSpawn = true, registered = RegisterCrew != null,
                    preservedMind = mind.ToString(), preservedBackpack = backpack.ToString(), oldActorDeleted = !_em.EntityExists(old),
                    inventoryTransferredAtPreviousActor = true, oldCoordinates = oldCoordinates.ToString() });
            }
            if (_em.GetComponent<MobStateComponent>(target).CurrentState != MobState.Alive)
                throw new InvalidOperationException("Crew damage wave must start with a living actor after normal fixture preparation.");
            var threshold = _em.GetComponent<MobThresholdsComponent>(target).Thresholds.First(pair => pair.Value == desired).Key;
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Bloodloss", threshold + 1 - _em.GetComponent<DamageableComponent>(target).TotalDamage);
            AtomicJson($"crew-before-{input.Id}.json", new { actor = target.ToString(), beforeState = "Alive", beforeDamage,
                threshold = threshold.ToString(), totalDamage = _em.GetComponent<DamageableComponent>(target).TotalDamage.ToString(),
                realMind = _em.HasComponent<Content.Shared.Mind.Components.MindContainerComponent>(target), registered = RegisterCrew != null });
            _em.System<DamageableSystem>().TryChangeDamage(target, damage);
            return AwaitNative(input, 120, () => _em.GetComponent<MobStateComponent>(target).CurrentState == desired
                ? new { actor = target.ToString(), mobState = desired.ToString(), independentDeathActor = death,
                    realMind = _em.HasComponent<Content.Shared.Mind.Components.MindContainerComponent>(target) } : null);
        }
        Heal(target);
        if (input.Type == "crew.revive")
        {
            if (_em.GetComponent<MobStateComponent>(target).CurrentState != MobState.Dead)
                throw new InvalidOperationException("Revival must begin with a real corpse.");
            var rescuer = _em.SpawnEntity("MobHuman", _em.GetComponent<TransformComponent>(target).Coordinates);
            var tool = _em.SpawnEntity("Defibrillator", _em.GetComponent<TransformComponent>(rescuer).Coordinates);
            Rescuers.Add(target, (rescuer, tool));
            if (!_em.System<SharedHandsSystem>().TryPickup(rescuer, tool)
                || !_em.System<ItemToggleSystem>().TryActivate(tool, rescuer)
                || !_em.System<DefibrillatorSystem>().TryStartZap(tool, target, rescuer))
                throw new InvalidOperationException("Native defibrillator interaction rejected.");
        }
        return AwaitNative(input, 600, () =>
        {
            if (_em.GetComponent<MobStateComponent>(target).CurrentState == MobState.Dead) return null;
            Heal(target);
            if (_em.GetComponent<MobStateComponent>(target).CurrentState != MobState.Alive) return null;
            if (Rescuers.Remove(target, out var rescuer)) { _em.DeleteEntity(rescuer.Tool); _em.DeleteEntity(rescuer.Rescuer); }
            return new { actor = target.ToString(), mobState = "Alive", nativeDefibrillator = input.Type == "crew.revive" };
        });
    }

    private static void ResetGameDrivers()
    {
        _networkAdmitted = false; _networkAdmittedUtc = default; _networkAdmittedStamp = 0; _networkChecks = 0;
        _pendingProgressJson = null; _progressFirstFailure = 0; _progressLastAttempt = 0;
        _progressWriteFailures = 0; _progressWriteError = string.Empty;
        OperatedDoors.Clear(); BreachedFloors.Clear(); Visitors.Clear(); Fauna.Clear(); VisitorGuns.Clear(); CrewTrips.Clear(); AnomalyPulseEntities.Clear(); AnomalyFloors.Clear(); RadioFixtures.Clear(); FtlVisitors.Clear(); VentFixtures.Clear(); FireFixtures.Clear(); CollisionShields.Clear(); CollisionRepairs.Clear(); CollisionExtensions.Clear();
        CrewTripPlatforms.Clear(); DockChecks.Clear();
        ExpectedOfflineGrids.Clear(); PendingGameDrivers.Clear(); ForeignGrids.Clear(); DeathActors.Clear(); Rescuers.Clear(); GunFixtures.Clear(); PdcFixtures.Clear(); PdcRemovedMagazines.Clear(); PdcMagazineOperators.Clear(); FtlOrigins.Clear(); _nativeCompleted = 0; _crewRegistered = false; _physicsDiagnosticStarted = false; PauseRuntime = false; PauseRoomScans = false;
    }

    private static object AwaitNative(Input input, uint deadlineTicks, Func<object?> observe, Func<object>? diagnostics = null)
    {
        PendingGameDrivers.Add(new(input, _timing.CurTick.Value, _timing.CurTick.Value + deadlineTicks, observe, diagnostics));
        return new { nativeStarted = true, asynchronous = true, deadlineTicks };
    }

    private static void PollGameDrivers(uint tick)
    {
        for (var i = PendingGameDrivers.Count - 1; i >= 0; i--)
        {
            var pending = PendingGameDrivers[i];
            var proof = pending.Observe();
            if (proof == null)
            {
                if (_timing.CurTick.Value >= pending.Deadline)
                {
                    AtomicJson("native-timeout.json", new { pending.Input.Id, pending.Input.Type, pending.Started, pending.Deadline,
                        actualTick = _timing.CurTick.Value, diagnostics = pending.Diagnostics?.Invoke() });
                    throw new InvalidOperationException($"Native postcondition timeout: {pending.Input.Id} / {pending.Input.Type}.");
                }
                continue;
            }
            var record = new { id = pending.Input.Id, ship = pending.Input.Ship, type = pending.Input.Type,
                scheduledTick = pending.Input.Tick, nativeStartedTick = pending.Started,
                nativeCompletedTick = _timing.CurTick.Value, relativeCompletedTick = tick,
                latencyTicks = _timing.CurTick.Value - pending.Started, postconditionTrue = true, proof };
            _outcomes!.WriteLine(JsonSerializer.Serialize(record));
            PendingGameDrivers.RemoveAt(i); _nativeCompleted++;
        }
    }

    private static EntityUid CreateForeign(Ship ship, Vector2 offset)
    {
        var maps = _em.System<SharedMapSystem>();
        var transforms = _em.System<SharedTransformSystem>();
        var foreign = maps.CreateGridEntity(_em.GetComponent<TransformComponent>(ship.Grid).MapID);
        var floor = maps.GetTileRef(ship.Grid, _em.GetComponent<MapGridComponent>(ship.Grid), ship.FireTile).Tile;
        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++) maps.SetTile(foreign, new Vector2i(x, y), floor);
        _em.EnsureComponent<ShuttleComponent>(foreign);
        _em.System<SharedPhysicsSystem>().SetBodyType(foreign, BodyType.Dynamic);
        transforms.SetWorldPosition(foreign, transforms.GetWorldPosition(ship.Grid) + offset);
        return foreign;
    }

    private static object ApplyGameDriver(Ship ship, Input input)
    {
        var transforms = _em.System<SharedTransformSystem>();
        var observer = _em.System<NativeGameObserverSystem>();
        switch (input.Type)
        {
            case "dock.dock":
            case "dock.undock": return ExerciseDocking(ship, input);
            case "ammo.finite_cycle": return ExerciseFiniteMagazine(ship, input);
            case "navigation.arrive": return NavigateNative(ship, input);
            case "fire.ignite_suppress": return ExerciseFire(ship, input);
            case "fire.clear" when FireFixtures.ContainsKey(ship.Grid): return ExerciseFire(ship, input);
            case "collision.damage_threshold": return ExerciseCollision(ship, input);
            case "atmos.depressurize_vent":
            case "atmos.restore": return ExerciseVent(ship, input);
            case "ftl.visitor_in":
            case "ftl.visitor_out": return VisitByFtl(ship, input);
            case "radio.jam":
            case "radio.restore": return ProbeRadio(ship, input);
            case "anomaly.progress": return ProgressAnomaly(ship, input);
            case "crew.depart":
            case "crew.return": return MoveCrew(ship, input);
            case "crew.visitor":
            case "crew.visitor_clear":
            case "crew.armed_threat":
            case "crew.threat_clear":
            case "fauna.enter":
            case "fauna.clear": return IntroduceLife(ship, input);
            case "door.open":
            case "door.close":
            case "geometry.breach":
            case "geometry.repair": return OperateRoom(ship, input);
            case "pdc.hostile":
            case "pdc.friendly":
            case "pdc.blocked":
            case "pdc.empty": return FirePdcThreat(ship, input);
            case "pdc.unload":
            case "pdc.reload": return ChangePdcMagazine(ship, input);
            case "ftl.console_depart":
            case "ftl.console_return": return ConsoleFtl(ship, input);
            case "gun.fire": return FireNativeGun(ship, input);
            case "gun.clear":
                foreach (var fixture in GunFixtures[ship.Grid]) if (_em.EntityExists(fixture)) _em.DeleteEntity(fixture);
                GunFixtures.Remove(ship.Grid);
                _em.DeleteEntity(ForeignGrids[ship.Grid]); ForeignGrids.Remove(ship.Grid);
                return new { cleared = true };
            case "crew.critical":
            case "crew.recover":
            case "crew.death":
            case "crew.revive":
                return ChangeCrew(ship, input);
            case "data.cut":
            case "runtime.all_any":
            case "data.restore":
            case "core.power_loss":
            case "core.power_restore":
            case "rack.power_loss":
            case "rack.power_restore":
                if (KiasDriver == null) return AwaitNative(input, 1, () => new { baselineWithoutKias = true, nativeDevicesAbsent = true });
                KiasDriver(ship.Grid, input.Type, true);
                return AwaitNative(input, 1800, () => KiasDriver(ship.Grid, input.Type, false));
            case "collision.start":
            {
                if (ForeignGrids.ContainsKey(ship.Grid)) throw new InvalidOperationException("Foreign fixture already exists.");
                KiasDriver?.Invoke(ship.Grid, "collision.prepare", true);
                var bounds = _em.GetComponent<MapGridComponent>(ship.Grid).LocalAABB;
                var foreign = CreateForeign(ship, new Vector2(bounds.Right + 4, 0));
                ForeignGrids.Add(ship.Grid, foreign);
                _em.EnsureComponent<NativeLabCollisionObserverComponent>(ship.Grid);
                _em.EnsureComponent<NativeLabCollisionObserverComponent>(foreign);
                var started = _timing.CurTick.Value;
                _em.System<SharedPhysicsSystem>().SetLinearVelocity(foreign, new Vector2(-20, 0));
                return AwaitNative(input, 600, () => observer.Collisions.TryGetValue(ship.Grid, out var contact)
                    && contact.Tick >= started && contact.Other == foreign
                    ? new { realContactTick = contact.Tick, relativeSpeed = contact.Speed, other = foreign.ToString() } : null);
            }
            case "collision.clear":
                ExpectedOfflineGrids[ship.Grid] = _timing.CurTick.Value + 1800;
                if (_em.EntityExists(ForeignGrids[ship.Grid])) _em.DeleteEntity(ForeignGrids[ship.Grid]);
                ForeignGrids.Remove(ship.Grid);
                _em.System<SharedPhysicsSystem>().SetLinearVelocity(ship.Grid, Vector2.Zero);
                _em.System<SharedPhysicsSystem>().SetAngularVelocity(ship.Grid, 0);
                var repairedTiles = 0;
                var rebuilt = new List<object>();
                if (CollisionRepairs.Remove(ship.Grid, out var repair))
                {
                    var repairMaps = _em.System<SharedMapSystem>();
                    var repairGrid = _em.GetComponent<MapGridComponent>(ship.Grid);
                    foreach (var (cell, tile) in repair.Tiles)
                        if (repairMaps.GetTileRef(ship.Grid, repairGrid, cell).Tile != tile)
                        { repairMaps.SetTile((ship.Grid, repairGrid), cell, tile); repairedTiles++; }
                    foreach (var entity in repair.Entities)
                    {
                        var restored = entity.Uid;
                        if (!_em.EntityExists(restored))
                        {
                            restored = _em.SpawnEntity(entity.Prototype, new EntityCoordinates(ship.Grid, entity.Position));
                            rebuilt.Add(new { original = entity.Uid.ToString(), replacement = restored.ToString(), entity.Prototype });
                        }
                        var restoredTransform = _em.GetComponent<TransformComponent>(restored);
                        if (restoredTransform.ParentUid != ship.Grid || restoredTransform.LocalPosition != entity.Position)
                            transforms.SetCoordinates(restored, new EntityCoordinates(ship.Grid, entity.Position));
                        if (restoredTransform.LocalRotation != entity.Rotation)
                            transforms.SetLocalRotation(restored, entity.Rotation);
                        if (!restoredTransform.Anchored && !transforms.AnchorEntity(restored))
                            throw new InvalidOperationException("Native collision repair could not anchor " + entity.Prototype);
                        if (entity.Damage is { } saved && _em.TryGetComponent<DamageableComponent>(restored, out var damageable))
                        {
                            var delta = new DamageSpecifier();
                            foreach (var (kind, amount) in damageable.Damage.DamageDict)
                                if (amount > saved.DamageDict.GetValueOrDefault(kind))
                                    delta.DamageDict[kind] = saved.DamageDict.GetValueOrDefault(kind) - amount;
                            if (delta.DamageDict.Count > 0) _em.System<DamageableSystem>().TryChangeDamage(restored, delta);
                        }
                    }
                }
                if (CollisionShields.Remove(ship.Grid, out var shieldStates))
                    foreach (var (shield, disabled) in shieldStates)
                        if (_em.EntityExists(shield)) _em.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().SetPowerDisabled(shield, disabled);
                if (CollisionExtensions.Remove(ship.Grid, out var extension))
                {
                    foreach (var wall in extension.Walls) if (_em.EntityExists(wall)) _em.DeleteEntity(wall);
                    foreach (var (cell, tile) in extension.Tiles)
                        _em.System<SharedMapSystem>().SetTile((ship.Grid, _em.GetComponent<MapGridComponent>(ship.Grid)), cell, tile);
                }
                return AwaitNative(input, 1800, () =>
                {
                    var recovery = KiasDriver?.Invoke(ship.Grid, "collision.recovery", false);
                    if (KiasDriver != null && recovery == null) return null;
                    ExpectedOfflineGrids.Remove(ship.Grid);
                    return new { cleared = true, repairedTiles, rebuiltNativeEntities = rebuilt, recovery,
                        repairScope = "Native tile construction, prototype reconstruction and damage healing; destroyed entities receive new identities." };
                }, () => new { repairedTiles, rebuiltNativeEntities = rebuilt,
                    recovery = KiasDriver?.Invoke(ship.Grid, "collision.inspect", false) });
            case "ftl.in":
            case "ftl.out":
            {
                if (!ForeignGrids.TryGetValue(ship.Grid, out var foreign))
                {
                    foreign = CreateForeign(ship, new Vector2(1000, 1000));
                    ForeignGrids.Add(ship.Grid, foreign);
                }
                var destination = transforms.GetWorldPosition(ship.Grid)
                    + (input.Type == "ftl.in" ? new Vector2(100, 100) : new Vector2(1000, 1000));
                var map = transforms.GetMapCoordinates(ship.Grid);
                var started = _timing.CurTick.Value;
                _em.System<ShuttleSystem>().FTLToCoordinates(foreign, _em.GetComponent<ShuttleComponent>(foreign),
                    new EntityCoordinates(_em.System<SharedMapSystem>().GetMap(map.MapId), destination), Angle.Zero,
                    startupTime: .5f, hyperspaceTime: .5f);
                return AwaitNative(input, 1800, () => observer.FtlCompleted.TryGetValue(foreign, out var completed)
                    && completed >= started && transforms.GetMapCoordinates(foreign).MapId == map.MapId
                    && Vector2.Distance(transforms.GetWorldPosition(foreign), destination) < 10
                    ? new { actualFtlCompletedTick = completed, destination = destination.ToString(),
                        actualPosition = transforms.GetWorldPosition(foreign).ToString(), nativeTransit = true } : null);
            }
            case "ftl.clear":
                _em.DeleteEntity(ForeignGrids[ship.Grid]); ForeignGrids.Remove(ship.Grid);
                return new { cleared = true };
            default: throw new InvalidOperationException($"Unimplemented native replay driver: {input.Type}");
        }
    }
}
