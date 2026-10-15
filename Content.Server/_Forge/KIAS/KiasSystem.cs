using System.Linq;
using Content.Shared._Forge.KIAS;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Verbs;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.DeviceLinking;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._Forge.KIAS;

public sealed partial class KiasSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    private readonly HashSet<EntityUid> _active = new();
    private readonly HashSet<EntityUid> _dirty = new();
    private readonly Queue<EntityUid> _dirtyQueue = new();

    public IReadOnlySet<EntityUid> ActiveGrids => _active;
    public bool MeasureUpdates;
    public readonly KiasPhaseMetrics[] Phases = Enum.GetValues<KiasPhase>().Select(_ => new KiasPhaseMetrics()).ToArray();
    public double MeasuredUpdateMilliseconds;
    public long MeasuredUpdateBytes;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KiasDeviceComponent, ComponentStartup>(OnDeviceStartup);
        SubscribeLocalEvent<KiasDeviceComponent, ComponentShutdown>(OnDeviceShutdown);
        SubscribeLocalEvent<KiasDeviceComponent, EntParentChangedMessage>(OnDeviceParent);
        SubscribeLocalEvent<KiasDeviceComponent, GridUidChangedEvent>(OnDeviceGrid);
        SubscribeLocalEvent<KiasDeviceComponent, AnchorStateChangedEvent>(OnDeviceAnchor);
        SubscribeLocalEvent<KiasDeviceComponent, PowerChangedEvent>(OnDevicePower);
        SubscribeLocalEvent<KiasDeviceComponent, MoveEvent>(OnDeviceMove);
        SubscribeLocalEvent<KiasDeviceComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<KiasCoreComponent, GetVerbsEvent<AlternativeVerb>>(OnCoreVerbs);
        SubscribeLocalEvent<KiasCoreComponent, InteractUsingEvent>(OnMasterKey);
        SubscribeLocalEvent<KiasDataCableComponent, ComponentStartup>(OnCableStartup);
        SubscribeLocalEvent<KiasDataCableComponent, ComponentShutdown>(OnCableShutdown);
        SubscribeLocalEvent<KiasDataCableComponent, EntParentChangedMessage>(OnCableParent);
        SubscribeLocalEvent<KiasDataCableComponent, GridUidChangedEvent>(OnCableGrid);
        SubscribeLocalEvent<KiasDataCableComponent, AnchorStateChangedEvent>(OnCableAnchor);
        SubscribeLocalEvent<KiasDataCableComponent, MoveEvent>(OnCableMove);
        SubscribeLocalEvent<GridSplitEvent>(OnSplit);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
    }

    public override void Update(float frameTime)
    {
        using var measurement = new KiasUpdateMeasurement(this);
        base.Update(frameTime);
        if (_dirtyQueue.Count == 0)
            return;
        const int gridBudget = 8;
        var processed = 0;
        for (var examined = 0; examined < 128 && processed < gridBudget && _dirtyQueue.TryDequeue(out var grid); examined++)
        {
            if (!_dirty.Remove(grid)) continue;
            processed++;
            Rebuild(grid);
        }
    }

    private void OnDeviceStartup(Entity<KiasDeviceComponent> ent, ref ComponentStartup args)
    {
        EntityManager.System<KiasDeviceIdentitySystem>().Identifier(ent);
        RegisterDevice(ent);
    }
    private void OnDeviceShutdown(Entity<KiasDeviceComponent> ent, ref ComponentShutdown args)
    {
        EntityManager.System<KiasDeviceIdentitySystem>().Release(ent);
        RemoveDevice(ent);
    }
    private void OnDeviceParent(Entity<KiasDeviceComponent> ent, ref EntParentChangedMessage args) => RegisterDevice(ent);
    private void OnDeviceGrid(Entity<KiasDeviceComponent> ent, ref GridUidChangedEvent args) => RegisterDevice(ent);
    private void OnDeviceAnchor(Entity<KiasDeviceComponent> ent, ref AnchorStateChangedEvent args) => RegisterDevice(ent);
    private void OnDevicePower(Entity<KiasDeviceComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered && ent.Comp.Status == KiasDeviceStatus.Online && ent.Comp.RegisteredGrid is { } grid && _active.Contains(grid))
        {
            var message = Loc.GetString("kias-device-power-lost", ("device", Name(ent)));
            EntityManager.System<KiasSafetySystem>().Publish(grid, message, true, announce: false, key: $"power-lost:{ent.Owner}");
            EntityManager.System<KiasProtocolSystem>().Trigger(grid, KiasTrigger.PowerLost, message: message, eventKey: ent.Owner.ToString());
        }
        MarkDirty(ent.Comp.RegisteredGrid);
    }
    private void OnDeviceMove(Entity<KiasDeviceComponent> ent, ref MoveEvent args)
    {
        if (!args.OnlyRotation)
            MarkDirty(ent.Comp.RegisteredGrid);
    }
    private void OnCableStartup(Entity<KiasDataCableComponent> ent, ref ComponentStartup args) => RegisterCable(ent);
    private void OnCableShutdown(Entity<KiasDataCableComponent> ent, ref ComponentShutdown args) => RemoveCable(ent);
    private void OnCableParent(Entity<KiasDataCableComponent> ent, ref EntParentChangedMessage args) => RegisterCable(ent);
    private void OnCableGrid(Entity<KiasDataCableComponent> ent, ref GridUidChangedEvent args) => RegisterCable(ent);
    private void OnCableAnchor(Entity<KiasDataCableComponent> ent, ref AnchorStateChangedEvent args) => RegisterCable(ent);
    private void OnCableMove(Entity<KiasDataCableComponent> ent, ref MoveEvent args)
    {
        if (!args.OnlyRotation)
            MarkDirty(ent.Comp.RegisteredGrid);
    }

    private void RegisterDevice(Entity<KiasDeviceComponent> ent)
    {
        _metadata.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
        RemoveDevice(ent);
        var xform = Transform(ent);
        if (TerminatingOrDeleted(ent) || !xform.Anchored || xform.GridUid is not { } grid || !HasComp<MapGridComponent>(grid))
            return;
        ent.Comp.RegisteredGrid = grid;
        EnsureComp<KiasGridComponent>(grid).Devices.Add(ent);
        MarkDirty(grid);
    }

    private void RemoveDevice(Entity<KiasDeviceComponent> ent)
    {
        if (ent.Comp.RegisteredGrid is { } grid && TryComp<KiasGridComponent>(grid, out var runtime))
        {
            runtime.Devices.Remove(ent);
            runtime.Online.Remove(ent);
            if (TerminatingOrDeleted(ent) && HasComp<DeviceLinkSourceComponent>(ent))
            {
                foreach (var device in runtime.Devices)
                {
                    if (TryComp<KiasSpeakerComponent>(device, out var speaker))
                        speaker.Links.RemoveAll(link => link.Source == ent.Owner);
                }
            }
            MarkDirty(grid);
        }
        ent.Comp.RegisteredGrid = null;
        ent.Comp.Status = KiasDeviceStatus.Disconnected;
        _appearance.SetData(ent.Owner, KiasVisuals.Status, ent.Comp.Status);
    }

    private void RegisterCable(Entity<KiasDataCableComponent> ent)
    {
        _metadata.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
        RemoveCable(ent);
        var xform = Transform(ent);
        if (TerminatingOrDeleted(ent) || !xform.Anchored || xform.GridUid is not { } grid || !HasComp<MapGridComponent>(grid))
            return;
        ent.Comp.RegisteredGrid = grid;
        EnsureComp<KiasGridComponent>(grid).Cables.Add(ent);
        MarkDirty(grid);
    }

    private void RemoveCable(Entity<KiasDataCableComponent> ent)
    {
        if (ent.Comp.RegisteredGrid is { } grid && TryComp<KiasGridComponent>(grid, out var runtime))
        {
            runtime.Cables.Remove(ent);
            MarkDirty(grid);
        }
        ent.Comp.RegisteredGrid = null;
    }

    private void MarkDirty(EntityUid? grid)
    {
        if (grid is not { } uid || TerminatingOrDeleted(uid) || !TryComp<KiasGridComponent>(uid, out var runtime))
            return;
        if (runtime.Core is { } core && TryComp<KiasCoreComponent>(core, out var component) && !component.Enabled)
            return;
        if (_dirty.Add(uid)) _dirtyQueue.Enqueue(uid);
    }

    public void Invalidate(EntityUid grid) => MarkDirty(grid);

    public void Rebuild(EntityUid grid)
    {
        _dirty.Remove(grid);
        if (TerminatingOrDeleted(grid) || !TryComp<KiasGridComponent>(grid, out var runtime) || !TryComp<MapGridComponent>(grid, out var map))
            return;
        var wasActive = runtime.Active;
        UpdateCableVisuals(grid, map, runtime);
        var cores = runtime.Devices.Where(uid => !TerminatingOrDeleted(uid) && HasComp<KiasCoreComponent>(uid)).ToArray();
        runtime.Core = cores.Length == 1 ? cores[0] : null;
        runtime.Active = runtime.Core is { } core && Comp<KiasCoreComponent>(core).Enabled && _power.IsPowered(core);
        runtime.Online.Clear();
        runtime.Roles.Clear();
        if (runtime.Active)
        {
            _active.Add(grid);
            var relays = EntityManager.System<KiasRelaySystem>();
            runtime.Topology.Rebuild(runtime.Cables.Where(uid => !TerminatingOrDeleted(uid)
                && !relays.IsCableBlocked(uid)).Select(uid => _map.TileIndicesFor(grid, map, Transform(uid).Coordinates)));
        }
        else
        {
            _active.Remove(grid);
            runtime.Entities = 0;
            runtime.Crew = 0;
            runtime.CrewDetails = string.Empty;
        }

        foreach (var uid in runtime.Devices)
        {
            if (!TryComp<KiasDeviceComponent>(uid, out var device) || TerminatingOrDeleted(uid))
                continue;
            device.Status = cores.Length > 1 ? KiasDeviceStatus.DuplicateCore
                : !HasComp<KiasIntegratedComponent>(uid) && !_power.IsPowered(uid) ? KiasDeviceStatus.NoPower
                : !runtime.Active ? KiasDeviceStatus.Offline
                : (HasComp<KiasIntegratedComponent>(uid) ? EntityManager.System<KiasIntegrationSystem>().CanControl(uid)
                    : runtime.Topology.Connected(_map.TileIndicesFor(grid, map, Transform(runtime.Core!.Value).Coordinates),
                    _map.TileIndicesFor(grid, map, Transform(uid).Coordinates))) ? KiasDeviceStatus.Online
                : KiasDeviceStatus.NoDataPath;
            if (device.Status == KiasDeviceStatus.Online)
            {
                runtime.Online.Add(uid);
                runtime.Roles.Add(device.Role);
            }
            _appearance.SetData(uid, KiasVisuals.Status, device.Status);
        }
        runtime.Revision++;
        if (wasActive != runtime.Active)
        {
            var availability = new KiasAvailabilityChangedEvent(grid, runtime.Active);
            RaiseLocalEvent(grid, ref availability, true);
        }
        var topology = new KiasTopologyChangedEvent(grid, runtime.Revision);
        RaiseLocalEvent(grid, ref topology, true);
    }

    public bool TopologyPending(EntityUid grid) => _dirty.Contains(grid);

    public bool IsOnline(EntityUid device)
    {
        return !TerminatingOrDeleted(device) && TryComp<KiasDeviceComponent>(device, out var comp) && comp.RegisteredGrid is { } grid
               && !_dirty.Contains(grid) && _active.Contains(grid) && comp.Status == KiasDeviceStatus.Online
               && (HasComp<KiasIntegratedComponent>(device)
                   ? EntityManager.System<KiasIntegrationSystem>().CanControl(device) : _power.IsPowered(device));
    }

    public bool CanConfigure(EntityUid grid, EntityUid actor)
    {
        return EntityManager.System<KiasAccessSystem>().CanConfigure(grid, actor);
    }

    public bool HasRole(EntityUid grid, KiasDeviceRole role) => !_dirty.Contains(grid) && _active.Contains(grid) && TryComp<KiasGridComponent>(grid, out var runtime) && runtime.Roles.Contains(role);

    public void SetEnabled(EntityUid core, bool enabled)
    {
        if (!TryComp<KiasCoreComponent>(core, out var comp) || comp.Enabled == enabled)
            return;
        var grid = Transform(core).GridUid;
        if (!enabled && grid is { } shuttingDown && _active.Contains(shuttingDown))
        {
            EntityManager.System<KiasSafetySystem>().Publish(shuttingDown, Loc.GetString("kias-shutdown"), announce: false);
            EntityManager.System<Controllers.KiasControllerRuntimeSystem>().FinishBeforeShutdown(shuttingDown,
                () => EntityManager.System<KiasProtocolSystem>().Trigger(shuttingDown, KiasTrigger.Shutdown, message: Loc.GetString("kias-shutdown")));
        }
        comp.Enabled = enabled;
        if (grid is not { } uid)
            return;
        _dirty.Remove(uid);
        Rebuild(uid);
        if (enabled && _active.Contains(uid))
        {
            EntityManager.System<KiasSafetySystem>().Publish(uid, Loc.GetString("kias-online"), announce: false);
            EntityManager.System<KiasProtocolSystem>().Trigger(uid, KiasTrigger.Boot, message: Loc.GetString("kias-online"));
        }
    }

    private void OnCoreVerbs(Entity<KiasCoreComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || Transform(ent).GridUid is not { } grid || !CanConfigure(grid, args.User))
            return;
        var core = ent.Owner;
        var actor = args.User;
        var enabled = !ent.Comp.Enabled;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(enabled ? "kias-enable" : "kias-disable"),
            Act = () =>
            {
                if (Transform(core).GridUid == grid && CanConfigure(grid, actor))
                    SetEnabled(core, enabled);
            },
        });
    }

    private void OnMasterKey(Entity<KiasCoreComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<KiasMasterKeyComponent>(args.Used))
            return;
        args.Handled = true;
        if (Transform(ent).GridUid is not { } grid || !CanConfigure(grid, args.User))
        {
            _popup.PopupEntity(Loc.GetString("kias-owner-only"), ent, args.User);
            return;
        }
        SetEnabled(ent, !ent.Comp.Enabled);
    }

    private void OnExamine(Entity<KiasDeviceComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("kias-device-status", ("status", Loc.GetString($"kias-status-{ent.Comp.Status.ToString().ToLowerInvariant()}"))));
    }

    private void OnSplit(ref GridSplitEvent args)
    {
        MarkDirty(args.Grid);
        foreach (var grid in args.NewGrids)
            MarkDirty(grid);
    }

    private void OnGridRemoval(GridRemovalEvent args)
    {
        _dirty.Remove(args.EntityUid);
        _active.Remove(args.EntityUid);
    }
}
