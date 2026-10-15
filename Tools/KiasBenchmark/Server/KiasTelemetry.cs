using System.Reflection;
using System.Numerics;
using System.Linq;
using System.Collections;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared.Power.Components;
using Content.Server.Benchmark;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using System.Security.Cryptography;
using System.Text;

namespace Content.Server.Benchmark;

public sealed class KiasLabTelemetrySystem : EntitySystem
{
    private readonly Dictionary<EntityUid, KiasGraphMachine> _pendingMachines = new();
    private bool _requiresDockingFixture;
    private readonly List<EntityUid> _auxDockingSensors = new();
    public override void Initialize()
    {
        var scenario = Environment.GetEnvironmentVariable("NATIVE_LAB_SCENARIO");
        _requiresDockingFixture = scenario != null && System.IO.File.ReadAllText(scenario).Contains("\"dock.dock\"", StringComparison.Ordinal);
        NativeLab.Telemetry = Capture;
        NativeLab.RegisterCrew = RegisterCrew;
        NativeLab.KiasDriver = ApplyNative;
        NativeLab.RoomProbe = ObserveRoom;
        NativeLab.WorldProbe = ObserveLife;
        EntityManager.System<KiasControllerIoSystem>().Emitted += ObserveScannerSignal;
        EntityManager.System<KiasControllerIoSystem>().CommandDispatched += ObserveFanout;
        EntityManager.System<KiasDefenceSystem>().ProjectileIntercepted += ObserveInterception;
        EntityManager.System<KiasSystem>().MeasureUpdates = Environment.GetEnvironmentVariable("NATIVE_LAB_PHASE_COUNTERS") != "0";
        if (Environment.GetEnvironmentVariable("NATIVE_LAB_PROFILE") == "1")
        {
            NativeLab.ProfilePhaseNames = Enum.GetNames<KiasPhase>();
            KiasPhaseMeasurement.Observed = NativeLab.ProfilePhaseObserved;
            NativeLab.ProfilePhaseTotals = CapturePhaseTotals;
        }
    }

    private sealed record CableSnapshot(EntityUid Original, string Prototype, EntityCoordinates Position, Angle Rotation, ushort Layer);
    private sealed record DataCutSnapshot(List<CableSnapshot> Cables, Dictionary<Vector2i, Tile> Floors, string FloorSha256);
    private readonly Dictionary<EntityUid, DataCutSnapshot> _cutCables = new();

    private Dictionary<Vector2i, Tile> DataFloors(EntityUid grid)
        => EntityManager.System<SharedMapSystem>().GetAllTiles(grid, Comp<MapGridComponent>(grid))
            .ToDictionary(tile => tile.GridIndices, tile => tile.Tile);

