using System.Linq;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.KIAS;

public sealed class KiasIntegrationSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasRoomTopologySystem _rooms = default!;
    [Dependency] private DeviceLinkSystem _links = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _nativeTargets = new();
    private readonly Dictionary<EntityUid, EntityUid> _nativeGrids = new();
    private readonly Dictionary<EntityUid, EntityUid> _candidates = new();
    private readonly HashSet<EntityUid> _pendingRooms = new();
    private readonly Dictionary<EntityUid, EntityUid> _automatic = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _byGrid = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasIntegrationKitComponent, AfterInteractEvent>(OnInstall);
        SubscribeLocalEvent<KiasIntegratedComponent, SignalReceivedEvent>(OnSignal);
        SubscribeLocalEvent<KiasIntegratedComponent, MapInitEvent>(OnIntegratedInit);
        SubscribeLocalEvent<KiasIntegratedComponent, ComponentStartup>(OnIntegratedStartup);
        SubscribeLocalEvent<KiasIntegratedComponent, ComponentShutdown>(OnIntegratedShutdown);
        SubscribeLocalEvent<KiasIntegratedComponent, GridUidChangedEvent>(OnIntegratedGrid);
        SubscribeLocalEvent<KiasIntegratedComponent, AnchorStateChangedEvent>(OnIntegratedAnchor);
        SubscribeLocalEvent<KiasIntegratedComponent, MoveEvent>(OnIntegratedMove);
        EntityManager.EntityInitialized += OnNativeInitialized;
        SubscribeLocalEvent<ApcPowerReceiverComponent, AnchorStateChangedEvent>(OnNativeAnchor);
        SubscribeLocalEvent<ApcPowerReceiverComponent, MoveEvent>(OnNativeMove);
        EntityManager.EntityDeleted += OnNativeDeleted;
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
    }

    private void OnIntegratedStartup(Entity<KiasIntegratedComponent> ent, ref ComponentStartup args) => Track(ent);
    private void OnIntegratedGrid(Entity<KiasIntegratedComponent> ent, ref GridUidChangedEvent args) => Track(ent);
    private void OnIntegratedAnchor(Entity<KiasIntegratedComponent> ent, ref AnchorStateChangedEvent args) => Track(ent);
    private void OnIntegratedMove(Entity<KiasIntegratedComponent> ent, ref MoveEvent args)
    {
        if (!args.OnlyRotation) Track(ent);
    }
    private void OnIntegratedShutdown(Entity<KiasIntegratedComponent> ent, ref ComponentShutdown args)
    {
        Untrack(ent);
    }
    private void Untrack(EntityUid target)
    {
        if (!_automatic.Remove(target, out var old)) return;
        if (_byGrid.TryGetValue(old, out var targets))
        {
            targets.Remove(target);
            if (targets.Count == 0) _byGrid.Remove(old);
        }
        _pendingRooms.Add(old);
    }
    private void Track(Entity<KiasIntegratedComponent> ent)
    {
        Untrack(ent);
        if (!ent.Comp.Direct && Transform(ent).GridUid is { } grid)
        {
            _automatic[ent] = grid;
            if (!_byGrid.TryGetValue(grid, out var targets)) _byGrid.Add(grid, targets = new());
            targets.Add(ent);
            _pendingRooms.Add(grid);
        }
    }

    private void OnIntegratedInit(Entity<KiasIntegratedComponent> ent, ref MapInitEvent args)
    {
        if (HasComp<Content.Shared.Light.Components.PoweredLightComponent>(ent)) EnsureComp<KiasLightFixtureComponent>(ent);
    }

    public override void Shutdown()
    {
        EntityManager.EntityInitialized -= OnNativeInitialized;
        EntityManager.EntityDeleted -= OnNativeDeleted;
        _pendingRooms.Clear();
        _automatic.Clear();
        _byGrid.Clear();
        _nativeTargets.Clear(); _nativeGrids.Clear(); _candidates.Clear();
    }

    private bool NativeTarget(EntityUid uid) => HasComp<DoorComponent>(uid) || HasComp<AtmosMonitorComponent>(uid)
        || HasComp<AirAlarmComponent>(uid) || HasComp<FireAlarmComponent>(uid)
        || HasComp<GasVentPumpComponent>(uid) || HasComp<GasVentScrubberComponent>(uid);

    private void TrackNative(EntityUid uid)
    {
        if (_nativeGrids.Remove(uid, out var old))
        {
            if (_nativeTargets.TryGetValue(old, out var previous)) previous.Remove(uid);
            _pendingRooms.Add(old);
        }
        if (TerminatingOrDeleted(uid) || !NativeTarget(uid) || Transform(uid).GridUid is not { } grid) return;
        if (!_nativeTargets.TryGetValue(grid, out var targets)) _nativeTargets.Add(grid, targets = new());
        targets.Add(uid); _nativeGrids[uid] = grid; _pendingRooms.Add(grid);
    }
    private void OnNativeInitialized(Entity<MetaDataComponent> ent) => TrackNative(ent);
    private void OnNativeAnchor(Entity<ApcPowerReceiverComponent> ent, ref AnchorStateChangedEvent args) => TrackNative(ent);
    private void OnNativeMove(Entity<ApcPowerReceiverComponent> ent, ref MoveEvent args) => TrackNative(ent);
    private void OnNativeDeleted(Entity<MetaDataComponent> ent)
    {
        if (!_nativeGrids.Remove(ent, out var old)) return;
        if (_nativeTargets.TryGetValue(old, out var targets)) targets.Remove(ent);
        _pendingRooms.Add(old);
    }
    private void OnGridRemoval(GridRemovalEvent args)
    {
        if (_nativeTargets.Remove(args.EntityUid, out var targets)) foreach (var target in targets) _nativeGrids.Remove(target);
        _pendingRooms.Remove(args.EntityUid);
    }

    public override void Update(float frameTime)
    {
        if (_pendingRooms.Count == 0) return;
        foreach (var grid in _pendingRooms.Take(4).ToArray())
        {
            _pendingRooms.Remove(grid);
            if (!TerminatingOrDeleted(grid)) Reconcile(grid);
        }
    }

    public bool CanControl(EntityUid target)
    {
        if (TerminatingOrDeleted(target) || !Transform(target).Anchored || Transform(target).GridUid is not { } grid
            || !TryComp<KiasGridComponent>(grid, out var runtime) || !runtime.Active || runtime.Core is not { } core
            || !TryComp<MapGridComponent>(grid, out var map)) return false;
        var connection = target;
        if (TryComp<KiasIntegratedComponent>(target, out var integrated) && !integrated.Direct)
        {
            if (integrated.Scanner is not { } scanner || TerminatingOrDeleted(scanner) || Transform(scanner).GridUid != grid
                || !TryComp<KiasRoomScannerComponent>(scanner, out var module) || module.LifeStage > ComponentLifeStage.Running
                || (module.Modules & KiasScannerModules.Connector) == 0
                || !_power.IsPowered(scanner) || !Transform(scanner).Anchored) return false;
            if (!_rooms.ContainsTargetForScanner(scanner, target)) return false;
            connection = scanner;
        }
        return runtime.Topology.Connected(_maps.TileIndicesFor(grid, map, Transform(core).Coordinates),
            _maps.TileIndicesFor(grid, map, Transform(connection).Coordinates));
    }

    public IEnumerable<EntityUid> NativeTargets(EntityUid grid) => _nativeTargets.TryGetValue(grid, out var targets)
        ? targets : Array.Empty<EntityUid>();

    private void OnInstall(Entity<KiasIntegrationKitComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || Transform(target).GridUid is not { } grid
            || !_kias.CanConfigure(grid, args.User) || TryComp<KiasIntegratedComponent>(target, out var existing) && existing.Direct
            || !(HasComp<ApcPowerReceiverComponent>(target) || HasComp<Content.Shared.Radio.Components.RadioJammerComponent>(target))
            || HasComp<KiasDeviceComponent>(target) && !HasComp<KiasIntegratedComponent>(target)
            || !Transform(target).Anchored) return;
        Integrate(target, null);
        args.Handled = true;
        QueueDel(ent);
    }

    private void Integrate(EntityUid target, EntityUid? scanner)
    {
        if (TryComp<KiasIntegratedComponent>(target, out var existing)
            && existing.Scanner == scanner && existing.Direct == (scanner == null)) return;
        var integrated = EnsureComp<KiasIntegratedComponent>(target);
        integrated.Scanner = scanner;
        integrated.Direct = scanner == null;
        Track((target, integrated));
        EnsureComp<KiasDeviceComponent>(target).Role = KiasDeviceRole.Adapter;
        if (HasComp<Content.Shared.Light.Components.PoweredLightComponent>(target))
            EnsureComp<KiasLightFixtureComponent>(target);
        _links.EnsureSinkPorts(target, "On", "Off");
        _links.EnsureSourcePorts(target, "Status");
        if (Transform(target).GridUid is { } grid) _kias.Invalidate(grid);
    }

    public void Reconcile(EntityUid grid)
    {
        if (!TryComp<KiasGridComponent>(grid, out var runtime) || !TryComp<MapGridComponent>(grid, out var map)) return;
        if (runtime.Active && (runtime.Core is not { } core || !_kias.IsOnline(core))) return;
        var candidates = _candidates;
        candidates.Clear();
        if (_nativeTargets.TryGetValue(grid, out var targets))
        foreach (var target in targets)
        {
            if (TerminatingOrDeleted(target) || !Transform(target).Anchored || Transform(target).GridUid != grid
                || TryComp<KiasIntegratedComponent>(target, out var integrated) && integrated.Direct
                || HasComp<KiasDeviceComponent>(target) && !HasComp<KiasIntegratedComponent>(target)) continue;
            EntityUid? owner = null;
            foreach (var scanner in runtime.Online)
            {
                if (!_rooms.Contains(scanner, target, KiasScannerModules.Connector)) continue;
                if (owner == null || scanner.Id < owner.Value.Id) owner = scanner;
            }
            if (owner is { } selected) candidates.Add(target, selected);
        }
        foreach (var (target, scanner) in candidates) Integrate(target, scanner);
        if (!_byGrid.TryGetValue(grid, out var assigned)) return;
        foreach (var target in assigned.ToArray())
        {
            if (candidates.ContainsKey(target) || TerminatingOrDeleted(target)
                || !TryComp<KiasIntegratedComponent>(target, out var integrated) || integrated.Direct || integrated.Scanner == null) continue;
            integrated.Scanner = null;
            _kias.Invalidate(grid);
        }
    }

    private void OnSignal(Entity<KiasIntegratedComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port is not ("On" or "Off") || !CanControl(ent)
            || Comp<KiasGridComponent>(Transform(ent).GridUid!.Value).Testing) return;
        if (args.Trigger is { } source && (Transform(source).GridUid != Transform(ent).GridUid || !_kias.IsOnline(source))) return;
        var enabled = args.Port == "On";
        if (HasComp<Content.Shared.Radio.Components.RadioJammerComponent>(ent))
        {
            if (!_kias.HasRole(Transform(ent).GridUid!.Value, KiasDeviceRole.Defence)) return;
            enabled = EntityManager.System<Content.Server.Radio.EntitySystems.JammerSystem>().SetEnabled(ent, enabled);
        }
        _power.SetPowerDisabled(ent, !enabled);
        _links.SendSignal(ent, "Status", enabled);
    }
}
