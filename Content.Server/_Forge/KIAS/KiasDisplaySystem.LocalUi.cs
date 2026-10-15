using System.Linq;
using Content.Shared._Forge.KIAS;
using Content.Shared.UserInterface;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Server._Forge.KIAS.Controllers;
using System.Globalization;

namespace Content.Server._Forge.KIAS;

public sealed partial class KiasDisplaySystem
{
    private void InitializeLocalUi()
    {
        SubscribeLocalEvent<KiasManagementComponent, ActivatableUIOpenAttemptEvent>(OnManagementAttempt);
        SubscribeLocalEvent<KiasLocalUiComponent, BoundUIOpenedEvent>(OnLocalOpen);
        SubscribeLocalEvent<KiasLocalUiComponent, BoundUIClosedEvent>(OnLocalClosed);
        SubscribeLocalEvent<KiasLocalUiComponent, KiasRefreshMessage>(OnLocalRefresh);
        SubscribeLocalEvent<KiasLocalUiComponent, KiasDeviceSettingsMessage>(OnDeviceSettings);
        SubscribeLocalEvent<KiasDisplayComponent, KiasDeviceSettingsMessage>(OnDisplaySettings);
    }

    private void OnManagementAttempt(Entity<KiasManagementComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.User))
            args.Cancel();
    }

    private void OnLocalOpen(Entity<KiasLocalUiComponent> ent, ref BoundUIOpenedEvent args) { _sentStates.Remove(ent); Refresh(ent); }
    private void OnLocalRefresh(Entity<KiasLocalUiComponent> ent, ref KiasRefreshMessage args) => Refresh(ent);

    public KiasUiKey UiKey(EntityUid uid)
    {
        if (HasComp<KiasManagementComponent>(uid)) return KiasUiKey.Key;
        if (HasComp<KiasServiceToolComponent>(uid)) return KiasUiKey.Service;
        if (HasComp<KiasRecorderComponent>(uid)) return KiasUiKey.Recorder;
        if (HasComp<KiasRoomScannerComponent>(uid)) return KiasUiKey.Scanner;
        if (HasComp<KiasCrewServerComponent>(uid)) return KiasUiKey.Crew;
        if (HasComp<KiasSpeakerComponent>(uid)) return KiasUiKey.Speaker;
        if (HasComp<KiasLocalUiComponent>(uid)) return KiasUiKey.Sensor;
        return KiasUiKey.Wall;
    }

    public BoundUserInterfaceState BuildLocalState(EntityUid uid)
    {
        if (TryComp<KiasServiceToolComponent>(uid, out var tool))
        {
            if (tool.Mode == KiasServiceMode.Coverage && tool.Target is { } coverageTarget && !TerminatingOrDeleted(coverageTarget)
                && Transform(coverageTarget).GridUid is { } coverageGrid) tool.Geometry = BuildCoverage(coverageGrid, coverageTarget);
            else if (tool.Target is { } deletedTarget && TerminatingOrDeleted(deletedTarget)) tool.Geometry = null;
            var selectedGroup = tool.Target is { } selected && !TerminatingOrDeleted(selected)
                ? TryComp<KiasSpeakerComponent>(selected, out var selectedSpeaker) ? selectedSpeaker.Group
                    : TryComp<KiasLightControllerComponent>(selected, out var selectedController) ? selectedController.Group
                    : TryComp<KiasLightGroupComponent>(selected, out var selectedLight) ? selectedLight.Group : string.Empty : string.Empty;
            return new KiasServiceState { Mode = tool.Mode, Message = tool.Message, Group = tool.Group, CurrentGroup = selectedGroup,
                GroupKind = tool.Target is { } groupTarget && HasComp<KiasSpeakerComponent>(groupTarget) ? "speaker" : "lighting",
                Details = tool.Target is { } diagnosticTarget && !TerminatingOrDeleted(diagnosticTarget) ? Diagnostics(diagnosticTarget) + (tool.Mode is KiasServiceMode.Diagnose or KiasServiceMode.Monitor ? "\n" + OutputDetails(diagnosticTarget) : string.Empty) : string.Empty,
                SourceName = tool.Source is { } namedSource && !TerminatingOrDeleted(namedSource) ? Name(namedSource) : string.Empty,
                TargetName = tool.Target is { } namedTarget && !TerminatingOrDeleted(namedTarget) ? EntityManager.System<KiasDeviceIdentitySystem>().Label(namedTarget) : string.Empty,
                RotaryPositions = tool.Target is { } rotaryTarget && TryComp<KiasRotaryComponent>(rotaryTarget, out var rotary) ? rotary.Positions : 0,
                RotarySignals = tool.Target is { } signalTarget && TryComp<KiasRotaryComponent>(signalTarget, out var signalRotary) ? signalRotary.Signals.Take(signalRotary.Positions).ToList() : new(),
                SensorRange = tool.Target is { } rangeTarget && !TerminatingOrDeleted(rangeTarget) && IsSensor(rangeTarget) && !HasComp<KiasRoomScannerComponent>(rangeTarget)
                    ? SensorRange(rangeTarget) : null,
                Source = tool.Source is { } source && !TerminatingOrDeleted(source) ? GetNetEntity(source) : null,
                Target = tool.Target is { } target && !TerminatingOrDeleted(target) ? GetNetEntity(target) : null,
                Geometry = tool.Mode == KiasServiceMode.Coverage ? tool.Geometry : null };
        }
        if (TryComp<KiasRecorderComponent>(uid, out var recorder))
            return new KiasRecorderState { Online = _kias.IsOnline(uid), Entries = recorder.Entries.Concat(recorder.ProtocolEvents).OrderBy(entry => entry, StringComparer.Ordinal).TakeLast(64).ToList() };
        KiasGridComponent? runtime = null;
        if (Transform(uid).GridUid is { } grid)
            TryComp(grid, out runtime);
        if (TryComp<KiasDisplayComponent>(uid, out var display))
        {
            var details = display.Page switch
            {
                KiasDisplayPage.Crew => runtime?.CrewDetails,
                KiasDisplayPage.Power => runtime?.PowerDetails,
                KiasDisplayPage.Navigation => runtime == null ? null : string.Join("\n", runtime.Log.TakeLast(5)),
                KiasDisplayPage.Atmos => runtime == null ? null : RoleDetails(runtime, KiasDeviceRole.Atmosphere, KiasDeviceRole.Suppression, KiasDeviceRole.Scanner),
                KiasDisplayPage.Defence => runtime == null ? null : RoleDetails(runtime, KiasDeviceRole.Defence, KiasDeviceRole.PdcRadar, KiasDeviceRole.PdcWeapon, KiasDeviceRole.Hull),
                KiasDisplayPage.Faults => runtime == null ? null : string.Join("\n", runtime.Devices.Where(device => !TerminatingOrDeleted(device)
                    && TryComp<KiasDeviceComponent>(device, out var component) && component.Status != KiasDeviceStatus.Online)
                    .Take(32).Select(entity => Name(entity))),
                _ => Loc.GetString(_kias.IsOnline(uid) ? "kias-online" : "kias-status-offline"),
            };
            return new KiasWallState { Online = _kias.IsOnline(uid), Entities = runtime?.Entities ?? 0, Crew = runtime?.Crew ?? 0,
                Page = display.Page, Details = details ?? string.Empty };
        }
        KiasLocalState state;
        if (TryComp<KiasRoomScannerComponent>(uid, out var scanner))
            state = new KiasScannerState { Modules = scanner.Modules,
                Geometry = Transform(uid).GridUid is { } scannerGrid ? BuildCoverage(scannerGrid, uid) : null };
        else if (TryComp<KiasCrewServerComponent>(uid, out var crew))
            state = new KiasCrewState { Locked = crew.RegistrationLocked, DetectedCrew = runtime?.Crew ?? 0, Registered = crew.Registered.Take(256).ToList() };
        else if (TryComp<KiasSpeakerComponent>(uid, out var speaker))
            state = new KiasSpeakerState { Group = speaker.Group, Message = speaker.Message };
        else if (TryComp<KiasResourceMonitorComponent>(uid, out var resources))
            state = new KiasResourceState { Details = ResourceDetails(uid, resources, runtime) };
        else if (TryComp<KiasWirelessComponent>(uid, out var wireless))
            state = new KiasWirelessState { Channel = wireless.Channel, Range = wireless.Range,
                TrustedTransmitters = wireless.TrustedTransmitters.Where(source => !TerminatingOrDeleted(source)).Take(16).Select(source => Name(source)).ToList() };
        else if (TryComp<KiasLightControllerComponent>(uid, out var light))
            state = new KiasLightState { Group = light.Group, Color = light.Color, Brightness = light.Brightness };
        else
            state = new KiasSensorState { Range = SensorRange(uid), Arc = HasComp<KiasWeaponFlashComponent>(uid) ? 90 : 360,
                Geometry = Transform(uid).GridUid is { } sensorGrid ? BuildCoverage(sensorGrid, uid) : null };
        state.Name = Name(uid);
        if (TryComp<KiasDeviceComponent>(uid, out var device))
        {
            state.Status = device.Status;
            state.Room = device.Room;
        }
        return state;
    }

    public float SensorRange(EntityUid uid)
    {
        if (TryComp<KiasHorizonComponent>(uid, out var horizon)) return Math.Clamp(horizon.Range, 0, 2000);
        if (TryComp<KiasProximityComponent>(uid, out var proximity)) return Math.Clamp(proximity.Range, 0, 500);
        if (TryComp<KiasWeaponFlashComponent>(uid, out var flash)) return Math.Clamp(flash.Range, 0, 1000);
        if (TryComp<KiasPdcRadarComponent>(uid, out var radar)) return Math.Clamp(radar.Range, 0, 500);
        if (TryComp<KiasHullSensorComponent>(uid, out var hull)) return Math.Clamp(hull.Range, 50, 100);
        return 0;
    }

    public bool IsSensor(EntityUid uid) => HasComp<KiasRoomScannerComponent>(uid) || HasComp<KiasHorizonComponent>(uid)
        || HasComp<KiasProximityComponent>(uid) || HasComp<KiasWeaponFlashComponent>(uid) || HasComp<KiasPdcRadarComponent>(uid) || HasComp<KiasHullSensorComponent>(uid);

    public void SetSensorRange(EntityUid uid, float range)
    {
        if (!float.IsFinite(range)) return;
        if (HasComp<KiasRoomScannerComponent>(uid)) return;
        if (TryComp<KiasHorizonComponent>(uid, out var horizon)) horizon.Range = Math.Clamp(range, 0, 2000);
        if (TryComp<KiasProximityComponent>(uid, out var proximity)) proximity.Range = Math.Clamp(range, 0, 500);
        if (TryComp<KiasWeaponFlashComponent>(uid, out var flash)) flash.Range = Math.Clamp(range, 0, 1000);
        if (TryComp<KiasPdcRadarComponent>(uid, out var radar)) radar.Range = Math.Clamp(range, 0, 500);
        if (TryComp<KiasHullSensorComponent>(uid, out var hull)) hull.Range = Math.Clamp(range, 50, 100);
    }

    public KiasCoverageGeometry BuildCoverage(EntityUid grid, EntityUid target)
    {
        var geometry = new KiasCoverageGeometry { Target = GetNetEntity(target), Grid = GetNetEntity(grid) };
        if (HasComp<KiasRoomScannerComponent>(target))
        {
            geometry.Shape = KiasCoverageShape.Room;
            var rooms = EntityManager.System<KiasRoomTopologySystem>();
            if (rooms.Grids.TryGetValue(grid, out var cache)) geometry.Revision = cache.Revision;
            if (rooms.TryGetScannerRoom(target, out var room, out var status))
            {
                geometry.RoomId = room.Id;
                if (room.Status == KiasRoomStatus.OpenToSpace) geometry.Radius = 7;
                if (room.Status == KiasRoomStatus.ExteriorSector && TryComp<KiasRoomScannerComponent>(target, out var scanner))
                    geometry.Radius = Math.Clamp(scanner.Range, 1, 32);
                const int previewLimit = 4096;
                foreach (var cell in rooms.ScannerCells(target))
                {
                    if (room.Doors.Contains(cell))
                    {
                        geometry.DoorCount++;
                        if (geometry.BoundaryCells.Count < previewLimit) geometry.BoundaryCells.Add(cell);
                    }
                    else
                    {
                        geometry.TileCount++;
                        if (geometry.Cells.Count < previewLimit) geometry.Cells.Add(cell);
                        else geometry.PreviewTruncated = true;
                    }
                }
            }
            geometry.RoomStatus = status.ToString();
            return geometry;
        }
        var range = SensorRange(target);
        if (HasComp<KiasHorizonComponent>(target) || HasComp<KiasProximityComponent>(target)
            || HasComp<KiasWeaponFlashComponent>(target) || HasComp<KiasPdcRadarComponent>(target) || HasComp<KiasHullSensorComponent>(target))
        {
            geometry.Large = true;
            geometry.Radius = range;
            geometry.Arc = HasComp<KiasWeaponFlashComponent>(target) ? 90 : 360;
            geometry.Shape = geometry.Arc < 360 ? KiasCoverageShape.Sector : KiasCoverageShape.Circle;
            return geometry;
        }
        geometry.Shape = KiasCoverageShape.Data;
        if (!TryComp<KiasGridComponent>(grid, out var runtime) || runtime.Core is not { } core
            || !TryComp<Robust.Shared.Map.Components.MapGridComponent>(grid, out var map))
            return geometry;
        geometry.Revision = runtime.Revision;
        var maps = EntityManager.System<SharedMapSystem>();
        var center = maps.TileIndicesFor(grid, map, Transform(target).Coordinates);
        var coreTile = maps.TileIndicesFor(grid, map, Transform(core).Coordinates);
        const int diagnosticRadius = 12;
        for (var y = -diagnosticRadius; y <= diagnosticRadius; y++)
        for (var x = -diagnosticRadius; x <= diagnosticRadius; x++)
        {
            var tile = center + new Vector2i(x, y);
            if (runtime.Topology.Connected(coreTile, tile))
                geometry.Cells.Add(tile);
        }
        return geometry;
    }

    private void OnDisplaySettings(Entity<KiasDisplayComponent> ent, ref KiasDeviceSettingsMessage args)
    {
        if (HasComp<KiasManagementComponent>(ent) || HasComp<KiasRecorderComponent>(ent) || HasComp<KiasServiceToolComponent>(ent)
            || !Enum.IsDefined(args.Page) || Transform(ent).GridUid is not { } grid || !_kias.IsOnline(ent) || !_kias.CanConfigure(grid, args.Actor))
            return;
        ent.Comp.Page = args.Page;
        Refresh(ent);
        RefreshCoverageTools(grid);
    }

    private void OnDeviceSettings(Entity<KiasLocalUiComponent> ent, ref KiasDeviceSettingsMessage args)
    {
        if (!_kias.IsOnline(ent) || Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.Actor)
            || args.Room.Length > 64 || args.Group.Length > 32 || args.Message.Length > 256 || !float.IsFinite(args.Range))
            return;
        var device = Comp<KiasDeviceComponent>(ent);
        var room = args.Room.Trim();
        if (device.Room != room)
        {
            device.Room = room;
            _kias.Invalidate(grid);
        }
        SetSensorRange(ent, args.Range);
        if (TryComp<KiasWirelessComponent>(ent, out var wireless))
        {
            wireless.Channel = args.Group.Trim();
            wireless.Range = Math.Clamp(args.Range, 1, 200);
            if (TryComp<Content.Server.DeviceNetwork.Components.WirelessNetworkComponent>(ent, out var nativeWireless)) nativeWireless.Range = (int) wireless.Range;
        }
        if (TryComp<KiasCrewServerComponent>(ent, out var crew) && EntityManager.System<KiasAccessSystem>().CanConfigure(grid, args.Actor, security: true))
            crew.RegistrationLocked = args.LockRegistration;
        if (TryComp<KiasSpeakerComponent>(ent, out var speaker))
        {
            speaker.Group = args.Group.Trim().ToUpperInvariant();
            _kias.Invalidate(grid);
            speaker.Message = args.Message.Trim();
        }
        if (TryComp<KiasLightControllerComponent>(ent, out var light) && float.IsFinite(args.Brightness)
            && args.Color.Length == 7 && args.Color[0] == '#' && args.Color.Skip(1).All(Uri.IsHexDigit))
        {
            light.Group = args.Group.Trim().ToUpperInvariant();
            _kias.Invalidate(grid);
            light.Color = args.Color;
            light.Brightness = Math.Clamp(args.Brightness, 0, 2);
        }
        Refresh(ent);
        RefreshCoverageTools(grid);
    }

    private string RoleDetails(KiasGridComponent runtime, params KiasDeviceRole[] roles) => string.Join("\n",
        runtime.Devices.Where(uid => !TerminatingOrDeleted(uid) && TryComp<KiasDeviceComponent>(uid, out var device) && roles.Contains(device.Role))
            .Take(32).Select(uid => $"{Name(uid)}: {Loc.GetString($"kias-status-{Comp<KiasDeviceComponent>(uid).Status.ToString().ToLowerInvariant()}")}"));

    private string OutputDetails(EntityUid target)
    {
        if (!_kias.IsOnline(target)) return Loc.GetString("kias-monitor-offline");
        var io = EntityManager.System<KiasControllerIoSystem>();
        var lines = new List<string> { Loc.GetString("kias-monitor-title") };
        foreach (var profile in io.Profiles(target).Order())
        {
            if (io.Schema(profile) is not { } schema) continue;
            var ports = schema.Where(port => port.Direction == KiasPortDirection.Output).ToArray();
            if (ports.Length == 0) continue;
            lines.Add(Loc.TryGetString($"kias-controller-profile-{profile.ToLowerInvariant()}", out var label) ? label : Loc.GetString("kias-controller-native-profile"));
            foreach (var port in ports)
            {
                var text = Loc.GetString("kias-monitor-unset");
                if (io.TryOutput(target, profile, port.Id, out var value, out var at))
                {
                    text = value.Type switch
                    {
                        KiasPortType.Signal => Loc.GetString("kias-monitor-pulse", ("seconds", Math.Max(0, (int) (_timing.CurTime - at).TotalSeconds))),
                        KiasPortType.Bool => Loc.GetString(value.Bool ? "kias-controller-yes" : "kias-controller-no"),
                        KiasPortType.Number => value.Number.ToString("G", CultureInfo.InvariantCulture),
                        KiasPortType.String => $"“{value.Text}”",
                        KiasPortType.Entity => value.Entity is { } entity && !TerminatingOrDeleted(entity) ? Name(entity) : "—",
                        KiasPortType.Enum => Loc.TryGetString($"kias-controller-enum-{port.EnumDomain.ToString().ToLowerInvariant()}-{value.Enum}", out var choice) ? choice : value.Enum.ToString(),
                        _ => text,
                    };
                }
                var name = Loc.TryGetString(port.Name, out var localized) ? localized : port.Id;
                var type = Loc.GetString($"kias-controller-type-{port.Type.ToString().ToLowerInvariant()}");
                lines.Add($"  {name} ({type}): {text}");
            }
        }
        if (lines.Count == 1) lines.Add(Loc.GetString("kias-monitor-no-outputs"));
        return string.Join("\n", lines);
    }

    private string Diagnostics(EntityUid target)
    {
        if (!TryComp<KiasDeviceComponent>(target, out var device) || Transform(target).GridUid is not { } grid
            || !TryComp<KiasGridComponent>(grid, out var runtime) || !TryComp<Robust.Shared.Map.Components.MapGridComponent>(grid, out var map)) return string.Empty;
        var status = Loc.GetString($"kias-status-{device.Status.ToString().ToLowerInvariant()}");
        var core = runtime.Core;
        var connection = TryComp<KiasIntegratedComponent>(target, out var integrated) && !integrated.Direct
            && integrated.Scanner is { } scanner && !TerminatingOrDeleted(scanner) ? scanner : target;
        var path = core is { } uid ? runtime.Topology.DiagnosticPath(EntityManager.System<SharedMapSystem>().TileIndicesFor(grid, map, Transform(uid).Coordinates),
            EntityManager.System<SharedMapSystem>().TileIndicesFor(grid, map, Transform(connection).Coordinates)) : new List<Vector2i>();
        return Loc.GetString("kias-diagnostic-chain", ("core", core is { } source ? Name(source) : "—"), ("target", connection == target ? Name(target) : $"{Name(connection)} → {Name(target)}"),
            ("status", status), ("nodes", path.Count), ("power", Loc.GetString(EntityManager.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().IsPowered(target) ? "kias-online" : "kias-status-nopower")));
    }

    private string ResourceDetails(EntityUid uid, KiasResourceMonitorComponent resources, KiasGridComponent? runtime)
    {
        if (!_kias.IsOnline(uid)) return string.Empty;
        var lines = new List<string> { runtime?.PowerDetails ?? string.Empty };
        foreach (var target in resources.Targets.Take(32))
        {
            if (TerminatingOrDeleted(target) || Transform(target).GridUid != Transform(uid).GridUid) continue;
            if (TryComp<Content.Shared.Stacks.StackComponent>(target, out var stack) && stack.Count > 0) lines.Add($"{Name(target)}: {stack.Count}");
            if (TryComp<Content.Shared.Power.Components.BatteryComponent>(target, out var battery) && battery.CurrentCharge > 0) lines.Add($"{Name(target)}: {battery.CurrentCharge:F0} / {battery.MaxCharge:F0} J");
            var ammo = new Content.Shared.Weapons.Ranged.Events.GetAmmoCountEvent();
            RaiseLocalEvent(target, ref ammo);
            if (ammo.Count > 0) lines.Add($"{Name(target)}: {ammo.Count} / {ammo.Capacity}");
            if (HasComp<Content.Shared.Chemistry.Components.SolutionManager.SolutionContainerManagerComponent>(target))
            {
                foreach (var (name, solution) in EntityManager.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>()
                    .EnumerateSolutions(new Entity<Content.Shared.Chemistry.Components.SolutionManager.SolutionContainerManagerComponent?>(target, null)))
                    if (solution.Comp.Solution.Volume > 0) lines.Add($"{Name(target)} / {name}: {solution.Comp.Solution.Volume} u");
            }
        }
        return string.Join("\n", lines);
    }
}
