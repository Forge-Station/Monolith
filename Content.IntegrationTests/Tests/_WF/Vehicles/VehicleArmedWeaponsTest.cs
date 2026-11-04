#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Mono.CCVar;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Explosion.Components;
using Content.Shared.Explosion.Components.OnTrigger;
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Every armed hardpoint of the roster, one after the other, on a vehicle whose slot takes it: a gunner strapped into a
/// weapons seat picks it and fires, and exactly one round (or the round's pellets) leaves the muzzle on the exterior map
/// without touching the hull, the ready magazine loses a round, the next magazine comes up from the reserve, and a round that
/// is meant to stop at the aimed point does. The two APC port guns are fired from the port gun seat.
/// </summary>
public sealed partial class VehicleArmedWeaponsTest : InteractionTest
{
    private const string InteriorFolder = "/Maps/_WF/Vehicles/Interiors/";
    private const string TestInterior = "wf_test_interior.yml";
    private const string SelectAction = "ActionVehicleSelectHardpoint";

    // The vehicle stands here on a wide plating grid, facing the grid's south; the aim is this far from it.
    private static readonly Vector2 VehicleSpot = new(8.5f, 8.5f);
    private const float AimDistance = 9f;

    // A fixed-distance round comes to rest within a tick of travel of the aimed point (see ShootAtFixedPointSystem): its speed
    // over the tick rate, and a little for where in the tick the stop is checked.
    private const float StopTolerance = 0.25f;

    protected override string PlayerPrototype => "MobHuman";

    // Shells that hit things play sounds, and the client's room echo asserts on audio whose parent changes mid-setup.
    [SetUp]
    public async Task DisableRoomEcho()
    {
        _checkedFlights.Clear();
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, false));
    }

    [TearDown]
    public async Task RestoreRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, MonoCVars.AreaEchoEnabled.DefaultValue));
    }

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFFlybyTestBullet
  parent: BaseBullet
  categories: [ HideSpawnMenu ]
