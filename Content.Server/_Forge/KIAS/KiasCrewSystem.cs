using System.Linq;
using System.Text;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Radiation.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Server._Forge.KIAS.Controllers;
using Content.Shared.Access.Systems;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.SurveillanceCamera.Components;
using Content.Shared.SurveillanceCamera;
using Content.Shared.NPC.Systems;

namespace Content.Server._Forge.KIAS;

public sealed class KiasCrewSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasRoomTopologySystem _rooms = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedIdCardSystem _ids = default!;
    [Dependency] private DeviceLinkSystem _links = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _people = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _transponders = new();
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, List<EntityUid>>> _coverage = new();
    private readonly KiasPeriodicScheduler _scans = new(1);
    private readonly HashSet<EntityUid> _radiationHigh = new();
    private readonly Dictionary<EntityUid, EntityUid> _opticalCameras = new();
    private readonly HashSet<EntityUid> _fauna = new();
    private sealed class ScanScratch
    {
        public bool Scanning;
        public bool GeometryChanged;
        public readonly Dictionary<EntityUid, Vector2i> PreviousLocations = new();
        public readonly HashSet<EntityUid> MotionScanners = new();
        public readonly Dictionary<EntityUid, int> Counts = new();
        public readonly StringBuilder Details = new();
        public readonly HashSet<string> Serials = new(), Registered = new();
        public readonly Dictionary<EntityUid, HashSet<string>> RoomCrew = new();
        public readonly List<EntityUid> Devices = new();
        public readonly HashSet<EntityUid> Armed = new();
        public readonly HashSet<EntityUid> People = new();
        public void Reset()
        {
            Counts.Clear(); Details.Clear(); Serials.Clear(); Registered.Clear(); Devices.Clear(); MotionScanners.Clear();
            People.Clear();
            foreach (var serials in RoomCrew.Values) serials.Clear();
        }
    }
    private readonly Dictionary<EntityUid, ScanScratch> _scratch = new();
    private ScanScratch ScratchFor(EntityUid grid)
    {
        if (!_scratch.TryGetValue(grid, out var scratch)) _scratch.Add(grid, scratch = new());
        return scratch;
    }
    private readonly struct ScanGuard : IDisposable
    {
        private readonly ScanScratch _scratch;
        public ScanGuard(ScanScratch scratch) { _scratch = scratch; scratch.Scanning = true; }
        public void Dispose() => _scratch.Scanning = false;
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMind);
        SubscribeLocalEvent<MindContainerComponent, PlayerAttachedEvent>(OnPlayer);
        SubscribeLocalEvent<BorgChassisComponent, ComponentInit>(OnBorg);
        SubscribeLocalEvent<KiasTrackedEntityComponent, ComponentStartup>(OnTrackedStartup);
        SubscribeLocalEvent<KiasTrackedEntityComponent, ComponentShutdown>(OnTrackedShutdown);
        SubscribeLocalEvent<KiasTrackedEntityComponent, EntParentChangedMessage>(OnTrackedParent);
        SubscribeLocalEvent<KiasTrackedEntityComponent, GridUidChangedEvent>(OnTrackedGrid);
        SubscribeLocalEvent<KiasTransponderComponent, ComponentStartup>(OnTransponderStartup);
        SubscribeLocalEvent<KiasTransponderComponent, ComponentShutdown>(OnTransponderShutdown);
        SubscribeLocalEvent<KiasTransponderComponent, EntParentChangedMessage>(OnTransponderParent);
        SubscribeLocalEvent<KiasTransponderComponent, GridUidChangedEvent>(OnTransponderGrid);
        SubscribeLocalEvent<KiasTransponderComponent, MapInitEvent>(OnSerial);
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology);
        SubscribeLocalEvent<KiasRoomsChangedEvent>(OnRooms);
        SubscribeLocalEvent<KiasRoomsInvalidatedEvent>(OnRoomsInvalidated);
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
        SubscribeLocalEvent<KiasRoomScannerComponent, EntInsertedIntoContainerMessage>(OnModuleInserted);
        SubscribeLocalEvent<KiasRoomScannerComponent, EntRemovedFromContainerMessage>(OnModuleRemoved);
        SubscribeLocalEvent<KiasRoomScannerComponent, ComponentShutdown>(OnScannerShutdown);
        SubscribeLocalEvent<KiasRoomScannerComponent, Content.Server.SurveillanceCamera.SurveillanceCameraSetActiveAttemptEvent>(OnCameraActivate);
        SubscribeLocalEvent<KiasCrewServerComponent, InteractUsingEvent>(OnRegister);
        SubscribeLocalEvent<KiasCrewServerComponent, GetVerbsEvent<AlternativeVerb>>(OnRegistrationVerbs);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
    }

    public bool IsKiasTrackedEntity(EntityUid uid) => !HasComp<GhostComponent>(uid) && HasComp<KiasTrackedEntityComponent>(uid);

    private void OnCameraActivate(Entity<KiasRoomScannerComponent> ent, ref Content.Server.SurveillanceCamera.SurveillanceCameraSetActiveAttemptEvent args)
    {
        args.Cancelled |= !_kias.IsOnline(ent) || (ent.Comp.Modules & KiasScannerModules.Optical) == 0;
    }

    private void OnMind(Entity<MindContainerComponent> ent, ref MindAddedMessage args)
    {
        if (args.Mind.Comp.UserId != null && !HasComp<GhostComponent>(ent))
            EnsureComp<KiasTrackedEntityComponent>(ent);
    }
    private void OnPlayer(Entity<MindContainerComponent> ent, ref PlayerAttachedEvent args)
    {
        if (!HasComp<GhostComponent>(ent))
            EnsureComp<KiasTrackedEntityComponent>(ent);
    }
    private void OnBorg(Entity<BorgChassisComponent> ent, ref ComponentInit args) => EnsureComp<KiasTrackedEntityComponent>(ent);
    private void OnTrackedStartup(Entity<KiasTrackedEntityComponent> ent, ref ComponentStartup args) => Index(ent, ref ent.Comp.IndexedGrid, _people);
    private void OnTrackedParent(Entity<KiasTrackedEntityComponent> ent, ref EntParentChangedMessage args) => Index(ent, ref ent.Comp.IndexedGrid, _people);
    private void OnTrackedGrid(Entity<KiasTrackedEntityComponent> ent, ref GridUidChangedEvent args) => Index(ent, ref ent.Comp.IndexedGrid, _people);
    private void OnTrackedShutdown(Entity<KiasTrackedEntityComponent> ent, ref ComponentShutdown args)
    {
        foreach (var cached in _scratch.Values) { cached.Armed.Remove(ent); cached.PreviousLocations.Remove(ent); }
        if (ent.Comp.IndexedGrid is { } grid && _scratch.TryGetValue(grid, out var scratch)) scratch.PreviousLocations.Remove(ent);
        RemoveIndex(ent, ref ent.Comp.IndexedGrid, _people);
    }
    private void OnTransponderStartup(Entity<KiasTransponderComponent> ent, ref ComponentStartup args) => Index(ent, ref ent.Comp.IndexedGrid, _transponders);
    private void OnTransponderParent(Entity<KiasTransponderComponent> ent, ref EntParentChangedMessage args) => Index(ent, ref ent.Comp.IndexedGrid, _transponders);
    private void OnTransponderGrid(Entity<KiasTransponderComponent> ent, ref GridUidChangedEvent args) => Index(ent, ref ent.Comp.IndexedGrid, _transponders);
    private void OnTransponderShutdown(Entity<KiasTransponderComponent> ent, ref ComponentShutdown args) => RemoveIndex(ent, ref ent.Comp.IndexedGrid, _transponders);
    private void OnSerial(Entity<KiasTransponderComponent> ent, ref MapInitEvent args)
    {
        if (string.IsNullOrEmpty(ent.Comp.Serial))
            ent.Comp.Serial = Guid.NewGuid().ToString("N");
    }

    private void RemoveIndex(EntityUid uid, ref EntityUid? oldGrid, Dictionary<EntityUid, HashSet<EntityUid>> index)
    {
        if (oldGrid is { } old && index.TryGetValue(old, out var previous))
        {
            previous.Remove(uid);
            if (previous.Count == 0)
                index.Remove(old);
        }
        oldGrid = null;
    }
    private void Index(EntityUid uid, ref EntityUid? oldGrid, Dictionary<EntityUid, HashSet<EntityUid>> index)
    {
        _metadata.AddFlag(uid, MetaDataFlags.ExtraTransformEvents);
        RemoveIndex(uid, ref oldGrid, index);
        if (TerminatingOrDeleted(uid) || Transform(uid).GridUid is not { } grid)
            return;
        oldGrid = grid;
        if (!index.TryGetValue(grid, out var entities))
            index.Add(grid, entities = new HashSet<EntityUid>());
        entities.Add(uid);
    }

    private void OnModuleInserted(Entity<KiasRoomScannerComponent> ent, ref EntInsertedIntoContainerMessage args) => RebuildCoverage(Transform(ent).GridUid);
    private void OnModuleRemoved(Entity<KiasRoomScannerComponent> ent, ref EntRemovedFromContainerMessage args) => RebuildCoverage(Transform(ent).GridUid);
    private void OnScannerShutdown(Entity<KiasRoomScannerComponent> ent, ref ComponentShutdown args)
    {
        _radiationHigh.Remove(ent);
        RebuildCoverage(Transform(ent).GridUid);
    }
    private void OnTopology(ref KiasTopologyChangedEvent args) => RebuildCoverage(args.Grid);
    private void OnRooms(ref KiasRoomsChangedEvent args)
    {
        ScratchFor(args.Grid).GeometryChanged = true;
        RebuildCoverage(args.Grid);
    }
    private void OnRoomsInvalidated(ref KiasRoomsInvalidatedEvent args)
    {
        if (!TryComp<KiasGridComponent>(args.Grid, out var runtime)) return;
        runtime.Entities = 0;
        runtime.Crew = 0;
        runtime.CrewDetails = string.Empty;
        var countsChanged = new KiasCountsChangedEvent(args.Grid, 0, 0);
        RaiseLocalEvent(args.Grid, ref countsChanged, true);
        var io = EntityManager.System<KiasControllerIoSystem>();
        foreach (var device in runtime.Online.ToArray())
        {
            if (!HasComp<KiasRoomScannerComponent>(device)) continue;
            io.EmitChanged(device, "RoomScanner", "Entities", KiasGraphValue.Numeric(0));
            io.EmitChanged(device, "RoomScanner", "Occupied", KiasGraphValue.Boolean(false));
            io.EmitChanged(device, "RoomScanner", "Crew", KiasGraphValue.Numeric(0));
        }
    }
    private void OnAvailability(ref KiasAvailabilityChangedEvent args)
    {
        if (!args.Active)
        {
            DisableOfflineCameras(args.Grid);
            EntityManager.System<KiasIntegrationSystem>().Reconcile(args.Grid);
            _coverage.Remove(args.Grid);
            _scans.Remove(args.Grid);
            if (TryComp<KiasGridComponent>(args.Grid, out var runtime))
                foreach (var device in runtime.Devices)
                {
                    if (HasComp<SurveillanceCameraComponent>(device)) EntityManager.System<SharedSurveillanceCameraSystem>().SetActive(device, false);
                    if (HasComp<KiasRoomScannerComponent>(device)) RemComp<RadiationReceiverComponent>(device);
                    _radiationHigh.Remove(device);
                }
        }
    }
    private void OnGridRemoval(GridRemovalEvent args)
    {
        DisableOfflineCameras(args.EntityUid);
        _people.Remove(args.EntityUid);
        _transponders.Remove(args.EntityUid);
        _coverage.Remove(args.EntityUid);
        _scratch.Remove(args.EntityUid);
        _scans.Remove(args.EntityUid);
    }

    public void RebuildCoverage(EntityUid? grid)
    {
        using var phase = new KiasPhaseMeasurement(_kias, KiasPhase.CrewCoverage);
        if (grid is { } changedGrid) DisableOfflineCameras(changedGrid);
        if (grid is not { } uid || !TryComp<KiasGridComponent>(uid, out var runtime) || !runtime.Active || !TryComp<MapGridComponent>(uid, out var map))
            return;
        if (!_rooms.Grids.ContainsKey(uid)) _rooms.Invalidate(uid);
        _rooms.BindScanners(uid);
        var coverage = _rooms.Coverage(uid);
        foreach (var device in runtime.Devices)
            if (HasComp<KiasRoomScannerComponent>(device) && !_kias.IsOnline(device)
                && HasComp<SurveillanceCameraComponent>(device))
                EntityManager.System<SharedSurveillanceCameraSystem>().SetActive(device, false);
        foreach (var device in runtime.Online)
        {
            if (!_kias.IsOnline(device) || !TryComp<KiasRoomScannerComponent>(device, out var scanner) || scanner.LifeStage > ComponentLifeStage.Running)
                continue;
            scanner.Modules = KiasScannerModules.None;
            for (var slot = 1; slot <= 6; slot++)
            {
                if (!_containers.TryGetContainer(device, $"kias-module-{slot}", out var container))
                    continue;
                foreach (var item in container.ContainedEntities)
                {
                    if (TryComp<KiasScannerModuleComponent>(item, out var module))
                        scanner.Modules |= module.Module is KiasScannerModules.Id or KiasScannerModules.Transponder ? KiasScannerModules.Identity : module.Module;
                }
            }
            if ((scanner.Modules & KiasScannerModules.Optical) != 0)
                EnsureComp<SurveillanceCameraComponent>(device);
            if (HasComp<SurveillanceCameraComponent>(device))
            {
                EntityManager.System<SharedSurveillanceCameraSystem>().SetActive(device, (scanner.Modules & KiasScannerModules.Optical) != 0);
                if ((scanner.Modules & KiasScannerModules.Optical) != 0) _opticalCameras[device] = uid;
                else _opticalCameras.Remove(device);
            }
            if (scanner.Modules == KiasScannerModules.None)
            {
                RemCompDeferred<RadiationReceiverComponent>(device);
                continue;
            }
            if ((scanner.Modules & KiasScannerModules.Radiation) != 0)
                EnsureComp<RadiationReceiverComponent>(device);
            else
                RemCompDeferred<RadiationReceiverComponent>(device);
        }
        if (coverage.Count == 0)
        {
            _coverage.Remove(uid);
            _scans.Remove(uid);
            if (runtime.Entities != 0 || runtime.Crew != 0 || runtime.CrewDetails.Length != 0)
            {
                runtime.Entities = 0;
                runtime.Crew = 0;
                runtime.CrewDetails = string.Empty;
                var changed = new KiasCountsChangedEvent(uid, 0, 0);
                RaiseLocalEvent(uid, ref changed, true);
            }
        }
        else
        {
            _coverage[uid] = coverage;
            _scans.Add(uid, _timing.CurTime);
        }
        EntityManager.System<KiasIntegrationSystem>().Reconcile(uid);
    }

    private void DisableOfflineCameras(EntityUid grid)
    {
        foreach (var (device, previousGrid) in _opticalCameras.ToArray())
        {
            if (previousGrid != grid) continue;
            if (!TerminatingOrDeleted(device) && HasComp<KiasRoomScannerComponent>(device) && _kias.IsOnline(device)) continue;
            if (!TerminatingOrDeleted(device) && HasComp<SurveillanceCameraComponent>(device))
                EntityManager.System<SharedSurveillanceCameraSystem>().SetActive(device, false);
            _opticalCameras.Remove(device);
        }
    }

    public bool HasCoverage(EntityUid grid, EntityUid source, KiasScannerModules module)
    {
        if (!_kias.ActiveGrids.Contains(grid)) return false;
        var buffer = _covering;
        _rooms.GetScannersCovering(grid, source, module, buffer);
        return buffer.Count > 0;
    }

    private readonly List<EntityUid> _covering = new();
    public IEnumerable<EntityUid> ScannersCovering(EntityUid grid, EntityUid source, KiasScannerModules module)
    {
        var result = new List<EntityUid>();
        if (_kias.ActiveGrids.Contains(grid)) _rooms.GetScannersCovering(grid, source, module, result);
        return result;
    }

    private KiasScannerModules Modules(EntityUid scanner) => TryComp<KiasRoomScannerComponent>(scanner, out var component) && _kias.IsOnline(scanner)
        ? component.Modules : KiasScannerModules.None;

    public bool CrewUnavailable(EntityUid grid)
    {
        if (!_kias.HasRole(grid, KiasDeviceRole.Crew) || !TryComp<KiasGridComponent>(grid, out var runtime)
            || !runtime.Online.Any(uid => (Modules(uid) & KiasScannerModules.Biometric) != 0))
            return false;
        var registered = ScratchFor(grid).Registered;
        registered.Clear();
        foreach (var uid in runtime.Online)
        {
            if (TryComp<KiasCrewServerComponent>(uid, out var server) && _kias.IsOnline(uid))
                registered.UnionWith(server.Registered);
        }
        if (registered.Count == 0)
            return false;
        if (!_transponders.TryGetValue(grid, out var tokens))
            return true;
        foreach (var uid in tokens)
        {
            if (!TryComp<KiasTransponderComponent>(uid, out var token) || token.Core != runtime.Core || !registered.Contains(token.Serial))
                continue;
            var carrier = Transform(uid).ParentUid;
            for (var depth = 0; depth < 16 && carrier.Valid && carrier != grid && !TerminatingOrDeleted(carrier); depth++)
            {
                if (IsKiasTrackedEntity(carrier) && HasCoverage(grid, carrier, KiasScannerModules.Biometric)
                    && (!TryComp<MobStateComponent>(carrier, out var state) || state.CurrentState == MobState.Alive))
                    return false;
                carrier = Transform(carrier).ParentUid;
            }
        }
        return true;
    }

    public string? RoomLabel(EntityUid grid, EntityUid source)
    {
        if (!_coverage.TryGetValue(grid, out var coverage) || !TryComp<MapGridComponent>(grid, out var map))
            return null;
        var coordinates = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(source));
        if (!coverage.TryGetValue(_map.TileIndicesFor(grid, map, coordinates), out var scanners))
            return null;
        foreach (var scanner in scanners.OrderBy(uid => uid.Id))
        {
            if (TryComp<KiasDeviceComponent>(scanner, out var device) && !string.IsNullOrWhiteSpace(device.Room))
                return device.Room;
        }
        return null;
    }

    public bool IsRegisteredPerson(EntityUid grid, EntityUid person)
    {
        if (!_transponders.TryGetValue(grid, out var tokens) || !TryComp<KiasGridComponent>(grid, out var runtime)) return false;
        foreach (var token in tokens)
        {
            if (!TryComp<KiasTransponderComponent>(token, out var transponder) || transponder.Core != runtime.Core
                || !runtime.Online.Any(uid => TryComp<KiasCrewServerComponent>(uid, out var crew) && crew.Registered.Contains(transponder.Serial))) continue;
            var carrier = token;
            for (var depth = 0; depth < 16 && carrier.Valid; depth++)
            {
                if (carrier == person) return true;
                carrier = Transform(carrier).ParentUid;
            }
        }
        return false;
    }

    public bool HasUnknownAlongside(EntityUid grid, EntityUid injured)
    {
        if (!_people.TryGetValue(grid, out var people) || !_coverage.TryGetValue(grid, out var coverage)
            || !TryComp<MapGridComponent>(grid, out var map)) return false;
        var tile = _map.TileIndicesFor(grid, map, _transform.ToCoordinates(grid, _transform.GetMapCoordinates(injured)));
        if (!coverage.TryGetValue(tile, out var room)) return false;
        foreach (var person in people)
        {
            if (person == injured || TerminatingOrDeleted(person) || IsRegisteredPerson(grid, person)
                || _kias.CanConfigure(grid, person)
                || TryComp<MobStateComponent>(person, out var state) && state.CurrentState != MobState.Alive) continue;
            var position = _map.TileIndicesFor(grid, map, _transform.ToCoordinates(grid, _transform.GetMapCoordinates(person)));
            if (_rooms.SharesRoom(grid, injured, person)) return true;
        }
        return false;
    }

    private void OnRegister(Entity<KiasCrewServerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<KiasTransponderComponent>(args.Used, out var transponder))
            return;
        args.Handled = true;
        if (ent.Comp.RegistrationLocked || !_kias.IsOnline(ent) || Transform(ent).GridUid is not { } grid)
        {
            _popup.PopupEntity(Loc.GetString("kias-registration-locked"), ent, args.User);
            return;
        }
        if (!EntityManager.System<KiasAccessSystem>().CanRegister(grid, args.User, ent.Comp.RegistrationLocked))
            return;
        if (string.IsNullOrEmpty(transponder.Serial))
            transponder.Serial = Guid.NewGuid().ToString("N");
        transponder.Core = Comp<KiasGridComponent>(grid).Core;
        ent.Comp.Registered.Add(transponder.Serial);
        _popup.PopupEntity(Loc.GetString("kias-transponder-registered"), ent, args.User);
    }

    private void OnRegistrationVerbs(Entity<KiasCrewServerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_kias.IsOnline(ent) || Transform(ent).GridUid is not { } grid
            || !EntityManager.System<KiasAccessSystem>().CanConfigure(grid, args.User, security: true))
            return;
        var component = ent.Comp;
        var server = ent.Owner;
        var actor = args.User;
        var locked = !component.RegistrationLocked;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(locked ? "kias-lock-registration" : "kias-unlock-registration"),
            Act = () =>
            {
                if (_kias.IsOnline(server) && Transform(server).GridUid == grid && EntityManager.System<KiasAccessSystem>().CanConfigure(grid, actor, security: true))
                    component.RegistrationLocked = locked;
            },
        });
    }

    public override void Update(float frameTime)
    {
        using var measurement = new KiasUpdateMeasurement(_kias);
        using var phase = new KiasPhaseMeasurement(_kias, KiasPhase.CrewUpdate);
        const int gridBudget = 8;
        for (var i = 0; i < gridBudget && _scans.TryDue(_timing.CurTime, out var grid); i++)
        {
            if (_coverage.TryGetValue(grid, out var coverage) && _rooms.Grids.TryGetValue(grid, out var rooms) && !rooms.Pending) Scan(grid, coverage);
        }
    }

    public void RefreshCounts(EntityUid grid)
    {
        if (_coverage.TryGetValue(grid, out var coverage) && _rooms.Grids.TryGetValue(grid, out var rooms) && !rooms.Pending)
            Scan(grid, coverage);
    }

    private void Scan(EntityUid grid, Dictionary<Vector2i, List<EntityUid>> coverage)
    {
        using var phase = new KiasPhaseMeasurement(_kias, KiasPhase.CrewScan);
        if (!TryComp<KiasGridComponent>(grid, out var runtime) || !runtime.Active || !TryComp<MapGridComponent>(grid, out var map))
            return;
        var scratch = ScratchFor(grid);
        if (scratch.Scanning) return;
        using var guard = new ScanGuard(scratch);
        scratch.Reset();
        var counts = scratch.Counts;
        var armedTargets = scratch.Armed;
        armedTargets.RemoveWhere(uid => TerminatingOrDeleted(uid) || !HasCoverage(grid, uid, KiasScannerModules.Threat));
        var entities = 0;
        var details = scratch.Details;
        var exterior = _rooms.Grids.TryGetValue(grid, out var regions)
            && regions.Scanners.Values.Any(region => region.Status == KiasRoomStatus.ExteriorSector);
        if (_people.TryGetValue(grid, out var ownPeople)) scratch.People.UnionWith(ownPeople);
        if (exterior)
        {
            var tracked = EntityQueryEnumerator<KiasTrackedEntityComponent, TransformComponent>();
            while (tracked.MoveNext(out var person, out _, out var transform))
                if (transform.MapUid == Transform(grid).MapUid) scratch.People.Add(person);
        }
        if (scratch.People.Count > 0)
        {
            foreach (var person in scratch.People)
            {
                if (TerminatingOrDeleted(person) || !IsKiasTrackedEntity(person))
                    continue;
                using var peoplePhase = new KiasPhaseMeasurement(_kias, KiasPhase.CrewPeople);
                var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(person));
                var tile = _map.TileIndicesFor(grid, map, position);
                var moved = !scratch.PreviousLocations.TryGetValue(person, out var previousTile) || previousTile != tile;
                scratch.PreviousLocations[person] = tile;
                if (!coverage.TryGetValue(tile, out var scanners))
                    continue;
                var counted = false;
                var combined = KiasScannerModules.None;
                foreach (var scanner in scanners)
                {
                    var modules = Modules(scanner);
                    if (!_rooms.Contains(scanner, person, modules)) continue;
                    combined |= modules;
                    if ((modules & KiasScannerModules.Motion) != 0)
                    {
                        counts[scanner] = counts.GetValueOrDefault(scanner) + 1;
                        if (moved) scratch.MotionScanners.Add(scanner);
                        counted = true;
                    }
                }
                if (counted)
                    entities++;
                if ((combined & KiasScannerModules.Optical) != 0) details.AppendLine(Name(person));
                if ((combined & KiasScannerModules.Threat) != 0)
                {
                    var armed = EntityManager.System<SharedHandsSystem>().EnumerateHands(person).Any(hand => hand.HeldEntity is { } held && HasComp<GunComponent>(held));
                    if (armed && armedTargets.Add(person))
                    {
                        foreach (var detector in ScannersCovering(grid, person, KiasScannerModules.Threat))
                        {
                            EntityManager.System<KiasControllerIoSystem>().Emit(detector, "RoomScanner", "Person", KiasGraphValue.Reference(person));
                            EntityManager.System<KiasControllerIoSystem>().Emit(detector, "RoomScanner", "Threat", KiasGraphValue.Pulse);
                        }
                        EntityManager.System<KiasProtocolSystem>().Trigger(grid, KiasTrigger.LocalThreat,
                            message: Loc.GetString("kias-local-threat", ("location", EntityManager.System<KiasSafetySystem>().Location(grid, person))), eventKey: person.ToString());
                    }
                    if (!armed) armedTargets.Remove(person);
                }
                if (runtime.Core is { } core && TryComp<KiasProtocolComponent>(core, out var protocols) && !protocols.CaptainGreeted
                    && EntityManager.System<KiasAccessSystem>().ResolvePolicy(grid) != KiasAccessPolicy.OpenUnclaimed
                    && EntityManager.System<KiasAccessSystem>().CanConfigure(grid, person, security: true))
                {
                    protocols.CaptainGreeted = true;
                    EntityManager.System<KiasProtocolSystem>().Trigger(grid, KiasTrigger.CaptainGreeting, message: Loc.GetString("kias-captain-greeting"));
                }
                if ((combined & KiasScannerModules.Id) != 0 && _ids.TryFindIdCard(person, out var card))
                    details.AppendLine(card.Comp.FullName ?? Name(person));
                if ((combined & KiasScannerModules.Biometric) != 0 && TryComp<MobStateComponent>(person, out var state))
                {
                    var mobState = state.CurrentState;
                    details.AppendLine($"{Name(person)}: {Loc.GetString($"kias-biometric-{mobState.ToString().ToLowerInvariant()}")}");
                }
                if ((combined & KiasScannerModules.Radiation) != 0 && TryComp<RadiationReceiverComponent>(person, out var radiation) && radiation.CurrentRadiation > 0)
                    details.AppendLine(Loc.GetString("kias-radiation", ("name", Name(person)), ("value", radiation.CurrentRadiation)));
            }
        }
        using var registrationPhase = new KiasPhaseMeasurement(_kias, KiasPhase.CrewRegistration);
        var serials = scratch.Serials;
        var registered = scratch.Registered;
        scratch.Devices.AddRange(runtime.Online);
        _fauna.Clear();
        var faunaQuery = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (faunaQuery.MoveNext(out var candidate, out var faunaState, out var faunaTransform))
            if (faunaState.CurrentState == MobState.Alive
                && !IsKiasTrackedEntity(candidate)
                && (faunaTransform.GridUid == grid
                    || exterior && faunaTransform.MapUid == Transform(grid).MapUid)) _fauna.Add(candidate);
        foreach (var device in scratch.Devices)
        {
            if (TryComp<KiasCrewServerComponent>(device, out var server))
                registered.UnionWith(server.Registered);
            if (TryComp<KiasRoomScannerComponent>(device, out var scanner) && _rooms.TryGetScannerRoom(device, out _, out _))
            {
                if ((scanner.Modules & KiasScannerModules.Threat) != 0)
                {
                    using var faunaPhase = new KiasPhaseMeasurement(_kias, KiasPhase.CrewFauna);
                    if (_kias.MeasureUpdates) _kias.Phases[(int) KiasPhase.CrewFauna].Items += _fauna.Count;
                    foreach (var creature in _fauna)
                    {
                        if (!TryComp<MobStateComponent>(creature, out var state) || state.CurrentState != MobState.Alive
                            || !_rooms.Contains(device, creature, KiasScannerModules.Threat)) continue;
                        if (!(EntityManager.System<NpcFactionSystem>().IsFactionHostile("NanoTrasen", creature)
                                || _people.TryGetValue(grid, out var watched) && watched.Any(person => !TerminatingOrDeleted(person)
                                    && HasCoverage(grid, person, KiasScannerModules.Threat)
                                    && EntityManager.System<NpcFactionSystem>().IsHostileFactionMember(creature, person)))) continue;
                        EntityManager.System<KiasControllerIoSystem>().Emit(device, "RoomScanner", "FaunaThreat", KiasGraphValue.Pulse);
                        if (armedTargets.Add(creature)) EntityManager.System<KiasProtocolSystem>().Trigger(grid, KiasTrigger.LocalThreat,
                            message: Loc.GetString("kias-local-threat", ("location", EntityManager.System<KiasSafetySystem>().Location(grid, creature))), eventKey: creature.ToString());
                    }
                }
                if ((scanner.Modules & KiasScannerModules.Radiation) != 0 && TryComp<RadiationReceiverComponent>(device, out var radiation))
                {
                    var high = radiation.CurrentRadiation >= 1;
                    if (high && _radiationHigh.Add(device))
                    {
                        EntityManager.System<KiasControllerIoSystem>().Emit(device, "RoomScanner", "Radiation", KiasGraphValue.Pulse);
                        EntityManager.System<KiasProtocolSystem>().Trigger(grid, KiasTrigger.Radiation,
                            value: radiation.CurrentRadiation, message: Loc.GetString("kias-radiation-background", ("location", EntityManager.System<KiasSafetySystem>().Location(grid, device)), ("value", radiation.CurrentRadiation)), eventKey: device.ToString());
                    }
                    if (!high) _radiationHigh.Remove(device);
                }
                var count = counts.GetValueOrDefault(device);
                var controllers = EntityManager.System<KiasControllerIoSystem>();
                controllers.EmitChanged(device, "RoomScanner", "Entities", KiasGraphValue.Numeric(count));
                controllers.EmitChanged(device, "RoomScanner", "Occupied", KiasGraphValue.Boolean(count > 0));
                if (scanner.Entities == 0 && count > 0 && (!scratch.GeometryChanged || scratch.MotionScanners.Contains(device)))
                {
                    controllers.Emit(device, "RoomScanner", "Motion", KiasGraphValue.Pulse);
                    _links.InvokePort(device, "KiasMotion");
                }
                if (!runtime.Active)
                    return;
                scanner.Entities = count;
            }
        }
        var roomCrew = scratch.RoomCrew;
        if (_transponders.TryGetValue(grid, out var transponders))
        {
            foreach (var uid in transponders)
            {
                if (TryComp<KiasTransponderComponent>(uid, out var token) && token.Core == runtime.Core && registered.Contains(token.Serial)
                    && HasCoverage(grid, uid, KiasScannerModules.Transponder))
                {
                    serials.Add(token.Serial);
                    foreach (var scanner in ScannersCovering(grid, uid, KiasScannerModules.Transponder))
                    {
                        if (!roomCrew.TryGetValue(scanner, out var roomSerials)) roomCrew[scanner] = roomSerials = new();
                        roomSerials.Add(token.Serial);
                    }
                }
            }
        }
        var controllerIo = EntityManager.System<KiasControllerIoSystem>();
        foreach (var scanner in controllerIo.Devices(grid, "RoomScanner"))
            controllerIo.EmitChanged(scanner, "RoomScanner", "Crew", KiasGraphValue.Numeric(roomCrew.GetValueOrDefault(scanner)?.Count ?? 0));
        var unavailable = CrewUnavailable(grid);
        foreach (var server in controllerIo.Devices(grid, "CrewMonitor"))
        {
            controllerIo.EmitChanged(server, "CrewMonitor", "Crew", KiasGraphValue.Numeric(serials.Count));
            controllerIo.EmitChanged(server, "CrewMonitor", "Unavailable", KiasGraphValue.Boolean(unavailable));
        }
        var text = details.ToString();
        scratch.GeometryChanged = false;
        if (runtime.Entities == entities && runtime.Crew == serials.Count && runtime.CrewDetails == text)
            return;
        runtime.Entities = entities;
        runtime.Crew = serials.Count;
        runtime.CrewDetails = text;
        var ev = new KiasCountsChangedEvent(grid, entities, serials.Count);
        RaiseLocalEvent(grid, ref ev, true);
    }
}
