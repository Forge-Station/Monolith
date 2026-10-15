using System.Linq;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Shared._Forge.KIAS;
using Content.Shared.Anomaly.Components;
using Content.Shared.Atmos.Monitor;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Content.Shared.Pinpointer;

namespace Content.Server._Forge.KIAS;

public sealed class KiasSafetySystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasCrewSystem _crew = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private KiasDisplaySystem _display = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly KiasEmissionGate _logGate = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _pendingGrowth = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<AnomalyStabilityChangedEvent>(OnAnomaly);
        SubscribeLocalEvent<AtmosAlarmEvent>(OnAtmos);
        SubscribeLocalEvent<KiasAnomalyGrowthEvent>(OnGrowth);
        SubscribeLocalEvent<KiasAtmosStateChangedEvent>(OnAtmosState);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
        SubscribeLocalEvent<KiasRoomsChangedEvent>(OnRoomsChanged);
    }

    private void OnAnomaly(ref AnomalyStabilityChangedEvent args)
    {
        if (!TryComp<AnomalyComponent>(args.Anomaly, out var anomaly)
            || args.PreviousStability is not { } previous || previous > anomaly.GrowthThreshold || args.Stability <= anomaly.GrowthThreshold)
            return;
        var rooms = EntityManager.System<KiasRoomTopologySystem>();
        foreach (var grid in _kias.ActiveGrids.ToArray())
        {
            if (Transform(grid).MapUid != Transform(args.Anomaly).MapUid) continue;
            if (rooms.Grids.TryGetValue(grid, out var geometry) && geometry.Pending)
            {
                if (!_pendingGrowth.TryGetValue(grid, out var pending)) _pendingGrowth.Add(grid, pending = new());
                if (pending.Count < 4096) pending.Add(args.Anomaly);
                continue;
            }
            PublishGrowth(grid, args.Anomaly);
        }
    }

    private void OnRoomsChanged(ref KiasRoomsChangedEvent args)
    {
        if (!_pendingGrowth.Remove(args.Grid, out var pending)) return;
        foreach (var source in pending) PublishGrowth(args.Grid, source);
    }

    private void PublishGrowth(EntityUid grid, EntityUid source)
    {
        if (TerminatingOrDeleted(source) || Transform(source).MapUid != Transform(grid).MapUid
            || !_crew.HasCoverage(grid, source, KiasScannerModules.Spectral)) return;
        var ev = new KiasAnomalyGrowthEvent(grid, source, Location(grid, source));
        RaiseLocalEvent(grid, ref ev, true);
    }

    private void OnAtmos(AtmosAlarmEvent args)
    {
        if (args.Source is not { } source || !HasComp<AirAlarmComponent>(source)
            || Transform(source).GridUid is not { } grid || !_kias.HasRole(grid, KiasDeviceRole.Atmosphere)
            || !HasComp<KiasIntegratedComponent>(source) || !EntityManager.System<KiasIntegrationSystem>().CanControl(source))
            return;
        var ev = new KiasAtmosStateChangedEvent(grid, source, args.AlarmType, Location(grid, source));
        RaiseLocalEvent(grid, ref ev, true);
    }

    private void OnGrowth(ref KiasAnomalyGrowthEvent args)
    {
        Publish(args.Grid, Loc.GetString("kias-anomaly-growth", ("location", args.Location)), true, announce: false, key: $"anomaly:{args.Source}:{args.Location}");
    }

    private void OnAtmosState(ref KiasAtmosStateChangedEvent args)
    {
        if (args.State == AtmosAlarmType.Danger)
            Publish(args.Grid, Loc.GetString("kias-atmos-danger", ("location", args.Location)), true, announce: false, key: $"atmos:{args.Source}:{args.Location}");
    }

    public string Location(EntityUid grid, EntityUid source)
    {
        var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(source)).Position;
        if (TryComp<NavMapComponent>(grid, out var nav) && nav.Beacons.Count > 0)
        {
            var closest = nav.Beacons.Values.OrderBy(beacon => System.Numerics.Vector2.DistanceSquared(beacon.Position, position)).First();
            if (System.Numerics.Vector2.DistanceSquared(closest.Position, position) <= 100 && !string.IsNullOrWhiteSpace(closest.Text))
                return closest.Text;
        }
        if (TryComp<KiasDeviceComponent>(source, out var device) && !string.IsNullOrWhiteSpace(device.Room))
            return device.Room;
        if (_crew.RoomLabel(grid, source) is { } room)
            return room;
        var local = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(source)).Position;
        if (TryComp<MapGridComponent>(grid, out var map))
            local -= map.LocalAABB.Center;
        if (local.LengthSquared() <= 4)
            return Loc.GetString("kias-sector-center");
        var sector = local.Y >= 0 ? "fore" : "aft";
        sector += local.X < 0 ? "-port" : "-starboard";
        return Loc.GetString($"kias-sector-{sector}");
    }

    private void OnGridRemoval(GridRemovalEvent args)
    {
        _logGate.Remove(args.EntityUid);
        _pendingGrowth.Remove(args.EntityUid);
    }

    public void Publish(EntityUid grid, string message, bool warning = false, bool announce = true, EntityUid? speaker = null, string group = "", string key = "", KiasAudioChannel? channel = null, bool record = true)
    {
        if (!_kias.ActiveGrids.Contains(grid) || !TryComp<KiasGridComponent>(grid, out var runtime))
            return;
        key = string.IsNullOrEmpty(key) ? $"{speaker}:{group}:{message}" : key;
        var settings = runtime.Core is { } core && TryComp<KiasAudioComponent>(core, out var audio) ? audio : null;
        var severity = runtime.Core is { } coreUid && TryComp<KiasProtocolComponent>(coreUid, out var protocols) ? (int) protocols.Alert : warning ? 1 : 0;
        if (record && _kias.HasRole(grid, KiasDeviceRole.Recorder) && _logGate.Allow(grid, key, _timing.CurTime, settings?.LogCooldown ?? 2, severity, out var repeated))
        {
            var logMessage = repeated > 0 ? $"{message} ×{repeated + 1}" : message;
            if (runtime.Log.Count >= 64)
                runtime.Log.Dequeue();
            runtime.Log.Enqueue($"[{_timing.CurTime:hh\\:mm\\:ss}] {(warning ? "[!] " : "")}{logMessage}");
            foreach (var uid in runtime.Online)
            {
                if (!_kias.IsOnline(uid) || !TryComp<KiasRecorderComponent>(uid, out var recorder))
                    continue;
                while (recorder.Entries.Count >= 64)
                    recorder.Entries.RemoveAt(0);
                recorder.Entries.Add(runtime.Log.Last());
            }
        }
        if (announce)
        {
            var ev = new KiasAnnouncementEvent(grid, message, warning, speaker, group, key, channel);
            RaiseLocalEvent(grid, ref ev, true);
        }
        _display.RefreshOpen(grid);
    }
}
