using System.Linq;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared._Forge.KIAS;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.KIAS.Controllers;

public sealed class KiasControllerUiSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasControllerPhysicalSystem _physical = default!;
    [Dependency] private KiasControllerRuntimeSystem _runtime = default!;
    [Dependency] private KiasControllerIoSystem _io = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    private readonly HashSet<EntityUid> _openProgrammers = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology, after: new[] { typeof(KiasControllerRuntimeSystem) });
        SubscribeLocalEvent<KiasControllerProgrammerComponent, BoundUIOpenedEvent>(OnOpen);
        SubscribeLocalEvent<KiasControllerProgrammerComponent, BoundUIClosedEvent>(OnClose);
        SubscribeLocalEvent<KiasControllerProgrammerComponent, KiasControllerEditMessage>(OnEdit);
        SubscribeLocalEvent<KiasControllerRackComponent, BoundUIOpenedEvent>(OnRackOpen);
        SubscribeLocalEvent<KiasControllerRackComponent, KiasControllerRackMessage>(OnRackMessage);
    }

    private bool Access(EntityUid machine, EntityUid actor, Enum key) => _ui.IsUiOpen(machine, key, actor)
        && _interaction.InRangeUnobstructed(actor, machine) && _physical.CanConfigure(machine, actor);

    private void OnOpen(Entity<KiasControllerProgrammerComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not KiasControllerUiKey.Programmer) return;
        if (!Access(ent, args.Actor, args.UiKey) || ent.Comp.Editor is { } editor && editor != args.Actor
            && _ui.IsUiOpen(ent.Owner, args.UiKey, editor))
        {
            _ui.CloseUi(ent.Owner, args.UiKey, args.Actor);
            return;
        }
        _openProgrammers.Add(ent.Owner);
        ent.Comp.Editor = args.Actor;
        Refresh(ent);
    }

    private void OnClose(Entity<KiasControllerProgrammerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (ent.Comp.Editor == args.Actor) ent.Comp.Editor = null;
        if (!_ui.IsUiOpen(ent.Owner, KiasControllerUiKey.Programmer)) _openProgrammers.Remove(ent.Owner);
    }

    private void OnTopology(ref KiasTopologyChangedEvent args)
    {
        foreach (var uid in _openProgrammers.ToArray())
        {
            if (TerminatingOrDeleted(uid) || !_ui.IsUiOpen(uid, KiasControllerUiKey.Programmer))
            { _openProgrammers.Remove(uid); continue; }
            if (Transform(uid).GridUid == args.Grid && TryComp<KiasControllerProgrammerComponent>(uid, out var programmer))
                Refresh((uid, programmer));
        }
    }

    public void Refresh(Entity<KiasControllerProgrammerComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, KiasControllerUiKey.Programmer)) return;
        var state = new KiasControllerEditorState
        {
            Revision = ent.Comp.DraftRevision, HasCard = ent.Comp.Card != null,
            Online = _kias.IsOnline(ent), Dirty = ent.Comp.DraftDirty,
            Mapping = ent.Comp.Editor is { } actor && _physical.CanMapEdit(ent, actor),
            Editing = ent.Comp.Editor != null, Errors = ent.Comp.Errors.ToList()
        };
        state.Enabled = ent.Comp.DraftEnabled;
        if (ent.Comp.Draft is { } draft)
        {
            var graph = KiasGraphCompiler.Compile(draft, _io.Schema, _io.SnapshotSchema).Graph;
            state.Name = draft.Name;
            state.Wires = draft.Wires.ConvertAll(wire => wire.Copy());
            foreach (var node in draft.Nodes)
            {
                var ports = KiasGraphCatalog.External(node.Kind)
                    ? KiasGraphCatalog.DevicePorts(_io.Schema(node.Profile) ?? node.PortSnapshot)
                    : KiasGraphCatalog.InternalPorts(node.Kind, node.Config.EnumDomain);
                if (graph != null)
                    ports = ports.Select(port => graph.Ports.GetValueOrDefault(new(node.Id, port.Id), port)).ToList();
                state.Nodes.Add(new KiasGraphNodeView
                {
                    Id = node.Id, Kind = node.Kind, X = node.X, Y = node.Y, Profile = node.Profile,
                    Binding = node.Binding is { } binding && !TerminatingOrDeleted(binding) ? GetNetEntity(binding) : null,
                    DeviceName = node.DeviceName, Room = node.Room, Group = node.Group,
                    Config = node.Config.Copy(), Ports = ports,
                    Matched = Transform(ent).GridUid is { } grid && KiasGraphCatalog.External(node.Kind) ? _io.Match(grid, node).Count : 0
                });
            }
        }
        foreach (var profile in _prototypes.EnumeratePrototypes<KiasControllerProfilePrototype>().OrderBy(profile => profile.ID))
            state.Profiles.Add(new() { Id = profile.ID, Ports = profile.Ports.ConvertAll(port => port.Copy()) });
        if (Transform(ent).GridUid is { } deviceGrid)
        {
            foreach (var profile in _io.LinkProfiles(deviceGrid))
                if (_io.Schema(profile) is { } schema) state.Profiles.Add(new() { Id = profile, Ports = schema.Select(port => port.Copy()).ToList() });
            if (TryComp<KiasGridComponent>(deviceGrid, out var gridState) && gridState.Core is { } core
                && TryComp<KiasProtocolComponent>(core, out var legacy))
                state.Legacy = legacy.Protocols.Take(32).Select(record => record.PresetId.Length > 0 ? record.PresetId : record.Trigger.ToString()).ToList();
        }
        state.Presets = _prototypes.EnumeratePrototypes<KiasControllerProgramPrototype>().Select(preset => preset.ID).Order().ToList();
        if (state.Mapping && Transform(ent).GridUid is { } mappingGrid && TryComp<MapGridComponent>(mappingGrid, out var mapGrid))
        {
            var devices = new HashSet<EntityUid>();
            foreach (var uid in _maps.GetLocalAnchoredEntities(mappingGrid, mapGrid, mapGrid.LocalAABB))
            {
                if (devices.Count >= 4096) break;
                if (!TerminatingOrDeleted(uid) && HasComp<KiasDeviceComponent>(uid) && Transform(uid).GridUid == mappingGrid) devices.Add(uid);
            }
            var identities = EntityManager.System<KiasDeviceIdentitySystem>();
            state.Identifiers = devices.Select(identities.Identifier).ToList();
            var rooms = identities.Rooms(mappingGrid, devices);
            foreach (var uid in devices.OrderBy(uid => uid.Id))
                foreach (var profile in _io.MappingProfiles(uid))
                {
                    if (state.Profiles.All(existing => existing.Id != profile) && _io.Schema(profile) is { } schema)
                        state.Profiles.Add(new() { Id = profile, Ports = schema.Select(port => port.Copy()).ToList() });
                    state.Devices.Add(new() { Entity = GetNetEntity(uid), Name = Name(uid), Profile = profile,
                        Identifier = identities.Identifier(uid), Room = rooms.GetValueOrDefault(uid).Label ?? string.Empty,
                        NamedRoom = rooms.GetValueOrDefault(uid).Named, RoomOrder = rooms.GetValueOrDefault(uid).Order });
                }
        }
        if (state.Online && !state.Mapping && Transform(ent).GridUid is { } current)
        {
            var identities = EntityManager.System<KiasDeviceIdentitySystem>();
            if (TryComp<KiasGridComponent>(current, out var inventory))
                state.Identifiers = inventory.Devices.Where(uid => !TerminatingOrDeleted(uid)).Select(identities.Identifier).ToList();
            var rooms = identities.Rooms(current, state.Profiles.SelectMany(profile => _io.Devices(current, profile.Id)));
            foreach (var profile in state.Profiles)
                foreach (var uid in _io.Devices(current, profile.Id))
                    if (_kias.IsOnline(uid)) state.Devices.Add(new() { Entity = GetNetEntity(uid), Name = Name(uid), Profile = profile.Id,
                        Identifier = identities.Identifier(uid), Room = rooms.GetValueOrDefault(uid).Label ?? string.Empty, NamedRoom = rooms.GetValueOrDefault(uid).Named, RoomOrder = rooms.GetValueOrDefault(uid).Order });
        }
        state.CatalogQuery = ent.Comp.CatalogQuery;
        var devicesMatching = state.Devices.Where(device =>
            $"{device.Room} {device.Name} #{device.Identifier} {device.Profile}".Contains(state.CatalogQuery, StringComparison.OrdinalIgnoreCase)).ToList();
        state.CatalogTotal = devicesMatching.Count;
        state.CatalogPage = ent.Comp.CatalogPage = Math.Clamp(ent.Comp.CatalogPage, 0, Math.Max(0, (devicesMatching.Count - 1) / 256));
        state.Devices = devicesMatching.Skip(state.CatalogPage * 256).Take(256).ToList();
        _ui.SetUiState(ent.Owner, KiasControllerUiKey.Programmer, state);
    }

    private void OnEdit(Entity<KiasControllerProgrammerComponent> ent, ref KiasControllerEditMessage args)
    {
        Edit(ent, args.Actor, args);
    }

    public bool Edit(Entity<KiasControllerProgrammerComponent> ent, EntityUid actor, KiasControllerEditMessage message)
    {
        var accepted = EditDraft(ent, actor, message);
        Refresh(ent);
        return accepted;
    }

    private bool EditDraft(Entity<KiasControllerProgrammerComponent> ent, EntityUid actor, KiasControllerEditMessage message)
    {
        if (!Access(ent, actor, KiasControllerUiKey.Programmer) || ent.Comp.Editor != actor) return false;
        if (message.Edit == KiasGraphEdit.Catalog)
        {
            if (message.Text == null || message.Text.Length > 128 || message.Node < 0) return false;
            ent.Comp.CatalogQuery = message.Text.Trim();
            ent.Comp.CatalogPage = message.Node;
            return true;
        }
        if (message.Edit == KiasGraphEdit.Refresh) { return true; }
        if (message.Revision != ent.Comp.DraftRevision || ent.Comp.Card is not { } card
            || _physical.Card(ent, KiasControllerProgrammerComponent.SlotId) != card
            || !TryComp<KiasControllerCardComponent>(card, out var controller) || ent.Comp.Draft is not { } current)
        { return false; }
        if (message.Edit == KiasGraphEdit.Discard)
        {
            ent.Comp.Draft = controller.Program.Copy(); ent.Comp.DraftDirty = false;
            ent.Comp.DraftEnabled = controller.Enabled;
            ent.Comp.Errors.Clear(); ent.Comp.DraftRevision++; return true;
        }
        if (message.Edit == KiasGraphEdit.Eject)
        {
            var ejected = _slots.TryEject(ent, KiasControllerProgrammerComponent.SlotId, actor, out _);
            return ejected;
        }
        if (!_physical.CanProgram(ent, actor) || !Enum.IsDefined(message.Edit)
            || message.Text == null || message.Profile == null || message.Room == null || message.Group == null
            || message.Text.Length > 256 || message.Profile.Length > 64 || message.Room.Length > 64 || message.Group.Length > 32
            || !float.IsFinite(message.X) || !float.IsFinite(message.Y) || Math.Abs(message.X) > 100000 || Math.Abs(message.Y) > 100000)
            return false;
        if (message.Edit == KiasGraphEdit.Write)
        {
            var compiled = KiasGraphCompiler.Compile(current, _io.Schema, _io.SnapshotSchema);
            ent.Comp.Errors = compiled.Errors;
            if (!compiled.Success) { return false; }
            controller.Program = current.Copy(); controller.Revision++;
            controller.Enabled = ent.Comp.DraftEnabled;
            _physical.SyncCardMetadata(card, controller);
            ent.Comp.DraftDirty = false; ent.Comp.DraftRevision++; return true;
        }
        var draft = current.Copy();
        var node = draft.Nodes.FirstOrDefault(node => node.Id == message.Node);
        switch (message.Edit)
        {
            case KiasGraphEdit.Add:
                if (!Enum.IsDefined(message.Kind) || draft.Nodes.Count >= KiasGraphCompiler.MaxNodes) return false;
                node = new() { Id = draft.Nodes.Count == 0 ? 1 : draft.Nodes.Max(node => node.Id) + 1,
                    Kind = message.Kind, X = message.X, Y = message.Y };
                if (KiasGraphCatalog.External(node.Kind))
                {
                    if (draft.Nodes.Count(node => KiasGraphCatalog.External(node.Kind)) >= KiasGraphCompiler.MaxExternal
                        || _io.Schema(message.Profile) is not { } schema) return false;
                    node.Profile = message.Profile; node.PortSnapshot = schema.Select(port => port.Copy()).ToList();
                    if (node.Kind == KiasNodeKind.Specific)
                    {
                        if (message.Binding is not { } net || !TryGetEntity(net, out var bound) || bound is not { } uid
                            || TerminatingOrDeleted(uid)
                            || Transform(uid).GridUid != Transform(ent).GridUid
                            || (_physical.CanMapEdit(ent, actor)
                                ? !Transform(uid).Anchored || !_io.Supports(uid, node.Profile)
                                : !_kias.IsOnline(uid) || !_io.Profiles(uid).Contains(node.Profile))) return false;
                        node.Binding = uid; node.DeviceName = EntityManager.System<KiasDeviceIdentitySystem>().Label(uid);
                    }
                }
                draft.Nodes.Add(node);
                break;
            case KiasGraphEdit.Move:
                if (node == null) return false;
                node.X = message.X; node.Y = message.Y;
                break;
            case KiasGraphEdit.Configure:
                if (node == null || message.Config == null || message.Config.Text == null
                    || message.Config.Text.Length > 256 || !double.IsFinite(message.Config.Number)
                    || Math.Abs(message.Config.Number) > 1e12 || !double.IsFinite(message.Config.Seconds)
                    || message.Config.Seconds is < KiasGraphCompiler.MinSeconds or > KiasGraphCompiler.MaxSeconds
                    || !Enum.IsDefined(message.Config.Comparison) || !Enum.IsDefined(message.Config.EnumDomain)) return false;
                node.Config = message.Config.Copy();
                node.Room = node.Kind is KiasNodeKind.Any or KiasNodeKind.All ? message.Room : string.Empty;
                node.Group = node.Kind is KiasNodeKind.Any or KiasNodeKind.All ? message.Group : string.Empty;
                break;
            case KiasGraphEdit.Remove:
                if (node == null) return false;
                draft.Nodes.Remove(node); draft.Wires.RemoveAll(wire => wire.FromNode == node.Id || wire.ToNode == node.Id);
                break;
            case KiasGraphEdit.Connect:
                if (message.Wire == null || message.Wire.FromPort == null || message.Wire.ToPort == null
                    || message.Wire.FromPort.Length > 64 || message.Wire.ToPort.Length > 64
                    || draft.Wires.Count >= KiasGraphCompiler.MaxWires) return false;
                draft.Wires.Add(message.Wire.Copy());
                var compiled = KiasGraphCompiler.Compile(draft, _io.Schema, _io.SnapshotSchema);
                if (!compiled.Success) { ent.Comp.Errors = compiled.Errors; return false; }
                break;
            case KiasGraphEdit.Disconnect:
                if (message.Wire == null) return false;
                draft.Wires.RemoveAll(wire => wire.FromNode == message.Wire.FromNode && wire.ToNode == message.Wire.ToNode
                    && wire.FromPort == message.Wire.FromPort && wire.ToPort == message.Wire.ToPort);
                break;
            case KiasGraphEdit.Rename:
                if (message.Text.Length is < 1 or > 64) return false;
                draft.Name = message.Text;
                break;
            case KiasGraphEdit.Preset:
                if (!_prototypes.TryIndex<KiasControllerProgramPrototype>(message.Text, out var preset)) return false;
                draft = preset.Program.Copy();
                ent.Comp.DraftEnabled = preset.Enabled;
                break;
            case KiasGraphEdit.Enabled:
                ent.Comp.DraftEnabled = message.Enabled;
                break;
            case KiasGraphEdit.ImportLegacy:
                if (Transform(ent).GridUid is not { } legacyGrid || !TryComp<KiasGridComponent>(legacyGrid, out var gridRuntime)
                    || gridRuntime.Core is not { } source || !TryComp<KiasProtocolComponent>(source, out var records)
                    || message.Node < 0 || message.Node >= Math.Min(32, records.Protocols.Count)) return false;
                var import = KiasLegacyGraphTranslator.Import(records.Protocols[message.Node], _io.Supports, _io.NativeSinkProfile,
                    trigger => Loc.GetString(trigger == KiasTrigger.Boot ? "kias-online" : $"kias-trigger-{trigger.ToString().ToLowerInvariant()}"));
                ent.Comp.Errors = import.Errors;
                if (import.Program is not { } converted) { return false; }
                foreach (var external in converted.Nodes.Where(node => KiasGraphCatalog.External(node.Kind)))
                {
                    if (_io.Schema(external.Profile) is { } schema) external.PortSnapshot = schema.Select(port => port.Copy()).ToList();
                    if (external.Binding is { } binding && !TerminatingOrDeleted(binding)) external.DeviceName = Name(binding);
                }
                draft = converted; ent.Comp.DraftEnabled = import.Enabled;
                break;
            default: return false;
        }
        ent.Comp.Draft = draft; ent.Comp.DraftDirty = true; ent.Comp.DraftRevision++;
        ent.Comp.Errors = KiasGraphCompiler.Compile(draft, _io.Schema, _io.SnapshotSchema).Errors;
        return true;
    }

    private void OnRackOpen(Entity<KiasControllerRackComponent> ent, ref BoundUIOpenedEvent args) => RefreshRack(ent);
    public void RefreshRack(Entity<KiasControllerRackComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, KiasControllerUiKey.Rack)) return;
        var state = new KiasControllerRackState { Online = _kias.IsOnline(ent), Load = ent.Comp.CurrentLoad, Running = _runtime.RunningCount(ent) };
        for (var i = 0; i < KiasControllerRackComponent.SlotCount; i++)
        {
            var row = new KiasControllerRackSlotView();
            if (_physical.Card(ent, KiasControllerRackComponent.SlotId(i)) is { } card && TryComp<KiasControllerCardComponent>(card, out var controller))
            {
                row.Inserted = true; row.Name = controller.Program.Name; row.Enabled = controller.Enabled;
                row.Status = _runtime.Status(card); row.Fault = _runtime.Fault(card);
            }
            state.Slots.Add(row);
        }
        _ui.SetUiState(ent.Owner, KiasControllerUiKey.Rack, state);
    }
    private void OnRackMessage(Entity<KiasControllerRackComponent> ent, ref KiasControllerRackMessage args)
    {
        if (!Access(ent, args.Actor, KiasControllerUiKey.Rack)) return;
        if (!args.Refresh && args.Slot is >= 0 and < KiasControllerRackComponent.SlotCount
            && _physical.Card(ent, KiasControllerRackComponent.SlotId(args.Slot)) is { } card
            && TryComp<KiasControllerCardComponent>(card, out var controller))
        {
            if (args.Eject) _slots.TryEject(ent, KiasControllerRackComponent.SlotId(args.Slot), args.Actor, out _);
            else { controller.Enabled = args.Enabled; _runtime.ContainerChanged(ent); }
        }
        RefreshRack(ent);
    }
}