";

    /// <summary>Notes where a round reached the end of its fixed distance.</summary>
    public sealed partial class FixedStopProbeSystem : EntitySystem
    {
        [Dependency] private SharedTransformSystem _transform = default!;

        public readonly Dictionary<EntityUid, Vector2> Stops = new();

        /// <summary>What a round was parented to, and where on it, when it stopped.</summary>
        public readonly Dictionary<EntityUid, (EntityUid Parent, Vector2 Local)> StopFrames = new();

        /// <summary>Rounds that were deleted still in flight, as the cannon shell is when it goes off on a hit.</summary>
        public readonly Dictionary<EntityUid, Vector2> Ends = new();

        public override void Initialize()
        {
            SubscribeLocalEvent<ProjectileFixedDistanceComponent, ProjectileFixedDistanceStopEvent>(OnStop);
            SubscribeLocalEvent<ProjectileFixedDistanceComponent, EntityTerminatingEvent>(OnTerminating);
        }

        private void OnTerminating(Entity<ProjectileFixedDistanceComponent> ent, ref EntityTerminatingEvent args)
        {
            Ends[ent.Owner] = _transform.GetWorldPosition(ent.Owner);
        }

        private void OnStop(Entity<ProjectileFixedDistanceComponent> ent, ref ProjectileFixedDistanceStopEvent args)
        {
            Stops[ent.Owner] = _transform.GetWorldPosition(ent.Owner);
            var xform = Transform(ent.Owner);
            StopFrames[ent.Owner] = (xform.ParentUid, xform.LocalPosition);
        }
    }

    /// <summary>Notes the rounds whose trigger-on-collide went off.</summary>
    public sealed partial class CollideTriggerProbeSystem : EntitySystem
    {
        [Dependency] private INetManager _net = default!;
        [Dependency] private SharedTransformSystem _transform = default!;

        public readonly HashSet<EntityUid> Triggered = new();

        /// <summary>Where each round was when its collide trigger went off, which is where its blast starts.</summary>
        public readonly Dictionary<EntityUid, Vector2> TriggerPositions = new();

        /// <summary>Where each entity was when anything triggered it first: a fuse running out, a stop, a hit.</summary>
        public readonly Dictionary<EntityUid, Vector2> AnyTriggers = new();

        /// <summary>What each entity was parented to, and where on it, when anything triggered it first.</summary>
        public readonly Dictionary<EntityUid, (EntityUid Parent, Vector2 Local)> AnyTriggerFrames = new();

        public override void Initialize()
        {
            // The component is the server's alone.
            if (_net.IsServer)
            {
                SubscribeLocalEvent<TriggerOnCollideComponent, TriggerEvent>(OnTrigger);
                SubscribeLocalEvent<TriggerEvent>(OnAnyTrigger);
            }
        }

        private void OnAnyTrigger(TriggerEvent args)
        {
            if (AnyTriggers.ContainsKey(args.Triggered))
                return;

            AnyTriggers[args.Triggered] = _transform.GetWorldPosition(args.Triggered);
            var xform = Transform(args.Triggered);
            AnyTriggerFrames[args.Triggered] = (xform.ParentUid, xform.LocalPosition);
        }

        private void OnTrigger(EntityUid uid, TriggerOnCollideComponent comp, TriggerEvent args)
        {
            Triggered.Add(uid);
            TriggerPositions[uid] = _transform.GetWorldPosition(uid);
        }
    }

    /// <summary>Where an armed hardpoint goes on a vehicle: the slot of the vehicle, and the slot of a starting turret in it.</summary>
    private sealed record Mount(string Vehicle, string Slot, string? ChildSlot);

    [Test]
    public async Task EveryArmedHardpointFiresFromASeatedOperator()
    {
        var guns = new List<EntityPrototype>();
        var mounts = new Dictionary<string, Mount>();
        var failures = new List<string>();

        await Server.WaitPost(() =>
        {
            var factory = SEntMan.ComponentFactory;
            foreach (var proto in ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract ||
                    proto.ID.Contains("Test") ||
                    !proto.TryComp<HardpointItemComponent>(out _, factory) ||
                    !proto.TryComp<GunComponent>(out _, factory))
                {
                    continue;
                }

                guns.Add(proto);
            }

            foreach (var gun in guns)
            {
                if (TryFindMount(gun, out var mount))
                    mounts[gun.ID] = mount;
                else
                    failures.Add($"{gun.ID}: no vehicle has a slot that takes it.");
            }
        });

        Assert.That(guns, Has.Count.GreaterThanOrEqualTo(25), "The armed hardpoints are missing.");

        foreach (var gun in guns.OrderBy(g => g.ID))
        {
            if (mounts.TryGetValue(gun.ID, out var mount))
                await CheckGun(gun, mount, failures);
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// The first vehicle, the original roster before the pre-armed variants, with a slot that takes the gun. Only a vehicle with
    /// the weapons component counts: an unarmed one, the AEV, has no weapons seat to fire from.
    /// </summary>
    private bool TryFindMount(EntityPrototype gun, out Mount mount)
    {
        var factory = SEntMan.ComponentFactory;
        var item = Comp<HardpointItemComponent>(gun);
        var vehicles = ProtoMan.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract && IsRosterVehicle(p) && p.TryComp<VehicleWeaponsComponent>(out _, factory))
            .OrderBy(p => p.ID.StartsWith("WF") ? 1 : 0)
            .ThenBy(p => p.ID);

        foreach (var vehicle in vehicles)
        {
            if (!vehicle.TryComp<HardpointSlotsComponent>(out var slots, factory))
                continue;

            vehicle.TryComp<ItemSlotsComponent>(out var itemSlots, factory);
            var itemSlotMap = itemSlots?.Slots;

            foreach (var slot in slots.Slots)
            {
                if (Accepts(item, slots, slot))
                {
                    mount = new Mount(vehicle.ID, slot.Id, null);
                    return true;
                }

                // A turret in the slot from the start carries slots of its own.
                if (itemSlots == null ||
                    !itemSlotMap!.TryGetValue(slot.Id, out var itemSlot) ||
                    itemSlot.StartingItem is not { } turretId ||
                    !ProtoMan.TryIndex<EntityPrototype>(turretId, out var turret) ||
                    !turret.TryComp<HardpointSlotsComponent>(out var turretSlots, factory))
                {
                    continue;
                }

                foreach (var nested in turretSlots.Slots)
                {
                    if (Accepts(item, turretSlots, nested))
                    {
                        mount = new Mount(vehicle.ID, slot.Id, nested.Id);
                        return true;
                    }
                }
            }
        }

        mount = default!;
        return false;
    }

    private bool IsRosterVehicle(EntityPrototype proto)
    {
        var factory = SEntMan.ComponentFactory;
        if (!proto.TryComp<RMCVehicleComponent>(out _, factory) ||
            !proto.TryComp<VehicleEnterComponent>(out var enter, factory))
        {
            return false;
        }

        var interiorPath = enter.InteriorPath;
        var path = interiorPath.ToString();
        return path.StartsWith(InteriorFolder) && !path.EndsWith(TestInterior);
    }

    /// <summary>The slot rule of the hardpoint system, on prototype data.</summary>
    private static bool Accepts(HardpointItemComponent item, HardpointSlotsComponent slots, HardpointSlot slot)
    {
        if (slots.VehicleFamily != null && item.VehicleFamily != slots.VehicleFamily)
            return false;

        if (slot.SlotType != null && item.SlotType != slot.SlotType)
            return false;

        if (item.CompatibilityId != slot.CompatibilityId)
            return false;

        if (slot.HardpointType != default && item.HardpointType != slot.HardpointType)
            return false;

        return slot.Whitelist == null;
    }

    private T Comp<T>(EntityPrototype proto) where T : IComponent, new()
    {
        if (!proto.TryComp<T>(out var found, SEntMan.ComponentFactory))
            throw new InvalidOperationException($"{proto.ID} has no {typeof(T).Name}.");

        return found;
    }

    private List<EntityUid> Projectiles()
    {
        var found = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            found.Add(uid);
        }

        return found;
    }

    private async Task LayPlating()
    {
        await Server.WaitPost(() =>
        {
            var plating = TileMan["Plating"].TileId;
            for (var x = -14; x < 30; x++)
            {
                for (var y = -14; y < 30; y++)
                {
                    MapSystem.SetTile(MapData.Grid.Owner, MapData.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }
        });
    }

    /// <summary>Puts <paramref name="user"/> at the vehicle's first doorway, uses the vehicle and waits out the do-after.</summary>
    /// <summary>
    /// Walks the user in through the first doorway. Entering is a do-after that breaks on movement, so the user is
    /// stopped at the doorway first, and a try that a stray nudge or a stale doorway hold cancelled is tried again.
    /// </summary>
    private async Task<bool> Enter(EntityUid vehicle, EntityUid user)
    {
        var doorway = SEntMan.GetComponent<VehicleEnterComponent>(vehicle).EntryPoints[0].Offset;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Server.WaitPost(() =>
            {
                Transform.SetCoordinates(user, new EntityCoordinates(MapData.Grid.Owner, VehicleSpot + doorway));
                if (SEntMan.TryGetComponent<PhysicsComponent>(user, out var body))
                    SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(user, Vector2.Zero, body: body);
            });
            await RunTicks(2);

            await Server.WaitPost(() => SEntMan.System<SharedInteractionSystem>().InteractionActivate(user, vehicle));
            await RunTicks(120);

            if (SEntMan.HasComponent<VehicleInteriorOccupantComponent>(user))
                return true;
        }

        return false;
    }

    private static void ResetGun(GunComponent gun)
    {
        gun.NextFire = TimeSpan.Zero;
        gun.ShotCounter = 0;
        gun.BurstActivated = false;
        gun.BurstShotsCount = 0;
    }

    /// <summary>What a gun's prototype says about how it fires, read on the server thread.</summary>
    private sealed class GunData
    {
        public required HardpointItemComponent Item;
        public required int Pellets;
        public ShootAtFixedPointComponent? Fixed;
        public OnShootTriggerAmmoTimerComponent? Timer;
        public VehicleHardpointAmmoComponent? HardpointAmmo;
        public ProjectileMaxFixedRangeComponent? RoundCap;
        public bool TriggersAtStop;
        public required string Bullet;
        public required float Speed;
    }

    /// <summary>
    /// Fixed-distance flight profiles whose obstacle, face and moving-ship matrix already ran. The matrix tests how a round stops,
    /// which depends only on the round, its speed, timer and range, so guns sharing a profile run it once.
    /// </summary>
    private readonly HashSet<string> _checkedFlights = new();

    private static string FlightProfile(GunData data) =>
        $"{data.Bullet}|{data.Speed}|{data.Pellets}|{data.Timer?.Delay}|{data.Fixed?.MaxFixedRange}|{data.RoundCap?.MaxFixedRange}|{data.TriggersAtStop}";

    private GunData ReadGun(EntityPrototype gun)
    {
        var factory = SEntMan.ComponentFactory;
        var ammo = Comp<BallisticAmmoProviderComponent>(gun);
        var cartridge = ProtoMan.Index<EntityPrototype>(ammo.Proto!.Value);
        var bullet = ProtoMan.Index<EntityPrototype>(Comp<CartridgeAmmoComponent>(cartridge).Prototype);

        gun.TryComp<ShootAtFixedPointComponent>(out var fixedPoint, factory);
        gun.TryComp<OnShootTriggerAmmoTimerComponent>(out var timer, factory);
        gun.TryComp<VehicleHardpointAmmoComponent>(out var hardpointAmmo, factory);
        bullet.TryComp<ProjectileMaxFixedRangeComponent>(out var roundCap, factory);

        return new GunData
        {
            Item = Comp<HardpointItemComponent>(gun),
            Pellets = bullet.TryComp<ProjectileSpreadComponent>(out var spread, factory) ? spread.Count : 1,
            Fixed = fixedPoint,
            Timer = timer,
            HardpointAmmo = hardpointAmmo,
            RoundCap = roundCap,
            TriggersAtStop = bullet.TryComp<TriggerOnFixedDistanceStopComponent>(out _, factory),
            Bullet = bullet.ID,
            Speed = Comp<GunComponent>(gun).ProjectileSpeed,
        };
    }

    private async Task CheckGun(EntityPrototype gunProto, Mount mount, List<string> failures)
    {
        void Fail(string what) => failures.Add($"{gunProto.ID} on {mount.Vehicle}: {what}");

        GunData data = null!;
        await Server.WaitPost(() => data = ReadGun(gunProto));

        var item = data.Item;
        var expectedPellets = data.Pellets;
        var fixedPoint = data.Fixed != null;
        var fixedComp = data.Fixed;
        var timerProto = data.Timer != null;
        var timerComp = data.Timer;
        var hardpointAmmoProto = data.HardpointAmmo != null;
        var hardpointAmmoComp = data.HardpointAmmo;
        var roundCap = data.RoundCap;
        var triggersAtStop = data.TriggersAtStop;
        var probe = Server.System<FixedStopProbeSystem>();

        await LayPlating();

        var vehicle = EntityUid.Invalid;
        var gun = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            vehicle = SEntMan.SpawnEntity(mount.Vehicle, new EntityCoordinates(MapData.Grid.Owner, VehicleSpot));
            Transform.SetLocalRotation(vehicle, Angle.Zero);
            var engine = SEntMan.GetComponent<WFVehicleEngineComponent>(vehicle);
            engine.Running = true;
            engine.Electrics = true;
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var slots = SEntMan.System<ItemSlotsSystem>();
            var owner = vehicle;
            var slotId = mount.Slot;

            if (mount.ChildSlot != null)
            {
                if (!slots.TryGetSlot(vehicle, mount.Slot, out var turretSlot) || turretSlot.Item is not { } turret)
                {
                    Fail($"the vehicle spawned with no turret in {mount.Slot}.");
                    return;
                }

                owner = turret;
                slotId = mount.ChildSlot;
            }

            if (!slots.TryGetSlot(owner, slotId, out var slot))
            {
                Fail($"no item slot {slotId}.");
                return;
            }

            if (slot.Item is { } old)
                SEntMan.DeleteEntity(old);

            gun = SEntMan.SpawnEntity(gunProto.ID, new EntityCoordinates(owner, Vector2.Zero));
            if (!slots.TryInsert(owner, slotId, gun, null))
            {
                Fail($"the slot {slotId} would not take the gun.");
                gun = EntityUid.Invalid;
            }
        });

        if (!gun.IsValid())
        {
            await CleanUp(vehicle);
            return;
        }

        await RunTicks(3);

        if (!await Enter(vehicle, SPlayer))
        {
            Fail("could not get inside.");
            await CleanUp(vehicle);
            return;
        }

        // Strap into a weapons seat that takes this hardpoint type.
        var seat = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var interior = SEntMan.GetComponent<VehicleInteriorComponent>(vehicle);
            var query = SEntMan.EntityQueryEnumerator<VehicleWeaponsSeatComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var seatComp, out var xform))
            {
                if (xform.MapID != interior.MapId)
                    continue;

                var allowed = seatComp.AllowedHardpointTypes;
                if (allowed.Count != 0 && !allowed.Contains(item.HardpointType))
                    continue;

                seat = uid;
                Transform.SetCoordinates(SPlayer, xform.Coordinates);
                break;
            }
        });

        if (!seat.IsValid())
        {
            Fail("the interior has no weapons seat that takes the hardpoint's type.");
            await CleanUp(vehicle);
            return;
        }

        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            if (!SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, seat))
                Fail("could not strap into the weapons seat.");
        });
        await RunTicks(10);

        // Pick the gun the way the hotbar does.
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true);

            EntityUid? action = null;
            if (SEntMan.TryGetComponent(SPlayer, out ActionsComponent? actions))
            {
                foreach (var candidate in actions.Actions)
                {
                    if (SEntMan.GetComponent<MetaDataComponent>(candidate).EntityPrototype?.ID == SelectAction &&
                        SEntMan.TryGetComponent(candidate, out VehicleHardpointActionComponent? hardpointAction) &&
                        hardpointAction.MountedWeapon == gun)
                    {
                        action = candidate;
                        break;
                    }
                }
            }

            if (action == null)
            {
                Fail("the gunner got no select action for it.");
                return;
            }

            var actionSystem = SEntMan.System<SharedActionsSystem>();
            actionSystem.TryGetActionData(action, out var data);
            actionSystem.PerformAction(SPlayer, null, action.Value, data!, data!.BaseEvent, STiming.CurTime, predicted: false);

            if (!SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out var found, out _) || found != gun)
                Fail("the gun lookup does not answer with the picked gun for the gunner.");

            // A spin-up gun plays its select sound to everyone nearby, the gunner too: the selection runs on the server only,
            // so a sound predicted for the gunner would be one nobody plays for them.
            if (SEntMan.TryGetComponent(gun, out GunSpinupComponent? spinup) && spinup.SelectSound is SoundPathSpecifier selectPath)
            {
                var played = 0;
                var excludedGunner = false;
                var audioQuery = SEntMan.EntityQueryEnumerator<AudioComponent>();
                while (audioQuery.MoveNext(out _, out var audio))
                {
                    if (audio.FileName != selectPath.Path.ToString())
                        continue;

                    played++;
                    excludedGunner |= audio.ExcludedEntity == SPlayer;
                }

                if (played == 0)
                    Fail("picking the spin-up gun played no select sound.");
                else if (excludedGunner)
                    Fail("the select sound is not played to the gunner who picked the gun.");
            }
        });

        // One pull of the trigger, aimed straight ahead of the vehicle, or at the middle of the gun's arc.
        var aim = Vector2.Zero;
        var vehiclePos = Vector2.Zero;
        var arcOffset = Angle.Zero;
        await Server.WaitPost(() =>
        {
            vehiclePos = Transform.GetWorldPosition(vehicle);
            var facing = Transform.GetWorldRotation(vehicle);
            if (SEntMan.TryGetComponent(gun, out GunFireArcComponent? arc))
                arcOffset = arc.AngleOffset;

            aim = vehiclePos + (facing + arcOffset).ToWorldVec() * AimDistance;
        });

        var netGun = SEntMan.GetNetEntity(gun);
        var netAim = SEntMan.GetNetCoordinates(Transform.ToCoordinates(new MapCoordinates(aim, MapId)));
        var guns = SEntMan.System<SharedGunSystem>();
        var gunComp = SEntMan.GetComponent<GunComponent>(gun);
        var ammoComp = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun);
        var hullDamage = DamageSpecifierTotal(vehicle);
        var hullIntegrity = SEntMan.GetComponent<HardpointIntegrityComponent>(vehicle).Integrity;

        var shots = new List<EntityUid>();
        var readyBefore = 0;
        var muzzle = Vector2.Zero;

        await Server.WaitAssertion(() =>
        {
            ResetGun(gunComp);
            readyBefore = ammoComp.Count;

            // Where the shot should start: the muzzle the gun's offset names, or the gun itself.
            var muzzleSystem = SEntMan.System<GunMuzzleOffsetSystem>();
            var aimCoordinates = Transform.ToCoordinates(new MapCoordinates(aim, MapId));
            muzzle = SEntMan.TryGetComponent(gun, out GunMuzzleOffsetComponent? offset) &&
                     muzzleSystem.TryGetMuzzleCoordinates(gun, offset, aimCoordinates, out var muzzleCoordinates, out _)
                ? Transform.ToMapCoordinates(muzzleCoordinates).Position
                : Transform.GetWorldPosition(gun);

            guns.ShootRequested(netGun, netAim, null, null, ServerSession);
            shots = Projectiles();

            if (shots.Count != expectedPellets)
            {
                Fail($"one shot put {shots.Count} rounds in the air, not {expectedPellets}.");
                return;
            }

            foreach (var shot in shots)
            {
                var projectile = SEntMan.GetComponent<ProjectileComponent>(shot);
                if (Transform.GetMapId(shot) != MapId)
                    Fail("a round is not on the vehicle's map.");
                if (Vector2.Distance(Transform.GetWorldPosition(shot), muzzle) > 0.6f)
                    Fail($"a round started at {Transform.GetWorldPosition(shot)}, not at the muzzle {muzzle}.");
                if (projectile.Shooter != SPlayer || projectile.Weapon != gun)
                    Fail("a round does not name the gunner and the gun.");
                if (timerProto && timerComp!.Delay > 0f && !SEntMan.HasComponent<ActiveTimerTriggerComponent>(shot))
                    Fail("a round did not get the gun's trigger timer.");
                if (fixedPoint && !SEntMan.HasComponent<ProjectileFixedDistanceComponent>(shot))
                    Fail("a round was not given a fixed distance.");

                // The blast is data; a real one would break the plating the next vehicle stands on.
                SEntMan.RemoveComponent<ExplosiveComponent>(shot);
            }

            if (ammoComp.Count != readyBefore - 1)
                Fail($"the ready magazine went from {readyBefore} to {ammoComp.Count}, not down by one.");
        });

        // The rounds clear the hull.
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            foreach (var shot in shots)
            {
                if (SEntMan.Deleted(shot))
                {
                    // A shell that goes off at the aimed point may already be there.
                    if (!probe.Stops.ContainsKey(shot))
                        Fail("a round was gone within six ticks.");

                    continue;
                }

                if (Vector2.Distance(Transform.GetWorldPosition(shot), vehiclePos) < 1.5f)
                    Fail("a round is still on the hull after six ticks.");
            }

            if (DamageSpecifierTotal(vehicle) != hullDamage ||
                SEntMan.GetComponent<HardpointIntegrityComponent>(vehicle).Integrity != hullIntegrity)
            {
                Fail("the shot damaged the vehicle that fired it.");
            }
        });

        if (fixedPoint)
        {
            // The rounds fly to the aimed point and stop or go off there.
            await RunSeconds(4f);
            await Server.WaitAssertion(() =>
            {
                var wanted = Vector2.Distance(aim, muzzle);
                if (fixedComp!.MaxFixedRange is { } gunCap)
                    wanted = Math.Min(wanted, gunCap);
                if (roundCap != null)
                    wanted = Math.Min(wanted, roundCap.MaxFixedRange);

                foreach (var shot in shots)
                {
                    if (!probe.Stops.TryGetValue(shot, out var stoppedAt))
                    {
                        Fail("a round never reached the end of its fixed distance.");
                        continue;
                    }

                    var flown = Vector2.Distance(stoppedAt, muzzle);
                    var tolerance = StopTolerance + gunComp.ProjectileSpeedModified * (float) STiming.TickPeriod.TotalSeconds;
                    TestContext.Out.WriteLine($"{gunProto.ID}: a round stopped {flown:0.00} tiles out, aimed {wanted:0.00}");
                    if (Math.Abs(flown - wanted) > tolerance)
                        Fail($"a round stopped {flown:0.0} tiles out, not about {wanted:0.0}.");

                    // A round that lies where it stopped is spent, so a mob walking onto it is not hit by it.
                    if (!SEntMan.Deleted(shot) && !SEntMan.GetComponent<ProjectileComponent>(shot).ProjectileSpent)
                        Fail("a round that came to rest is still a live projectile.");

                    if (triggersAtStop && !SEntMan.Deleted(shot))
                        Fail("a round that goes off at the aimed point is still there.");
                }
            });
        }

        await Server.WaitPost(() =>
        {
            foreach (var shot in Projectiles())
            {
                SEntMan.DeleteEntity(shot);
            }
        });

        // The magazine in the reserve comes up when the ready one is spent.
        if (hardpointAmmoProto && hardpointAmmoComp!.MaxStoredMagazines > 1)
        {
            var magazineSize = 0;
            await Server.WaitAssertion(() =>
            {
                var hardpointAmmo = SEntMan.GetComponent<VehicleHardpointAmmoComponent>(gun);
                var ammoSystem = SEntMan.System<VehicleHardpointAmmoSystem>();
                magazineSize = ammoSystem.GetMagazineSize(hardpointAmmo, ammoComp);
                ammoSystem.SetStoredRounds((gun, hardpointAmmo), magazineSize, magazineSize);
                guns.SetBallisticUnspawned((gun, ammoComp), 1);
                ResetGun(gunComp);

                guns.ShootRequested(netGun, netAim, null, null, ServerSession);
                var reloaded = Projectiles();
                if (reloaded.Count != expectedPellets)
                    Fail($"the last round of a magazine put {reloaded.Count} rounds in the air, not {expectedPellets}.");

                if (ammoComp.Count != magazineSize)
                    Fail($"after the last round the gun holds {ammoComp.Count}, not the next magazine of {magazineSize}.");

                if (hardpointAmmo.StoredRoundSlots.Any(r => r != 0))
                    Fail($"the reserve still holds {string.Join(",", hardpointAmmo.StoredRoundSlots)} after it was chambered.");

                foreach (var shot in reloaded)
                {
                    SEntMan.RemoveComponent<ExplosiveComponent>(shot);
                    SEntMan.DeleteEntity(shot);
                }
            });
        }

        // An aim point that is on another map or is not a real point is refused before a round is spawned.
        await CheckBadAims(gunProto, mount, vehicle, gun, netGun, failures);

        if (fixedPoint)
        {
            await CheckFarAim(gunProto, mount, vehicle, gun, netGun, vehiclePos, arcOffset, failures);

            // With the blast taken out, a round still shows where it came to rest.
            if (arcOffset == Angle.Zero && _checkedFlights.Add(FlightProfile(data)))
            {
                await CheckObstacles(gunProto, mount, vehicle, gun, netGun, failures);
                await CheckAimsAtTheFace(gunProto, mount, vehicle, gun, netGun, failures);
                await CheckMovingShips(gunProto, mount, vehicle, gun, netGun, failures);
            }
        }

        // A gun fixed to the hull holds to its arc: a shot aimed behind the hull is refused.
        if (mount.ChildSlot == null &&
            SEntMan.TryGetComponent(gun, out GunFireArcComponent? hullArc) &&
            hullArc.Arc.Degrees < 359.0)
        {
            var behind = Vector2.Zero;
            await Server.WaitPost(() =>
                behind = vehiclePos + (Transform.GetWorldRotation(vehicle) + arcOffset + Angle.FromDegrees(180)).ToWorldVec() * AimDistance);

            var netBehind = SEntMan.GetNetCoordinates(Transform.ToCoordinates(new MapCoordinates(behind, MapId)));
            await Server.WaitAssertion(() =>
            {
                guns.SetBallisticUnspawned((gun, ammoComp), 2);
                ResetGun(gunComp);
                var before = ammoComp.Count;
                guns.ShootRequested(netGun, netBehind, null, null, ServerSession);
                var fired = Projectiles();
                if (fired.Count != 0 || ammoComp.Count != before)
                    Fail($"a shot aimed behind the hull, outside its {hullArc.Arc.Degrees:0} degree arc, was fired.");

                foreach (var shot in fired)
                {
                    SEntMan.DeleteEntity(shot);
                }
            });
        }

        await CleanUp(vehicle);
    }

    /// <summary>
    /// A point a modified client could send, far beyond any view, cannot make a fixed-distance round fly further than the gun, the
    /// round and the module allow, and does not throw.
    /// </summary>
    private async Task CheckFarAim(EntityPrototype gunProto, Mount mount, EntityUid vehicle, EntityUid gun, NetEntity netGun, Vector2 vehiclePos, Angle arcOffset, List<string> failures)
    {
        void Fail(string what) => failures.Add($"{gunProto.ID} on {mount.Vehicle}: {what}");

        var guns = SEntMan.System<SharedGunSystem>();
        var gunComp = SEntMan.GetComponent<GunComponent>(gun);
        var ammoComp = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun);

        foreach (var far in new[] { 1e14f, 1e20f })
        {
            await Server.WaitAssertion(() =>
            {
                var facing = Transform.GetWorldRotation(vehicle);
                var point = vehiclePos + (facing + arcOffset).ToWorldVec() * far;
                var netFar = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, point));

                guns.SetBallisticUnspawned((gun, ammoComp), 2);
                ResetGun(gunComp);

                try
                {
                    guns.ShootRequested(netGun, netFar, null, null, ServerSession);
                }
                catch (Exception e)
                {
                    Fail($"a shot aimed {far:0e0} tiles away threw {e.GetType().Name}.");
                    return;
                }

                var maxRange = ShootAtFixedPointSystem.AbsoluteMaxFixedRange;
                if (SEntMan.GetComponent<ShootAtFixedPointComponent>(gun).MaxFixedRange is { } gunCap && gunCap > 0f)
                    maxRange = Math.Min(maxRange, gunCap);

                foreach (var shot in Projectiles())
                {
                    var range = maxRange;
                    if (SEntMan.TryGetComponent(shot, out ProjectileMaxFixedRangeComponent? roundCap) && roundCap.MaxFixedRange > 0f)
                        range = Math.Min(range, roundCap.MaxFixedRange);

                    // A round that lost its fixed distance has no stop and no range cap at all, unless it is on its way out: a turret holds
                    // the shot to its barrel by the aimed distance, which overflows for a point this far, and the round has no course.
                    if (!SEntMan.TryGetComponent(shot, out ProjectileFixedDistanceComponent? fixedDistance))
                    {
                        if (!SEntMan.IsQueuedForDeletion(shot))
                            Fail($"a shot aimed {far:0e0} tiles away left a round with no fixed distance.");

                        continue;
                    }

                    var flightTime = (fixedDistance.FlyEndTime - STiming.CurTime).TotalSeconds;
                    var allowed = range / gunComp.ProjectileSpeedModified + 0.05;
                    if (flightTime > allowed)
                        Fail($"a shot aimed {far:0e0} tiles away was given {flightTime:0.0} seconds of flight, not at most {allowed:0.0}.");
                }

                foreach (var shot in Projectiles())
                {
                    SEntMan.RemoveComponent<ExplosiveComponent>(shot);
                    SEntMan.DeleteEntity(shot);
                }
            });
        }
    }

    private float DamageSpecifierTotal(EntityUid uid)
    {
        return SEntMan.TryGetComponent(uid, out DamageableComponent? damageable) ? (float) damageable.TotalDamage : 0f;
    }

    /// <summary>Lets the gunner out of the seat and takes the vehicle away, with what is left of the shots.</summary>
    private async Task CleanUp(EntityUid vehicle)
    {
        await Server.WaitPost(() =>
        {
            if (SEntMan.HasComponent<BuckleComponent>(SPlayer))
                SEntMan.System<SharedBuckleSystem>().TryUnbuckle(SPlayer, SPlayer, true);

            foreach (var shot in Projectiles())
            {
                SEntMan.DeleteEntity(shot);
            }
        });
        await RunTicks(5);

        await Server.WaitPost(() => SEntMan.DeleteEntity(vehicle));
        await RunTicks(5);
    }

    /// <summary>Both APC port guns fire from the port gun seat, out of the vehicle, from where each is mounted.</summary>
    [Test]
    public async Task BothApcPortGunsFireFromThePortGunSeat()
    {
        var failures = new List<string>();
        const string vehicleId = "VehicleAPC";

        await LayPlating();

        var vehicle = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            vehicle = SEntMan.SpawnEntity(vehicleId, new EntityCoordinates(MapData.Grid.Owner, VehicleSpot));
            Transform.SetLocalRotation(vehicle, Angle.Zero);
            var engine = SEntMan.GetComponent<WFVehicleEngineComponent>(vehicle);
            engine.Running = true;
            engine.Electrics = true;
        });
        await RunTicks(5);

        Assert.That(await Enter(vehicle, SPlayer), Is.True, "Could not get into the APC.");

        var seat = EntityUid.Invalid;
        var controllers = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            var interior = SEntMan.GetComponent<VehicleInteriorComponent>(vehicle);

            var seats = SEntMan.EntityQueryEnumerator<VehiclePortGunSeatComponent, TransformComponent>();
            while (seats.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID != interior.MapId)
                    continue;

                seat = uid;
                Transform.SetCoordinates(SPlayer, xform.Coordinates);
                break;
            }

            var ports = SEntMan.EntityQueryEnumerator<VehiclePortGunControllerComponent, TransformComponent>();
            while (ports.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == interior.MapId)
                    controllers.Add(uid);
            }
        });

        Assert.That(seat.IsValid(), Is.True, "The APC interior has no port gun seat.");
        Assert.That(controllers, Has.Count.EqualTo(2), "The APC interior should have a firing port for each port gun.");

        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, seat), Is.True, "Could not strap into the port gun seat.");
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true);
        });
        await RunTicks(10);

        var guns = SEntMan.System<SharedGunSystem>();
        var firedFrom = new HashSet<string>();

        foreach (var port in controllers)
        {
            var slotId = SEntMan.GetComponent<VehiclePortGunControllerComponent>(port).GunSlotId;
            void Fail(string what) => failures.Add($"{slotId}: {what}");

            await Server.WaitAssertion(() => SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port));
            await RunTicks(3);

            var gun = EntityUid.Invalid;
            var aim = Vector2.Zero;
            var vehiclePos = Vector2.Zero;
            var muzzle = Vector2.Zero;
            var magazineBefore = 0;
            var hullDamage = 0f;
            var shots = new List<EntityUid>();

            await Server.WaitAssertion(() =>
            {
                if (!guns.TryGetGun(SPlayer, out var found, out var gunComp))
                {
                    Fail("working the firing port did not give the gunner the gun.");
                    return;
                }

                gun = found;
                if (!SEntMan.HasComponent<VehiclePortGunComponent>(gun))
                    Fail("the gun is not a port gun.");

                var slots = SEntMan.System<ItemSlotsSystem>();
                if (!slots.TryGetSlot(vehicle, slotId, out var slot) || slot.Item != gun)
                    Fail("the gun is not the one in the port's slot.");

                if (!slots.TryGetSlot(gun, "gun_magazine", out var magazineSlot) || magazineSlot.Item is not { } magazine)
                {
                    Fail("the port gun has no magazine.");
                    return;
                }

                var magazineAmmo = SEntMan.GetComponent<BallisticAmmoProviderComponent>(magazine);
                magazineBefore = magazineAmmo.Count;

                vehiclePos = Transform.GetWorldPosition(vehicle);
                var arc = SEntMan.GetComponent<GunFireArcComponent>(gun);
                aim = vehiclePos + (Transform.GetWorldRotation(vehicle) + arc.AngleOffset).ToWorldVec() * AimDistance;
                hullDamage = DamageSpecifierTotal(vehicle);

                var aimCoordinates = Transform.ToCoordinates(new MapCoordinates(aim, MapId));
                var offset = SEntMan.GetComponent<GunMuzzleOffsetComponent>(gun);
                muzzle = SEntMan.System<GunMuzzleOffsetSystem>().TryGetMuzzleCoordinates(gun, offset, aimCoordinates, out var muzzleCoordinates, out _)
                    ? Transform.ToMapCoordinates(muzzleCoordinates).Position
                    : Transform.GetWorldPosition(gun);

                ResetGun(gunComp);
                guns.ShootRequested(SEntMan.GetNetEntity(gun), SEntMan.GetNetCoordinates(aimCoordinates), null, null, ServerSession);
                shots = Projectiles();

                if (shots.Count != 1)
                {
                    Fail($"one shot put {shots.Count} rounds in the air, not 1.");
                    return;
                }

                var shot = shots[0];
                if (Transform.GetMapId(shot) != MapId)
                    Fail("the round is not on the vehicle's map.");
                if (Vector2.Distance(Transform.GetWorldPosition(shot), muzzle) > 0.6f)
                    Fail($"the round started at {Transform.GetWorldPosition(shot)}, not at the muzzle {muzzle}.");
                if (SEntMan.GetComponent<ProjectileComponent>(shot).Weapon != gun)
                    Fail("the round does not name the port gun.");
                if (magazineAmmo.Count != magazineBefore - 1)
                    Fail($"the magazine went from {magazineBefore} to {magazineAmmo.Count}, not down by one.");

                firedFrom.Add(slotId);
            });

            await RunTicks(6);
            await Server.WaitAssertion(() =>
            {
                foreach (var shot in shots)
                {
                    if (!SEntMan.Deleted(shot) && Vector2.Distance(Transform.GetWorldPosition(shot), vehiclePos) < 1.5f)
                        Fail("the round is still on the hull after six ticks.");
                }

                if (DamageSpecifierTotal(vehicle) != hullDamage)
                    Fail("the shot damaged the vehicle that fired it.");

                foreach (var shot in Projectiles())
                {
                    SEntMan.DeleteEntity(shot);
                }

                // A port gun refuses an aim on the interior map and one that is not a number, as the other guns do.
                var portGun = SEntMan.GetComponent<GunComponent>(gun);
                var badAims = new[]
                {
                    new EntityCoordinates(SPlayer, new Vector2(5f, 5f)),
                    new EntityCoordinates(MapData.MapUid, new Vector2(float.NaN, 0f)),
                };

                foreach (var badAim in badAims)
                {
                    ResetGun(portGun);
                    guns.ShootRequested(SEntMan.GetNetEntity(gun), SEntMan.GetNetCoordinates(badAim), null, null, ServerSession);
                    if (Projectiles().Count != 0)
                        Fail("a shot at a point on another map or not a number was not refused.");

                    foreach (var shot in Projectiles())
                    {
                        SEntMan.DeleteEntity(shot);
                    }
                }

                // Let go of the gun for the next port.
                SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port);
            });
            await RunTicks(3);
        }

        Assert.That(firedFrom, Is.EquivalentTo(new[] { "port-gun-north", "port-gun-south" }), "Not both port guns fired.");

        await CleanUp(vehicle);

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>Every gun mounted on a spawned vehicle: in its own slots, and in the slots of the turret bodies sitting in them.</summary>
    private List<EntityUid> MountedGuns(EntityUid root)
    {
        var found = new List<EntityUid>();
        var pending = new Stack<EntityUid>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var uid = pending.Pop();
            if (!SEntMan.TryGetComponent(uid, out ItemSlotsComponent? slots))
                continue;

            foreach (var slot in slots.Slots.Values)
            {
                if (slot.Item is not { } item)
                    continue;

                if (SEntMan.HasComponent<HardpointItemComponent>(item) && SEntMan.HasComponent<GunComponent>(item))
                    found.Add(item);

                pending.Push(item);
            }
        }

        return found;
    }

    /// <summary>
    /// Every vehicle whose id marks it as armed spawns with guns mounted, and a gunner strapped into a weapons seat of its interior
    /// is offered at least one of them to pick. A vehicle that spawned unarmed, or armed with nothing a seat can fire, fails.
    /// </summary>
    [Test]
    public async Task EveryArmedVehicleSpawnsWithGunsAndAGunnerCanSelectOne()
    {
        var variants = new List<string>();
        await Server.WaitPost(() =>
        {
            variants = ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && p.ID.Contains("Armed") && IsRosterVehicle(p))
                .Select(p => p.ID)
                .OrderBy(id => id)
                .ToList();
        });

        Assert.That(variants, Has.Count.GreaterThanOrEqualTo(8), "The armed vehicles are missing.");

        var failures = new List<string>();
        await LayPlating();

        foreach (var id in variants)
        {
            void Fail(string what) => failures.Add($"{id}: {what}");

            var vehicle = EntityUid.Invalid;
            var mounted = new List<EntityUid>();
            await Server.WaitPost(() =>
            {
                vehicle = SEntMan.SpawnEntity(id, new EntityCoordinates(MapData.Grid.Owner, VehicleSpot));
                Transform.SetLocalRotation(vehicle, Angle.Zero);
                var engine = SEntMan.GetComponent<WFVehicleEngineComponent>(vehicle);
                engine.Running = true;
                engine.Electrics = true;
            });
            await RunTicks(5);
            await Server.WaitPost(() => mounted = MountedGuns(vehicle));

            if (mounted.Count == 0)
            {
                Fail("it spawned with no gun mounted.");
                await CleanUp(vehicle);
                continue;
            }

            if (!await Enter(vehicle, SPlayer))
            {
                Fail("could not get inside.");
                await CleanUp(vehicle);
                continue;
            }

            var seat = EntityUid.Invalid;
            await Server.WaitPost(() =>
            {
                var interior = SEntMan.GetComponent<VehicleInteriorComponent>(vehicle);
                var types = mounted.Select(gun => SEntMan.GetComponent<HardpointItemComponent>(gun).HardpointType).ToList();
                var query = SEntMan.EntityQueryEnumerator<VehicleWeaponsSeatComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var seatComp, out var xform))
                {
                    if (xform.MapID != interior.MapId)
                        continue;

                    var allowed = seatComp.AllowedHardpointTypes;
                    if (allowed.Count != 0 && !types.Any(type => allowed.Contains(type)))
                        continue;

                    seat = uid;
                    Transform.SetCoordinates(SPlayer, xform.Coordinates);
                    break;
                }
            });

            if (!seat.IsValid())
            {
                Fail("its interior has no weapons seat that takes a gun it carries.");
                await CleanUp(vehicle);
                continue;
            }

            await RunTicks(3);
            await Server.WaitAssertion(() =>
            {
                if (!SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, seat))
                    Fail("could not strap into the weapons seat.");
            });
            await RunTicks(10);

            await Server.WaitAssertion(() =>
            {
                var selectable = 0;
                if (SEntMan.TryGetComponent(SPlayer, out ActionsComponent? actions))
                {
                    foreach (var candidate in actions.Actions)
                    {
                        if (SEntMan.GetComponent<MetaDataComponent>(candidate).EntityPrototype?.ID == SelectAction &&
                            SEntMan.TryGetComponent(candidate, out VehicleHardpointActionComponent? hardpointAction) &&
                            hardpointAction.MountedWeapon is { } weapon &&
                            mounted.Contains(weapon))
                        {
                            selectable++;
                        }
                    }
                }

                if (selectable == 0)
                    Fail($"a gunner in its weapons seat can pick none of the {mounted.Count} guns it carries.");
            });

            await CleanUp(vehicle);
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// The LTB cannon shell goes off on a hard thing it meets, a wall or a mob, and flies past the non-hard fly-by sensor every
    /// other bullet carries without going off.
    /// </summary>
    [Test]
    public async Task TheCannonShellGoesOffOnHardThingsAndFliesPastNonHardSensors()
    {
        const string shellId = "RMCProjectileLTBCannon";
        await LayPlating();

        EntityUid SpawnShell(Vector2 position)
        {
            var shell = SEntMan.SpawnEntity(shellId, new EntityCoordinates(MapData.Grid.Owner, position));

            // The blast is data, and a real one would break the plating and play its sound to the client; the trigger is what is read.
            SEntMan.RemoveComponent<ExplodeOnTriggerComponent>(shell);
            return shell;
        }

        var probe = Server.System<CollideTriggerProbeSystem>();
        bool WentOff(EntityUid shell) => probe.Triggered.Contains(shell);

        // Past a sensor: another bullet crosses the resting shell, its fly-by circle over the shell's whole body.
        var restingShell = EntityUid.Invalid;
        var other = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            restingShell = SpawnShell(new Vector2(8.5f, 10.5f));
            other = SEntMan.SpawnEntity("WFFlybyTestBullet", new EntityCoordinates(MapData.Grid.Owner, new Vector2(8.5f, 14.5f)));
            SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(other, new Vector2(0f, -12f));
        });

        var crossed = false;
        for (var i = 0; i < 120 && !crossed; i++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => crossed = SEntMan.Deleted(other) || Transform.GetWorldPosition(other).Y < 7f);
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(crossed, Is.True, "The other bullet never crossed the shell.");
            Assert.That(WentOff(restingShell), Is.False, "The shell went off on another bullet's non-hard fly-by sensor.");
            SEntMan.DeleteEntity(restingShell);
            if (!SEntMan.Deleted(other))
                SEntMan.DeleteEntity(other);
        });

        // On a wall.
        var wallShell = EntityUid.Invalid;
        var wall = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            wall = SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(MapData.Grid.Owner, new Vector2(8.5f, 5.5f)));
            wallShell = SpawnShell(new Vector2(8.5f, 10.5f));
            SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(wallShell, new Vector2(0f, -20f));
        });
        await RunSeconds(1.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(WentOff(wallShell), Is.True, "The shell flew into a wall and did not go off.");
            if (!SEntMan.Deleted(wallShell))
                SEntMan.DeleteEntity(wallShell);
            SEntMan.DeleteEntity(wall);
        });

        // On a mob.
        var mobShell = EntityUid.Invalid;
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(MapData.Grid.Owner, new Vector2(8.5f, 5.5f)));
            mobShell = SpawnShell(new Vector2(8.5f, 10.5f));
            SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(mobShell, new Vector2(0f, -20f));
        });
        await RunSeconds(1.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(WentOff(mobShell), Is.True, "The shell flew into a mob and did not go off.");
            if (!SEntMan.Deleted(mobShell))
                SEntMan.DeleteEntity(mobShell);
            SEntMan.DeleteEntity(mob);
        });
    }
}
