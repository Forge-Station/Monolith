using System.Linq;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Robust.Shared.Prototypes;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using System.Security.Cryptography;
using System.Text;
using Robust.Shared.Timing;

namespace Content.Server._Forge.KIAS.Controllers;

public sealed partial class KiasControllerIoSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    private readonly Dictionary<(EntityUid Grid, string Profile), List<EntityUid>> _devices = new();
    private readonly Dictionary<EntityUid, List<string>> _profiles = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _grids = new();
    private readonly Dictionary<EntityUid, EntityUid> _profileGrids = new();
    private readonly Dictionary<EntityUid, HashSet<string>> _gridProfiles = new();
    private readonly Dictionary<string, List<KiasGraphPort>> _linkSchemas = new();
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<EntityUid, Dictionary<(string Profile, string Port), (KiasGraphValue Value, TimeSpan At)>> _outputs = new();
    public event Action<EntityUid, string, string, KiasGraphValue>? Emitted;
    public event Action<EntityUid, EntityUid, EntityUid, string, string>? CommandDispatched;

    public bool TryOutput(EntityUid device, string profile, string port, out KiasGraphValue value, out TimeSpan at)
    {
        value = default; at = default;
        if (TerminatingOrDeleted(device) || !_kias.IsOnline(device) || !_outputs.TryGetValue(device, out var outputs)
            || !outputs.TryGetValue((profile, port), out var snapshot)) return false;
        value = snapshot.Value; at = snapshot.At; return true;
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<KiasDeviceComponent, DeviceLinkPortInvokedEvent>(OnPortInvoked);
        InitializeSensors();
    }

    public IReadOnlyList<KiasGraphPort>? Schema(string profile) =>
        _prototypes.TryIndex<KiasControllerProfilePrototype>(profile, out var definition) ? definition.Ports : _linkSchemas.GetValueOrDefault(profile);

    public IReadOnlyList<string> LinkProfiles(EntityUid grid) => _gridProfiles.TryGetValue(grid, out var profiles)
        ? profiles.Where(profile => profile.StartsWith("Link.", StringComparison.Ordinal)).Order().ToArray() : Array.Empty<string>();
    public bool Supports(EntityUid uid, string profile) => !TerminatingOrDeleted(uid)
        && (_prototypes.TryIndex<KiasControllerProfilePrototype>(profile, out var definition)
            ? HasComp(uid, Factory.GetRegistration(definition.Component).Type)
            : NativeSchema(uid) is { } ports && LinkId(ports) == profile);
    public string? NativeSinkProfile(EntityUid uid, string port)
    {
        if (TerminatingOrDeleted(uid) || !TryComp<DeviceLinkSinkComponent>(uid, out var sink) || !sink.Ports.Any(candidate => candidate == port)
            || NativeSchema(uid) is not { } ports) return null;
        var profile = LinkId(ports);
        if (_linkSchemas.Count >= 4096 && !_linkSchemas.ContainsKey(profile)) return null;
        _linkSchemas[profile] = ports; return profile;
    }
    private List<KiasGraphPort>? NativeSchema(EntityUid uid)
    {
        var ports = new List<KiasGraphPort>();
        if (TryComp<DeviceLinkSourceComponent>(uid, out var source))
            foreach (var port in source.Ports)
                if (LinkPort(port, KiasPortDirection.Output) is { } output) ports.Add(output);
        if (TryComp<DeviceLinkSinkComponent>(uid, out var sink))
            foreach (var port in sink.Ports)
                if (LinkPort(port, KiasPortDirection.Input) is { } input) ports.Add(input);
        return ports.Count is > 0 and <= 60 ? ports : null;
    }
    private static string LinkId(IEnumerable<KiasGraphPort> ports) => "Link." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("|", ports.OrderBy(port => port.Id).Select(port => port.Id)))))[..32];

    private KiasGraphPort? LinkPort(string id, KiasPortDirection direction)
    {
        if (id.Length is < 1 or > 60) return null;
        DevicePortPrototype definition;
        if (direction == KiasPortDirection.Output && _prototypes.TryIndex<SourcePortPrototype>(id, out var source)) definition = source;
        else if (direction == KiasPortDirection.Input && _prototypes.TryIndex<SinkPortPrototype>(id, out var sink)) definition = sink;
        else return null;
        return new() { Id = (direction == KiasPortDirection.Output ? "out:" : "in:") + id,
            Type = KiasPortType.Signal, Direction = direction, Name = definition.Name, Description = definition.Description };
    }

    public IReadOnlyList<KiasGraphPort>? SnapshotSchema(KiasControllerNode node)
    {
        if (!node.Profile.StartsWith("Link.", StringComparison.Ordinal) || node.PortSnapshot.Count is < 1 or > 60) return null;
        var ports = new List<KiasGraphPort>();
        var ids = new HashSet<string>();
        foreach (var saved in node.PortSnapshot)
        {
            var prefix = saved.Direction == KiasPortDirection.Output ? "out:" : "in:";
            if (saved.Type != KiasPortType.Signal || !Enum.IsDefined(saved.Direction) || !saved.Id.StartsWith(prefix, StringComparison.Ordinal)
                || !ids.Add(saved.Id) || LinkPort(saved.Id[prefix.Length..], saved.Direction) is not { } port) return null;
            ports.Add(port);
        }
        return LinkId(ports) == node.Profile ? ports : null;
    }

    private void OnPortInvoked(Entity<KiasDeviceComponent> ent, ref DeviceLinkPortInvokedEvent args)
    {
        foreach (var profile in Profiles(ent))
            if (profile.StartsWith("Link.", StringComparison.Ordinal)) Emit(ent, profile, "out:" + args.Port, KiasGraphValue.Pulse);
    }

    public IReadOnlyList<string> Profiles(EntityUid device) => _profiles.TryGetValue(device, out var profiles) ? profiles : Array.Empty<string>();
    public IReadOnlyList<string> MappingProfiles(EntityUid device)
    {
        var profiles = _prototypes.EnumeratePrototypes<KiasControllerProfilePrototype>().Where(profile => Supports(device, profile.ID))
            .Select(profile => profile.ID).ToList();
        if (NativeSchema(device) is { } ports)
        {
            var id = LinkId(ports);
            if (_linkSchemas.Count < 4096 || _linkSchemas.ContainsKey(id))
            { _linkSchemas[id] = ports; profiles.Add(id); }
        }
        return profiles;
    }
    public IReadOnlyList<EntityUid> Devices(EntityUid grid, string profile) =>
        _devices.TryGetValue((grid, profile), out var devices) ? devices : Array.Empty<EntityUid>();

    public List<EntityUid> Match(EntityUid grid, KiasControllerNode node)
    {
        var result = new List<EntityUid>();
        foreach (var uid in Devices(grid, node.Profile))
        {
            if (node.Kind == KiasNodeKind.Specific && node.Binding != uid || !_kias.IsOnline(uid)) continue;
            if (node.Kind != KiasNodeKind.Specific && node.Room.Length > 0 && Comp<KiasDeviceComponent>(uid).Room != node.Room) continue;
            if (node.Kind != KiasNodeKind.Specific && node.Group.Length > 0)
            {
                var group = TryComp<KiasSpeakerComponent>(uid, out var speaker) ? speaker.Group
                    : TryComp<KiasLightControllerComponent>(uid, out var light) ? light.Group
                    : TryComp<KiasLightGroupComponent>(uid, out var fixture) ? fixture.Group : string.Empty;
                if (group != node.Group) continue;
            }
            result.Add(uid);
        }
        return result;
    }

    private void Clear(EntityUid grid)
    {
        if (!_grids.Remove(grid, out var previous)) return;
        if (_gridProfiles.Remove(grid, out var keys))
            foreach (var key in keys) _devices.Remove((grid, key));
        foreach (var uid in previous)
        {
            if (_profileGrids.GetValueOrDefault(uid) != grid) continue;
            _outputs.Remove(uid);
            _profiles.Remove(uid);
            _profileGrids.Remove(uid);
        }
    }
    private void OnGridRemoved(GridRemovalEvent args) => Clear(args.EntityUid);
    private void OnTopology(ref KiasTopologyChangedEvent args)
    {
        var grid = args.Grid;
        Dictionary<EntityUid, Dictionary<(string Profile, string Port), (KiasGraphValue Value, TimeSpan At)>> previous = _grids.TryGetValue(grid, out var devicesBefore)
            ? devicesBefore.Where(uid => _profileGrids.GetValueOrDefault(uid) == grid && _outputs.ContainsKey(uid))
                .ToDictionary(uid => uid, uid => _outputs[uid]) : new();
        Clear(args.Grid);
        if (!TryComp<KiasGridComponent>(args.Grid, out var runtime)) return;
        var indexed = new HashSet<EntityUid>();
        var keys = new HashSet<string>();
        foreach (var uid in runtime.Online.OrderBy(uid => uid.Id))
        {
            if (!_kias.IsOnline(uid)) continue;
            var profiles = new List<string>();
            foreach (var profile in _prototypes.EnumeratePrototypes<KiasControllerProfilePrototype>())
            {
                if (!HasComp(uid, Factory.GetRegistration(profile.Component).Type)) continue;
                profiles.Add(profile.ID);
                keys.Add(profile.ID);
                if (!_devices.TryGetValue((args.Grid, profile.ID), out var devices)) _devices[(args.Grid, profile.ID)] = devices = new();
                devices.Add(uid);
            }
            if (NativeSchema(uid) is { } linked)
            {
                var id = LinkId(linked);
                if (_linkSchemas.Count < 4096 || _linkSchemas.ContainsKey(id))
                {
                    _linkSchemas[id] = linked; profiles.Add(id); keys.Add(id);
                    if (!_devices.TryGetValue((args.Grid, id), out var devices)) _devices[(args.Grid, id)] = devices = new();
                    devices.Add(uid);
                }
            }
            if (!previous.ContainsKey(uid)) _outputs.Remove(uid);
            _profiles[uid] = profiles;
            _profileGrids[uid] = args.Grid;
            indexed.Add(uid);
            if (previous.TryGetValue(uid, out var saved))
            {
                foreach (var key in saved.Keys.ToArray())
                    if (!profiles.Contains(key.Profile) || saved[key].Value.Type == KiasPortType.Signal
                        || Schema(key.Profile)?.Any(port => port.Id == key.Port && port.Direction == KiasPortDirection.Output
                            && port.Type == saved[key].Value.Type) != true)
                        saved.Remove(key);
                if (saved.Count > 0) _outputs[uid] = saved;
            }
        }
        _grids[args.Grid] = indexed;
        _gridProfiles[args.Grid] = keys;
    }

    public void EmitChanged(EntityUid device, string profile, string port, KiasGraphValue value)
    {
        if (!_kias.IsOnline(device)) return;
        if (_outputs.TryGetValue(device, out var outputs) && outputs.TryGetValue((profile, port), out var previous)
            && previous.Value.Equals(value))
        {
            outputs[(profile, port)] = (value, _timing.CurTime);
            return;
        }
        Emit(device, profile, port, value);
    }

    public void Emit(EntityUid device, string profile, string port, KiasGraphValue value)
    {
        using var phase = new KiasPhaseMeasurement(_kias, KiasPhase.IoEmit);
        if (value.Type == KiasPortType.Number && !double.IsFinite(value.Number)) return;
        if (value.Type == KiasPortType.String)
        {
            var text = value.Text ?? string.Empty;
            value = KiasGraphValue.String(text[..Math.Min(text.Length, 256)]);
        }
        if (!_kias.IsOnline(device) || !Profiles(device).Contains(profile)
            || Schema(profile)?.Any(item => item.Id == port && item.Direction == KiasPortDirection.Output && item.Type == value.Type) != true) return;
        if (!_outputs.TryGetValue(device, out var outputs)) _outputs[device] = outputs = new();
        outputs[(profile, port)] = (value, _timing.CurTime);
        Emitted?.Invoke(device, profile, port, value);
    }
}