    private static string FloorHash(Dictionary<Vector2i, Tile> floors)
    {
        var rows = floors.OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y).Select(pair => new {
            x = pair.Key.X, y = pair.Key.Y, type = pair.Value.TypeId, flags = pair.Value.Flags,
            variant = pair.Value.Variant, rotation = pair.Value.RotationMirroring });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows)))).ToLowerInvariant();
    }

    private object VerifyDataSnapshot(EntityUid grid, bool restored)
    {
        var saved = _cutCables[grid];
        var actualFloors = DataFloors(grid);
        var floorHash = FloorHash(actualFloors);
        if (actualFloors.Count != saved.Floors.Count || floorHash != saved.FloorSha256
            || saved.Floors.Any(pair => !actualFloors.TryGetValue(pair.Key, out var tile) || tile != pair.Value))
            throw new InvalidOperationException("DATA lifecycle changed floor tiles.");
        if (saved.Cables.Any(cable => EntityManager.EntityExists(cable.Original)))
            throw new InvalidOperationException("DATA teardown retained old cable entities.");
        if (OnGrid<MetaDataComponent>(grid).Any(uid => MetaData(uid).EntityPrototype?.ID == "KiasDataCableStack1"))
            throw new InvalidOperationException("DATA laboratory spawned loose cable coils.");
        var cables = OnGrid<KiasDataCableComponent>(grid);
        if (cables.Length != (restored ? saved.Cables.Count : 0))
            throw new InvalidOperationException("DATA cable count differs from the expected lifecycle.");
        var remaining = new HashSet<EntityUid>(cables);
        if (restored)
            foreach (var cable in saved.Cables)
            {
                var matches = remaining.Where(uid => MetaData(uid).EntityPrototype?.ID == cable.Prototype
                    && Transform(uid).Coordinates == cable.Position && Transform(uid).LocalRotation == cable.Rotation).ToArray();
                if (matches.Length == 0) throw new InvalidOperationException("DATA cable prototype, coordinates or orientation changed.");
                var uid = matches[0];
                if (!Transform(uid).Anchored || Transform(uid).GridUid != grid
                    || (TryComp<VisibilityComponent>(uid, out var visibility) ? visibility.Layer : (ushort) 1) != cable.Layer
                    || TryComp<PhysicsComponent>(uid, out var body) && body.BodyType != BodyType.Static)
                    throw new InvalidOperationException("Restored DATA cable is not anchored static underfloor wiring.");
                remaining.Remove(uid);
            }
        var proof = new { actualRemovedCables = saved.Cables.Count, actualRestoredCables = cables.Length,
            floorBeforeSha256 = saved.FloorSha256, floorAfterSha256 = floorHash, floorTileCount = saved.Floors.Count,
            floorTilesUnchanged = true, oldCableEntitiesDeleted = true, looseCableStacks = 0,
            dynamicCableBodies = 0, exactCableInventoryRestored = restored,
            baselineDuplicateCableEntries = saved.Cables.Count - saved.Cables.Select(cable => (cable.Prototype, cable.Position, cable.Rotation)).Distinct().Count(),
            addedDuplicateCableEntries = 0 };
        NativeLab.SaveDiagnostic($"data-{grid}-{(restored ? "restored" : "removed")}-{IoCManager.Resolve<IGameTiming>().CurTick.Value}.json", proof);
        return proof;
    }
    private readonly Dictionary<(EntityUid Grid, string Input), (uint Revision, bool Pending)> _roomRevisions = new();
    private readonly Dictionary<(EntityUid Device, string Port), (uint Tick, KiasGraphValue Value)> _scannerSignals = new();
    private readonly Dictionary<EntityUid, (uint Tick, Dictionary<EntityUid, int> Counts)> _lifeWatches = new();
    private readonly Dictionary<EntityUid, (EntityUid? Core, string Serial)> _crewTripTokens = new();
    private sealed class FanoutFixture
    {
        public EntityUid Card, Rack, Scanner;
        public KiasControllerProgram Original = default!;
        public uint OriginalRevision;
        public EntityUid[] Targets = Array.Empty<EntityUid>();
        public readonly HashSet<EntityUid> Dispatched = new();
        public readonly Dictionary<EntityUid, EntityUid?> BeforeTones = new();
        public bool Stimulated, Restoring;
    }
    private readonly Dictionary<EntityUid, FanoutFixture> _fanouts = new();
    private sealed record SuppressionFixture(EntityUid Device, EntityUid Cartridge, HashSet<EntityUid> ExistingFoam)
    {
        public bool Activated;
    }
    private readonly Dictionary<EntityUid, SuppressionFixture> _suppressions = new();

    private object? ExerciseSuppression(EntityUid grid, string type)
    {
        if (type == "fire.prepare")
        {
            var device = OnGrid<KiasSuppressionComponent>(grid)
                .Single(uid => Vector2.DistanceSquared(Transform(uid).LocalPosition, new Vector2(7.5f, 7.5f)) < .01f);
            var containers = EntityManager.System<Robust.Shared.Containers.SharedContainerSystem>();
            if (!containers.TryGetContainer(device, "kias-cartridge", out var container))
                throw new InvalidOperationException("Native suppression cartridge slot is missing.");
            var cartridge = container.ContainedEntities.FirstOrDefault();
            if (!cartridge.Valid)
            {
                cartridge = Spawn("KiasSuppressionCartridge", Transform(device).Coordinates);
                if (!EntityManager.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>().TryInsert(device, "kias-cartridge", cartridge, null))
                    throw new InvalidOperationException("Native suppression cartridge insertion failed.");
            }
            _suppressions.Add(grid, new(device, cartridge, OnGrid<MetaDataComponent>(grid)
                .Where(uid => Comp<MetaDataComponent>(uid).EntityPrototype?.ID == "Foam").ToHashSet()));
            return new { nativeCartridgeLoaded = true, device = device.ToString(), cartridge = cartridge.ToString() };
        }
        var fixture = _suppressions[grid];
        if (type == "fire.cleanup") { _suppressions.Remove(grid); return new { restored = true }; }
        if (type == "fire.suppress")
        {
            var kias = EntityManager.System<KiasSystem>();
            if (!kias.IsOnline(fixture.Device) || !kias.HasRole(grid, KiasDeviceRole.Atmosphere)) return null;
            var signal = new Content.Shared.DeviceLinking.Events.SignalReceivedEvent("KiasSuppress");
            EntityManager.EventBus.RaiseLocalEvent(fixture.Device, ref signal);
            if (!EntityManager.IsQueuedForDeletion(fixture.Cartridge))
                throw new InvalidOperationException("Native suppression signal did not consume its real cartridge.");
            fixture.Activated = true;
            return new { nativeSuppressionSignal = true };
        }
        if (!fixture.Activated || EntityManager.EntityExists(fixture.Cartridge)) return null;
        var foam = OnGrid<MetaDataComponent>(grid).Where(uid => Comp<MetaDataComponent>(uid).EntityPrototype?.ID == "Foam"
            && !fixture.ExistingFoam.Contains(uid)).ToArray();
        return foam.Length == 0 ? null : new { consumedCartridge = fixture.Cartridge.ToString(),
            nativeWaterFoam = foam.Select(uid => uid.ToString()).ToArray() };
    }
    private void ObserveInterception(EntityUid grid, EntityUid target, EntityUid interceptor)
    {
        var transforms = EntityManager.System<SharedTransformSystem>();
        EntityManager.System<NativeGameObserverSystem>().InterceptedProjectiles[target] =
            (IoCManager.Resolve<IGameTiming>().CurTick.Value, grid, interceptor,
                Vector2.Distance(transforms.GetWorldPosition(grid), transforms.GetWorldPosition(target)));
    }

    private void ObserveFanout(EntityUid grid, EntityUid card, EntityUid target, string profile, string port)
    {
        if (!_fanouts.TryGetValue(grid, out var fixture) || fixture.Restoring || fixture.Card != card || profile != "Speaker" || port != "Alarm") return;
        if (!fixture.Targets.Contains(target) || !fixture.Dispatched.Add(target))
            throw new InvalidOperationException("Native ALL fanout dispatched a duplicate or foreign target.");
    }

    private object? ExerciseFanout(EntityUid grid, bool start)
    {
        var runtime = EntityManager.System<KiasControllerRuntimeSystem>();
        var kias = EntityManager.System<KiasSystem>();
        if (start)
        {
            var card = OnGrid<KiasControllerCardComponent>(grid).First(runtime.Running);
            var component = Comp<KiasControllerCardComponent>(card);
            var fixture = new FanoutFixture { Card = card, Rack = Transform(card).ParentUid,
                Scanner = OnGrid<KiasRoomScannerComponent>(grid).First(kias.IsOnline),
                Original = component.Program, OriginalRevision = component.Revision,
                Targets = OnGrid<KiasSpeakerComponent>(grid).Where(kias.IsOnline).ToArray() };
            if (fixture.Targets.Length < 2) throw new InvalidOperationException("Native ALL fanout requires multiple online speakers.");
            foreach (var target in fixture.Targets) fixture.BeforeTones[target] = Comp<KiasSpeakerComponent>(target).Tone;
            var program = new KiasControllerProgram { Name = "Native ANY to ALL fixture" };
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "Automation" });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.All, Profile = "Speaker" });
            program.Nodes.Add(new() { Id = 3, Kind = KiasNodeKind.StringConstant, Config = new() { Text = "native-fanout:" + grid } });
            program.Wires.Add(new() { FromNode = 1, FromPort = "PowerLost", ToNode = 2, ToPort = "Alarm" });
            program.Wires.Add(new() { FromNode = 3, FromPort = "Value", ToNode = 2, ToPort = "Message" });
            program.Wires.Add(new() { FromNode = 3, FromPort = "Value", ToNode = 2, ToPort = "Key" });
            component.Program = program;
            component.Revision++;
            _fanouts.Add(grid, fixture);
            runtime.Reconcile(fixture.Rack);
            return null;
        }
        var current = _fanouts[grid];
        if (!runtime.Running(current.Card) || kias.TopologyPending(grid)) return null;
        if (!current.Stimulated)
        {
            current.Stimulated = true;
            EntityManager.System<SharedPowerReceiverSystem>().SetPowerDisabled(current.Scanner, true);
            return null;
        }
        if (!current.Restoring)
        {
            if (current.Dispatched.Count != current.Targets.Length || current.Targets.Any(uid =>
                    Comp<KiasSpeakerComponent>(uid).Tone is not { } tone || !EntityManager.EntityExists(tone)
                    || tone == current.BeforeTones[uid])) return null;
            current.Restoring = true;
            EntityManager.System<SharedPowerReceiverSystem>().SetPowerDisabled(current.Scanner, false);
            var card = Comp<KiasControllerCardComponent>(current.Card);
            card.Program = current.Original;
            card.Revision++;
            runtime.Reconcile(current.Rack);
            return null;
        }
        if (!kias.IsOnline(current.Scanner) || Comp<KiasControllerCardComponent>(current.Card).Program != current.Original) return null;
        _fanouts.Remove(grid);
        return new { nativePowerLostInput = true, actualAnyToAllGraph = true,
            nativeSoundEntitiesVerified = true, originalPhysicalCardRestored = true,
            originalRevision = current.OriginalRevision, revision = Comp<KiasControllerCardComponent>(current.Card).Revision,
            targets = current.Dispatched.Select(uid => uid.ToString()).ToArray() };
    }

    private (EntityUid? Core, string Serial) OwnedToken(EntityUid grid, EntityUid target)
    {
        var core = Comp<KiasGridComponent>(grid).Core;
        var servers = OnGrid<KiasCrewServerComponent>(grid);
        var query = EntityQueryEnumerator<KiasTransponderComponent>();
        while (query.MoveNext(out var uid, out var token))
        {
            if (token.Core != core || !servers.Any(server => Comp<KiasCrewServerComponent>(server).Registered.Contains(token.Serial))) continue;
            var carrier = uid;
            for (var depth = 0; depth < 16 && carrier.Valid && !TerminatingOrDeleted(carrier); depth++)
            {
                if (carrier == target) return (token.Core, token.Serial);
                carrier = Transform(carrier).ParentUid;
            }
        }
        throw new InvalidOperationException("Native crew lost its owned registered transponder.");
    }

    private void ObserveScannerSignal(EntityUid device, string profile, string port, KiasGraphValue value)
    {
        if (profile is "RoomScanner" or "Horizon" or "NavigationComms" or "CollisionMonitor" or "WeaponFlashDetector" or "IntegrityMonitor" or "DockingSensor")
            _scannerSignals[(device, port)] = (IoCManager.Resolve<IGameTiming>().CurTick.Value, value);
    }

    private object? ObserveLife(EntityUid grid, EntityUid target, string type, bool start)
    {
        var crew = EntityManager.System<KiasCrewSystem>();
        if (start)
        {
            if (type == "crew.depart") _crewTripTokens.Add(target, OwnedToken(grid, target));
            _lifeWatches.Add(target, (IoCManager.Resolve<IGameTiming>().CurTick.Value,
                OnGrid<KiasRoomScannerComponent>(grid).ToDictionary(uid => uid, uid => Comp<KiasRoomScannerComponent>(uid).Entities)));
            return null;
        }
        var watch = _lifeWatches[target];
        if (type is "dock.dock" or "dock.undock")
        {
            var docked = type == "dock.dock";
            var port = docked ? "Docked" : "Undocked";
            var detectors = OnGrid<KiasDockingSensorComponent>(grid).Where(uid =>
                _scannerSignals.TryGetValue((uid, port), out var signal) && signal.Tick >= watch.Tick).ToArray();
            if (detectors.Length == 0) return null;
            _lifeWatches.Remove(target);
            return new { actualDockingSignal = true, docked, detectors = detectors.Select(uid => uid.ToString()).ToArray() };
        }
        if (type is "radiation.start" or "hull.damage")
        {
            var profile = type == "radiation.start" ? "RoomScanner" : "IntegrityMonitor";
            var port = type == "radiation.start" ? "Radiation" : "Damage";
            var detectors = EntityManager.System<KiasControllerIoSystem>().Devices(grid, profile)
                .Where(uid => _scannerSignals.TryGetValue((uid, port), out var signal) && signal.Tick >= watch.Tick
                    && (type != "hull.damage" || _scannerSignals.TryGetValue((uid, "Structure"), out var structure)
                        && structure.Tick == signal.Tick && structure.Value.Entity == target)).ToArray();
            if (detectors.Length == 0) return null;
            _lifeWatches.Remove(target);
            return new { actualNativeSensorSignal = true, profile, port, detectors = detectors.Select(uid => uid.ToString()).ToArray() };
        }
        if (type == "pdc.weapon_flash")
        {
            var sourceGrid = Transform(target).GridUid
                ?? throw new InvalidOperationException("Native gun source lost its foreign grid.");
            var disposition = EntityManager.System<KiasNavigationSystem>().Classify(grid, sourceGrid);
            var detectors = OnGrid<KiasWeaponFlashComponent>(grid).Where(uid =>
                _scannerSignals.TryGetValue((uid, "Triggered"), out var trigger) && trigger.Tick >= watch.Tick
                && _scannerSignals.TryGetValue((uid, "Source"), out var source) && source.Tick == trigger.Tick
                && source.Value.Entity == target
                && _scannerSignals.TryGetValue((uid, "Disposition"), out var iff) && iff.Tick == trigger.Tick
                && iff.Value.Enum == (int) disposition).ToArray();
            if (detectors.Length == 0) return null;
            _lifeWatches.Remove(target);
            return new { nativeWeaponFlashObserved = true, source = target.ToString(),
                disposition = disposition.ToString(), detectors = detectors.Select(uid => uid.ToString()).ToArray() };
        }
        if (type is "collision.low" or "collision.high")
        {
            var detected = OnGrid<KiasCollisionMonitorComponent>(grid).Any(uid =>
                _scannerSignals.TryGetValue((uid, "Collision"), out var signal) && signal.Tick >= watch.Tick
                && _scannerSignals.TryGetValue((uid, "OtherGrid"), out var other) && other.Value.Entity == target);
            if (type == "collision.low" && detected)
                throw new InvalidOperationException("Native below-threshold grid contact emitted a collision alarm.");
            if (type == "collision.high" && !detected) return null;
            _lifeWatches.Remove(target);
            return new { actualCollisionMonitorSignal = detected, belowThresholdControl = type == "collision.low" };
        }
        if (type == "navigation.arrive")
        {
            var devices = EntityManager.System<KiasControllerIoSystem>().Devices(grid, "NavigationComms");
            if (!devices.Any(uid => _scannerSignals.TryGetValue((uid, "Arrival"), out var signal)
                    && signal.Tick >= watch.Tick)) return null;
            _lifeWatches.Remove(target);
            return new { actualNavigationArrivalSignal = true };
        }
        if (type is "ftl.visitor_in" or "ftl.visitor_out")
        {
            var contacts = OnGrid<KiasHorizonComponent>(grid).Where(uid =>
                _scannerSignals.TryGetValue((uid, "ContactEntity"), out var contact)
                && contact.Tick >= watch.Tick && contact.Value.Entity == target).ToArray();
            if (type == "ftl.visitor_in" && contacts.Length == 0) return null;
            if (type == "ftl.visitor_out" && contacts.Length != 0)
                throw new InvalidOperationException("Out-of-range FTL departure incorrectly produced an own-grid Horizon contact.");
            var disposition = EntityManager.System<KiasNavigationSystem>().Classify(grid, target);
            _lifeWatches.Remove(target);
            return new { actualHorizonContact = contacts.Length > 0, disposition = disposition.ToString(),
                horizonDevices = contacts.Select(uid => uid.ToString()).ToArray() };
        }
        if (type == "anomaly.progress")
        {
            var detectors = crew.ScannersCovering(grid, target, KiasScannerModules.Spectral).ToArray();
            if (!detectors.Any(uid => _scannerSignals.TryGetValue((uid, "AnomalyGrowth"), out var signal)
                    && signal.Tick >= watch.Tick)) return null;
            _lifeWatches.Remove(target);
            return new { actualAnomalyGrowthSignal = true, spectralScanners = detectors.Select(uid => uid.ToString()).ToArray() };
        }
        if (type is "crew.depart" or "crew.return")
        {
            var returned = type == "crew.return";
            var token = OwnedToken(grid, target);
            if (token != _crewTripTokens[target]) throw new InvalidOperationException("Native crew token identity changed during a grid transfer.");
            if (crew.HasCoverage(grid, target, KiasScannerModules.Biometric) != returned
                || crew.CrewUnavailable(grid) == returned) return null;
            _lifeWatches.Remove(target);
            if (returned) _crewTripTokens.Remove(target);
            return new { registrationPreserved = true, biometricCoverage = returned, unavailable = crew.CrewUnavailable(grid) };
        }
        if (type.EndsWith("clear", StringComparison.Ordinal))
        {
            if (watch.Counts.Any(pair => !TerminatingOrDeleted(pair.Key)
                    && Comp<KiasRoomScannerComponent>(pair.Key).Entities != pair.Value)) return null;
            _lifeWatches.Remove(target);
            return new { scannerCountsRestored = true };
        }
        var fauna = type == "fauna.enter";
        var module = fauna || type == "crew.armed_threat" ? KiasScannerModules.Threat : KiasScannerModules.Motion;
        var scanners = crew.ScannersCovering(grid, target, module).ToArray();
        if (scanners.Length == 0) return null;
        if (!fauna && (!crew.IsKiasTrackedEntity(target) || crew.IsRegisteredPerson(grid, target))) return null;
        if (fauna || type == "crew.armed_threat")
        {
            var port = fauna ? "FaunaThreat" : "Threat";
            if (!scanners.Any(uid => _scannerSignals.TryGetValue((uid, port), out var signal)
                    && signal.Tick >= watch.Tick
                    && (fauna || _scannerSignals.TryGetValue((uid, "Person"), out var person)
                        && person.Tick == signal.Tick && person.Value.Entity == target))) return null;
        }
        else if (!scanners.Any(uid => Comp<KiasRoomScannerComponent>(uid).Entities > watch.Counts[uid])) return null;
        return new { actualScannerObservation = true, tracked = crew.IsKiasTrackedEntity(target),
            registered = crew.IsRegisteredPerson(grid, target), scanners = scanners.Select(uid => uid.ToString()).ToArray(),
            port = fauna ? "FaunaThreat" : type == "crew.armed_threat" ? "Threat+Person" : "Entities" };
    }

    private object? ObserveRoom(EntityUid grid, string inputId, string type, bool start)
    {
        var topology = EntityManager.System<KiasRoomTopologySystem>();
        if (!topology.Grids.TryGetValue(grid, out var cache))
            throw new InvalidOperationException("Native room fixture has no topology cache.");
        if (start)
        {
            if (!_roomRevisions.TryAdd((grid, inputId), (cache.Revision, cache.Pending)))
                throw new InvalidOperationException("Duplicate native room input.");
            return null;
        }
        var before = _roomRevisions[(grid, inputId)];
        if (cache.Pending) return null;
        var scanners = OnGrid<KiasRoomScannerComponent>(grid).Where(uid =>
            Vector2.DistanceSquared(Transform(uid).LocalPosition, new Vector2(-10.5f, 2.5f)) < .01f).ToArray();
        if (scanners.Length != 1) throw new InvalidOperationException("Briar (-11,2) scanner fixture changed.");
        var usable = topology.TryGetScannerRoom(scanners[0], out _, out var status);
        var intentionalBreach = EntityManager.System<SharedMapSystem>()
            .GetTileRef(grid, Comp<MapGridComponent>(grid), new Vector2i(-11, 1)).Tile.IsEmpty;
        if (!usable && type != "geometry.breach" && !(type.StartsWith("door.", StringComparison.Ordinal) && intentionalBreach)) return null;
        if (status == KiasRoomStatus.ExteriorSector)
            throw new InvalidOperationException("Interior scanner incorrectly became an exterior sector.");
        if (type.StartsWith("geometry.", StringComparison.Ordinal) && cache.Revision <= before.Revision) return null;
        if (type == "geometry.repair" && status != KiasRoomStatus.Ok) return null;
        var advanced = OnGrid<KiasRoomScannerComponent>(grid).Single(uid =>
            Vector2.DistanceSquared(Transform(uid).LocalPosition, new Vector2(-8.5f, 2.5f)) < .01f);
        if (!topology.TryGetScannerRoom(advanced, out _, out var advancedStatus)) return null;
        if (advancedStatus == KiasRoomStatus.ExteriorSector)
            throw new InvalidOperationException("A breached interior advanced scanner switched to an exterior sector.");
        _roomRevisions.Remove((grid, inputId));
        return new { inputId, pendingAtDispatch = before.Pending, intentionalBreach,
            revisionBefore = before.Revision, revisionAfter = cache.Revision,
            scanner = scanners[0].ToString(), status = status.ToString(), usable,
            cells = topology.ScannerCells(scanners[0]).Count(), advancedScanner = advanced.ToString(),
            advancedStatus = advancedStatus.ToString(), advancedCells = topology.ScannerCells(advanced).Count() };
    }
    private readonly Dictionary<EntityUid, List<EntityUid>> _powerTargets = new();
    private readonly Dictionary<EntityUid, EntityUid[]> _onlineBefore = new();
    private readonly Dictionary<EntityUid, EntityUid[]> _collisionOnline = new();
    private readonly Dictionary<EntityUid, EntityUid[]> _collisionCards = new();
    private readonly Dictionary<EntityUid, uint> _collisionStableSince = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, (uint Revision, string Program)>> _savedCards = new();

    private EntityUid[] OnGrid<T>(EntityUid grid) where T : Component
    {
        var result = new List<EntityUid>();
        var query = EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform)) if (transform.GridUid == grid) result.Add(uid);
        return result.OrderBy(uid => uid.Id).ToArray();
    }

    private void RegisterCrew(EntityUid grid, EntityUid actor)
    {
        if (EntityManager.System<KiasCrewSystem>().IsRegisteredPerson(grid, actor)) return;
        var server = OnGrid<KiasCrewServerComponent>(grid).First(uid => EntityManager.System<KiasSystem>().IsOnline(uid));
        var coordinates = Transform(actor).Coordinates;
        var token = Spawn("KiasCrewTransponder", coordinates);
        if (!EntityManager.System<SharedHandsSystem>().TryPickupAnyHand(actor, token))
            throw new InvalidOperationException("Crew cannot hold registration transponder.");
        EntityManager.EventBus.RaiseLocalEvent(server, new InteractUsingEvent(actor, token, server, Transform(server).Coordinates));
        if (!EntityManager.System<InventorySystem>().TryGetSlotEntity(actor, "back", out var backpack)
            || backpack is not { } storage
            || !EntityManager.System<SharedStorageSystem>().Insert(storage, token, out _, user: actor, playSound: false)
            || !EntityManager.System<KiasCrewSystem>().IsRegisteredPerson(grid, actor))
            throw new InvalidOperationException("Native crew registration/storage failed.");
        if (_requiresDockingFixture && OnGrid<KiasDockingSensorComponent>(grid).Length == 0)
        {
            var sensor = Spawn("KiasDockingSensor", Transform(server).Coordinates);
            if (!Transform(sensor).Anchored && !EntityManager.System<SharedTransformSystem>().AnchorEntity(sensor))
                throw new InvalidOperationException("Native AUX_FIXTURE docking sensor could not anchor.");
            _auxDockingSensors.Add(sensor);
        }
    }

    private object? ApplyNative(EntityUid grid, string type, bool start)
    {
        if (type is "light.command_off" or "light.command_on")
        {
            var light = OnGrid<Content.Shared.Light.Components.PoweredLightComponent>(grid)
                .OrderBy(uid => Transform(uid).LocalPosition.X).ThenBy(uid => Transform(uid).LocalPosition.Y).First();
            if (!EntityManager.System<KiasSystem>().IsOnline(light) || !HasComp<KiasLightFixtureComponent>(light))
                throw new InvalidOperationException("Native lamp command requires an online integrated fixture.");
            var card = OnGrid<KiasControllerCardComponent>(grid).First();
            EntityManager.System<KiasControllerIoSystem>().Command(grid, card, light,
                new KiasControllerNode { Profile = "Lighting" }, type.EndsWith("_on", StringComparison.Ordinal) ? "On" : "Off",
                KiasGraphValue.Pulse, new KiasCommandInputs(Array.Empty<string>(), Array.Empty<KiasGraphValue>()));
            return new { light = light.ToString(), actualControllerCommandPath = true };
        }
        if (type is "fire.prepare" or "fire.suppress" or "fire.verify" or "fire.cleanup") return ExerciseSuppression(grid, type);
        if (type == "runtime.all_any") return ExerciseFanout(grid, start);
        var kias = EntityManager.System<KiasSystem>();
        var runtime = EntityManager.System<KiasControllerRuntimeSystem>();
        if (type == "collision.prepare")
        {
            _collisionOnline.Add(grid, OnGrid<KiasDeviceComponent>(grid).Where(kias.IsOnline).ToArray());
            var cards = OnGrid<KiasControllerCardComponent>(grid).Where(uid => Comp<KiasControllerCardComponent>(uid).Enabled).ToArray();
            if (cards.Length != 26 || !cards.All(runtime.Running))
                throw new InvalidOperationException("Native collision fixture must start with 26 running cards.");
            _collisionCards.Add(grid, cards);
            return null;
        }
        if (type == "collision.inspect")
        {
            return new
            {
                topologyPending = kias.TopologyPending(grid),
                gridPosition = Transform(grid).LocalPosition.ToString(),
                gridRotationRadians = Transform(grid).LocalRotation.Theta,
                roomGeometry = EntityManager.System<KiasRoomTopologySystem>().DebugSnapshot(grid),
                stableSince = _collisionStableSince.TryGetValue(grid, out var since) ? (uint?) since : null,
                unavailableDevices = _collisionOnline[grid].Where(uid => TerminatingOrDeleted(uid) || !kias.IsOnline(uid))
                    .Select(uid => TerminatingOrDeleted(uid)
                        ? (object) new { entity = uid.ToString(), deleted = true }
                        : new { entity = uid.ToString(), prototype = MetaData(uid).EntityPrototype?.ID,
                            status = Comp<KiasDeviceComponent>(uid).Status.ToString(),
                            anchored = Transform(uid).Anchored, parent = Transform(uid).ParentUid.ToString(),
                            grid = Transform(uid).GridUid?.ToString(), position = Transform(uid).LocalPosition.ToString(),
                            powered = TryComp<ApcPowerReceiverComponent>(uid, out var power) ? (bool?) power.Powered : null,
                            powerDisabled = power?.PowerDisabled, received = power?.PowerReceived, load = power?.Load }).ToArray(),
                unavailableCards = _collisionCards[grid].Where(uid => TerminatingOrDeleted(uid) || !runtime.Running(uid) || runtime.Fault(uid).Length != 0)
                    .Select(uid => new { entity = uid.ToString(), deleted = TerminatingOrDeleted(uid),
                        running = !TerminatingOrDeleted(uid) && runtime.Running(uid),
                        fault = TerminatingOrDeleted(uid) ? null : runtime.Fault(uid) }).ToArray()
            };
        }
        if (type == "collision.recovery")
        {
            if (kias.TopologyPending(grid) || !_collisionOnline[grid].All(uid => !TerminatingOrDeleted(uid) && kias.IsOnline(uid))
                || !_collisionCards[grid].All(uid => !TerminatingOrDeleted(uid) && runtime.Running(uid) && runtime.Fault(uid).Length == 0))
            {
                _collisionStableSince.Remove(grid);
                return null;
            }
            var tick = IoCManager.Resolve<IGameTiming>().CurTick.Value;
            _collisionStableSince.TryAdd(grid, tick);
            if (tick - _collisionStableSince[grid] < 180) return null;
            var restoredDevices = _collisionOnline[grid].Length;
            _collisionOnline.Remove(grid);
            _collisionCards.Remove(grid);
            _collisionStableSince.Remove(grid);
            return new { initiallyOnlineDevicesRecovered = restoredDevices, enabledCardsRunningWithoutFault = true,
                continuouslyStableRecoveryTicks = 180 };
        }
        if (type.StartsWith("pdc.", StringComparison.Ordinal))
        {
            EntityManager.System<Content.Server._Mono.FireControl.FireControlSystem>().ForceServerReconnectionOnGrid(grid);
            var weapons = OnGrid<KiasPdcWeaponComponent>(grid);
            var radars = OnGrid<KiasPdcRadarComponent>(grid);
            var servers = OnGrid<KiasDefenceComponent>(grid);
            if (!weapons.Any(kias.IsOnline) || !radars.Any(kias.IsOnline) || !servers.Any(kias.IsOnline))
                throw new InvalidOperationException("Native PDC fixture lacks online weapon, radar or defence server.");
            foreach (var server in servers) EntityManager.System<KiasDefenceSystem>().SetAutomatic(server, true);
            if (!servers.Any(uid => Comp<KiasDefenceComponent>(uid).PdcEnabled))
                throw new InvalidOperationException("Native PDC automatic mode did not enable.");
            var disposition = EntityManager.System<KiasNavigationSystem>().Classify(grid, NativeLab.PdcSource(grid));
            if (disposition != (type == "pdc.friendly" ? KiasContactDisposition.Friendly : KiasContactDisposition.Hostile))
                throw new InvalidOperationException("Native PDC fixture IFF classification is incorrect.");
            return new { onlineWeapons = weapons.Count(kias.IsOnline), onlineRadars = radars.Count(kias.IsOnline),
                disposition = disposition.ToString(), automatic = true };
        }
        var restore = type.EndsWith("restore", StringComparison.Ordinal);
        if (start)
        {
            if (!restore)
            {
                if (_savedCards.ContainsKey(grid)) throw new InvalidOperationException("Overlapping availability wave on the same grid.");
                _onlineBefore[grid] = OnGrid<KiasDeviceComponent>(grid).Where(kias.IsOnline).ToArray();
                if (_onlineBefore[grid].Length == 0) throw new InvalidOperationException("No initially online KIAS devices.");
                _savedCards[grid] = OnGrid<KiasControllerCardComponent>(grid).Where(runtime.Running).ToDictionary(uid => uid,
                    uid => { var card = Comp<KiasControllerCardComponent>(uid); return (card.Revision, JsonSerializer.Serialize(card.Program, new JsonSerializerOptions { IncludeFields = true })); });
                NativeLab.SaveDiagnostic($"availability-{grid}-{type}-before.json", new {
                    initiallyOnline = _onlineBefore[grid].Length, runningCards = _savedCards[grid].Count,
                    initiallyOffline = OnGrid<KiasDeviceComponent>(grid).Where(uid => !kias.IsOnline(uid)).Select(uid => new {
                        entity = uid.ToString(), prototype = MetaData(uid).EntityPrototype?.ID, Transform(uid).Anchored,
                        status = Comp<KiasDeviceComponent>(uid).Status.ToString() }).ToArray() });
                NativeLab.ExpectedOfflineGrids[grid] = IoCManager.Resolve<IGameTiming>().CurTick.Value + 3600;
            }
            if (type == "data.cut")
            {
                var cables = OnGrid<KiasDataCableComponent>(grid).Select(uid => new CableSnapshot(uid,
                    MetaData(uid).EntityPrototype?.ID ?? throw new InvalidOperationException("DATA cable has no prototype."),
                    Transform(uid).Coordinates, Transform(uid).LocalRotation,
                    TryComp<VisibilityComponent>(uid, out var visibility) ? visibility.Layer : (ushort) 1)).ToList();
                if (cables.Count == 0 || cables.Any(cable => !Transform(cable.Original).Anchored))
                    throw new InvalidOperationException("Missing anchored native DATA cables.");
                var floors = DataFloors(grid);
                _cutCables.Add(grid, new(cables, floors, FloorHash(floors)));
                NativeLab.SaveDiagnostic($"data-{grid}-before-{IoCManager.Resolve<IGameTiming>().CurTick.Value}.json", new {
                    floorSha256 = FloorHash(floors), cables = cables.Select(cable => new { uid = cable.Original.ToString(),
                        cable.Prototype, grid = grid.ToString(), x = cable.Position.X, y = cable.Position.Y,
                        rotation = cable.Rotation.Theta, visibility = cable.Layer }).ToArray() });
                foreach (var cable in cables) EntityManager.DeleteEntity(cable.Original);
            }
            else if (type == "data.restore")
            {
                foreach (var cable in _cutCables[grid].Cables)
                {
                    var uid = Spawn(cable.Prototype, cable.Position);
                    EntityManager.System<SharedTransformSystem>().SetLocalRotation(uid, cable.Rotation);
                    if (!Transform(uid).Anchored)
                        throw new InvalidOperationException("DATA cable replacement prototype did not initialize anchored.");
                }
            }
            else
            {
                if (!restore)
                    _powerTargets[grid] = (type.StartsWith("core", StringComparison.Ordinal)
                        ? OnGrid<KiasCoreComponent>(grid) : OnGrid<KiasControllerRackComponent>(grid)).ToList();
                if (_powerTargets[grid].Count == 0) throw new InvalidOperationException("Missing powered KIAS target.");
                foreach (var target in _powerTargets[grid]) EntityManager.System<SharedPowerReceiverSystem>().SetPowerDisabled(target, !restore);
            }
            return null;
        }
        if (restore)
        {
            if (!_onlineBefore[grid].All(kias.IsOnline)
                || !_savedCards[grid].Keys.All(runtime.Running)) return null;
            foreach (var (uid, saved) in _savedCards[grid])
            {
                var card = Comp<KiasControllerCardComponent>(uid);
                if (card.Revision != saved.Revision || JsonSerializer.Serialize(card.Program, new JsonSerializerOptions { IncludeFields = true }) != saved.Program || runtime.Fault(uid).Length > 0)
                    throw new InvalidOperationException("Card state was lost or faulted after native recovery.");
            }
            var dataIntegrity = type == "data.restore" ? VerifyDataSnapshot(grid, true) : null;
            var count = _savedCards[grid].Count;
            var restoredDevices = _onlineBefore[grid].Length;
            _onlineBefore.Remove(grid); _savedCards.Remove(grid); _cutCables.Remove(grid); _powerTargets.Remove(grid); NativeLab.ExpectedOfflineGrids.Remove(grid);
            return new { restored = true, preservedCards = count, restoredDevices, allInitiallyOnlineDevicesRestored = true, dataIntegrity };
        }
        if (kias.TopologyPending(grid)) return null;
        var offline = type == "data.cut"
            ? _onlineBefore[grid].Where(uid => !HasComp<KiasCoreComponent>(uid)).All(uid => !kias.IsOnline(uid)
                && Comp<KiasDeviceComponent>(uid).Status == KiasDeviceStatus.NoDataPath)
            : _powerTargets[grid].All(uid => !kias.IsOnline(uid));
        return offline ? new { offline = true, actualRemovedCables = type == "data.cut" ? _cutCables[grid].Cables.Count : 0,
            dataIntegrity = type == "data.cut" ? VerifyDataSnapshot(grid, false) : null,
            actualDisabledPowerReceivers = type == "data.cut" ? 0 : _powerTargets[grid].Count,
            topologyCommitted = true, committedNoDataPath = type == "data.cut" } : null;
    }

    private object CapturePhaseTotals()
    {
        var kias = EntityManager.System<KiasSystem>();
        return Enum.GetValues<KiasPhase>().Select(phase => new { name = phase.ToString(),
            calls = kias.Phases[(int) phase].Calls, inclusiveTicks = kias.Phases[(int) phase].TimestampTicks,
            exclusiveTicks = kias.Phases[(int) phase].ExclusiveTimestampTicks,
            inclusiveBytes = kias.Phases[(int) phase].AllocatedBytes, exclusiveBytes = kias.Phases[(int) phase].ExclusiveAllocatedBytes,
            buckets = kias.Phases[(int) phase].DurationBuckets.ToArray() }).ToArray();
    }

    private object Capture()
    {
        var grids = 0; var active = 0; var online = 0; var devices = 0; var running = 0;
        var query = EntityManager.AllEntityQueryEnumerator<KiasGridComponent>();
        while (query.MoveNext(out _, out var grid))
        {
            grids++; if (grid.Active) active++;
            online += grid.Online.Count; devices += grid.Devices.Count;
        }
        var runtime = EntityManager.System<KiasControllerRuntimeSystem>();
        var runtimeCards = (IDictionary) (typeof(KiasControllerRuntimeSystem).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(runtime)
            ?? throw new InvalidOperationException("Missing runtime card table."));
        var kias = EntityManager.System<KiasSystem>();
        var dirty = (IReadOnlySet<EntityUid>) (typeof(KiasSystem).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(kias)
            ?? throw new InvalidOperationException("Missing topology dirty set."));
        var alive = 0; var faults = 0; var unavailable = new List<object>(); var dirtyOnly = true;
        var recovered = 0; var sameMachines = 0;
        var expectedUnavailable = 0;
        var cards = EntityManager.AllEntityQueryEnumerator<KiasControllerCardComponent>();
        while (cards.MoveNext(out var uid, out var card))
        {
            var stored = runtimeCards[uid];
            var machine = stored?.GetType().GetField("Machine")?.GetValue(stored) as KiasGraphMachine;
            if (machine?.Active == true) alive++;
            if (runtime.Fault(uid).Length > 0) faults++;
            if (runtime.Running(uid))
            {
                running++;
                if (_pendingMachines.Remove(uid, out var previous)) { recovered++; if (ReferenceEquals(previous, machine)) sameMachines++; }
                continue;
            }
            if (!card.Enabled) continue;
            var gridUid = Transform(uid).GridUid;
            var topologyDirty = gridUid is { } gridId && dirty.Contains(gridId);
            var expectedOffline = gridUid is { } affected && NativeLab.ExpectedOfflineGrids.TryGetValue(affected, out var deadline)
                && IoCManager.Resolve<IGameTiming>().CurTick.Value <= deadline;
            if (expectedOffline) expectedUnavailable++;
            dirtyOnly &= expectedOffline || topologyDirty && machine?.Active == true;
            if (machine?.Active == true) _pendingMachines.TryAdd(uid, machine);
            unavailable.Add(new { card = uid.ToString(), program = card.Program.Name, grid = gridUid?.ToString(),
                topologyDirty, machineActive = machine?.Active == true, status = runtime.Status(uid), fault = runtime.Fault(uid) });
        }
        NativeLab.TelemetryRetryRequired = unavailable.Count > expectedUnavailable;
        var availabilityPending = unavailable.Count > 0 && dirtyOnly && faults == 0;
        var queues = new Dictionary<string, int>();
        foreach (var name in new[] { "_events", "_work", "_commands", "_boots", "_finalEvents", "_finalCommands", "_waitingTimers" })
        {
            var value = typeof(KiasControllerRuntimeSystem).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(runtime)
                ?? throw new InvalidOperationException($"Missing laboratory queue metric {name}.");
            queues[name] = (int) (value.GetType().GetProperty("Count")?.GetValue(value)
                ?? throw new InvalidOperationException($"Queue {name} has no Count."));
        }
        queues["pendingDispatch"] = runtime.PendingEventDispatchCount;
        var cores = new List<object>();
        var coreQuery = EntityManager.AllEntityQueryEnumerator<KiasCoreComponent, KiasDeviceComponent, TransformComponent>();
        while (coreQuery.MoveNext(out var uid, out var core, out var device, out var transform))
        {
            EntityManager.TryGetComponent<ApcPowerReceiverComponent>(uid, out var power);
            cores.Add(new { core.Enabled, anchored = transform.Anchored, device.Status, powered = power?.Powered, disabled = power?.PowerDisabled,
                received = power?.PowerReceived, load = power?.Load, provider = power?.Provider != null });
        }
        var batteries = EntityManager.AllEntityQueryEnumerator<BatteryComponent>();
        var charge = 0d; var capacity = 0d; var batteryCount = 0;
        while (batteries.MoveNext(out _, out var battery)) { charge += battery.CurrentCharge; capacity += battery.MaxCharge; batteryCount++; }
        return new { grids, activeGrids = active, topologyOnlineDevices = online, devices, runningCards = running,
            auxiliaryDockingSensors = _auxDockingSensors.Select(uid => new { uid = uid.ToString(),
                online = EntityManager.System<KiasSystem>().IsOnline(uid), position = Transform(uid).LocalPosition.ToString() }).ToArray(),
            activeMachines = alive, faultedCards = faults, unavailableCards = unavailable, topologyDirtyGrids = dirty.Count,
            availabilityPending, expectedOfflineGrids = NativeLab.ExpectedOfflineGrids.Count, expectedUnavailable,
            recoveredCards = recovered, recoveredSameMachines = sameMachines,
            queues, cores, charge, capacity, batteryCount,
            acceptedCommandLatency = new { count = runtime.AcceptedCommandLatency.Consumed,
                p50Ticks = runtime.AcceptedCommandLatency.Percentile(.5), p95Ticks = runtime.AcceptedCommandLatency.Percentile(.95),
                p99Ticks = runtime.AcceptedCommandLatency.Percentile(.99), maxTicks = runtime.AcceptedCommandLatency.MaxAgeTicks,
                runtime.CancelledCommands, runtime.FeedbackRejectedCommands, runtime.ActuatorExceptions,
                scope = "Root external emission (or timer/boot command creation) to accepted IO dispatch; physical completion is observed by native scenario drivers separately." },
            actuatorDispatchLatency = runtime.ActuatorDispatchLatency.Select(pair => new {
                profile = pair.Key.Profile, port = pair.Key.Port, count = pair.Value.Consumed,
                p50Ticks = pair.Value.Percentile(.5), p95Ticks = pair.Value.Percentile(.95),
                p99Ticks = pair.Value.Percentile(.99), maxTicks = pair.Value.MaxAgeTicks }).ToArray(),
            queueLatency = new[] { (name: "events", stats: runtime.EventQueueMetrics), (name: "work", stats: runtime.WorkQueueMetrics),
                (name: "commands", stats: runtime.CommandQueueMetrics) }.Select(item => new { item.name,
                    item.stats.Enqueued, item.stats.Consumed, item.stats.Rejected, item.stats.HighWater, item.stats.BackloggedTicks,
                    item.stats.MaxAgeTicks, p50Ticks = item.stats.Percentile(.5), p95Ticks = item.stats.Percentile(.95),
                    p99Ticks = item.stats.Percentile(.99), buckets = item.stats.AgeTicks,
                    scope = "enqueue to dequeue; includes cancelled or invalidated work; bucket 4096 includes overflow" }).ToArray(),
            phases = Enum.GetValues<KiasPhase>().Select(phase => new { name = phase.ToString(), metrics = new { kias.Phases[(int) phase].Calls, milliseconds = kias.Phases[(int) phase].TimestampTicks * 1000d / System.Diagnostics.Stopwatch.Frequency, kias.Phases[(int) phase].AllocatedBytes, exclusiveMilliseconds = kias.Phases[(int) phase].ExclusiveTimestampTicks * 1000d / System.Diagnostics.Stopwatch.Frequency, exclusiveAllocatedBytes = kias.Phases[(int) phase].ExclusiveAllocatedBytes, maxMilliseconds = kias.Phases[(int) phase].MaxTimestampTicks * 1000d / System.Diagnostics.Stopwatch.Frequency, kias.Phases[(int) phase].Items, kias.Phases[(int) phase].MaxQueue, buckets = kias.Phases[(int) phase].DurationBuckets } }).ToArray(),
            scope = "Minute-boundary samples; topology online set is not a fresh per-device power query; queue size is sampled, not peak backlog." };
    }
}
