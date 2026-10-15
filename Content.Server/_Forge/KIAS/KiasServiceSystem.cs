using System.Linq;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._Forge.KIAS;
using Content.Shared.DeviceLinking;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Forge.KIAS;

public sealed class KiasServiceSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private DeviceLinkSystem _links = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private KiasDisplaySystem _display = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Robust.Server.GameObjects.UserInterfaceSystem _ui = default!;
    private readonly Dictionary<EntityUid, TimeSpan> _tests = new();
    public bool IsTesting(EntityUid grid) => _tests.ContainsKey(grid);

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasServiceToolComponent, AfterInteractEvent>(OnUse);
        SubscribeLocalEvent<KiasServiceToolComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KiasServiceToolComponent, GetVerbsEvent<AlternativeVerb>>(OnModes);
        SubscribeLocalEvent<KiasServiceToolComponent, KiasSetMessage>(OnMessage);
        SubscribeLocalEvent<KiasServiceToolComponent, KiasModeMessage>(OnModeMessage);
        SubscribeLocalEvent<KiasServiceToolComponent, KiasDeviceSettingsMessage>(OnSettings);
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
    }

    private void OnStartup(Entity<KiasServiceToolComponent> ent, ref ComponentStartup args)
    {
        var legacy = ent.Comp.Message.Trim();
        if (ent.Comp.Mode == KiasServiceMode.Group && ent.Comp.Group.Length == 0 && legacy.Length is >= 1 and <= 32)
            ent.Comp.Group = legacy.ToUpperInvariant();
    }

    private void OnMessage(Entity<KiasServiceToolComponent> ent, ref KiasSetMessage args)
    {
        if (args.Message.Length > 256)
            return;
        if (ent.Comp.Mode == KiasServiceMode.Group)
        {
            if (args.Message.Trim().Length > 32) return;
            ent.Comp.Group = args.Message.Trim().ToUpperInvariant();
            _display.Refresh(ent);
            return;
        }
        ent.Comp.Message = args.Message.Trim()[..Math.Min(args.Message.Trim().Length, 256)];
        _display.Refresh(ent);
    }

    private void OnModeMessage(Entity<KiasServiceToolComponent> ent, ref KiasModeMessage args)
    {
        if (!Enum.IsDefined(args.Mode)) return;
        ent.Comp.Mode = args.Mode == KiasServiceMode.Monitor ? KiasServiceMode.Diagnose : args.Mode;
        ent.Comp.Source = null;
        ent.Comp.Target = null;
        ent.Comp.Geometry = null;
        _display.Refresh(ent);
    }

    private void OnModes(Entity<KiasServiceToolComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        foreach (var mode in Enum.GetValues<KiasServiceMode>())
        {
            if (mode == KiasServiceMode.Monitor) continue;
            var selected = mode;
            var tool = ent.Comp;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString($"kias-mode-{mode.ToString().ToLowerInvariant()}"),
                Act = () => { tool.Mode = selected; tool.Source = null; tool.Target = null; tool.Geometry = null; _display.Refresh(ent); },
            });
        }
    }

    private void OnUse(Entity<KiasServiceToolComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;
        if (ent.Comp.Mode == KiasServiceMode.Group && Transform(target).GridUid is { } lightGrid
            && _kias.ActiveGrids.Contains(lightGrid) && _kias.CanConfigure(lightGrid, args.User))
        {
            ent.Comp.Target = target;
            var group = ent.Comp.Group.Trim().ToUpperInvariant();
            if (group.Length == 0)
            {
                args.Handled = true;
                _ui.TryOpenUi(ent.Owner, KiasUiKey.Service, args.User);
                _display.Refresh(ent);
                return;
            }
            if (group.Length is < 1 or > 32)
                return;
            if (HasComp<Content.Shared.Light.Components.PoweredLightComponent>(target))
                EnsureComp<KiasLightGroupComponent>(target).Group = group;
            else if (TryComp<KiasLightControllerComponent>(target, out var controller) && _kias.IsOnline(target))
                controller.Group = group;
            else if (TryComp<KiasSpeakerComponent>(target, out var speakerGroup) && _kias.IsOnline(target))
                speakerGroup.Group = group;
            else
                return;
            args.Handled = true;
            _kias.Invalidate(lightGrid);
            _popup.PopupEntity(Loc.GetString("kias-light-group-set", ("group", group)), target, args.User);
            _display.Refresh(ent);
            return;
        }
        if (ent.Comp.Mode == KiasServiceMode.Link && ent.Comp.Source is { } monitor
            && TryComp<KiasResourceMonitorComponent>(monitor, out var resources))
        {
            if (_kias.IsOnline(monitor) && Transform(monitor).GridUid is { } resourceGrid
                && Transform(target).GridUid == resourceGrid && _kias.CanConfigure(resourceGrid, args.User))
            {
                if (resources.Targets.Contains(target)) resources.Targets.Remove(target);
                else if (resources.Targets.Count < 32) resources.Targets.Add(target);
                ent.Comp.Source = null;
                args.Handled = true;
            }
            return;
        }
        if (!HasComp<KiasDeviceComponent>(target) && !(ent.Comp.Mode == KiasServiceMode.Link && HasComp<DeviceLinkSinkComponent>(target)))
            return;
        args.Handled = true;
        if (Transform(target).GridUid is not { } accessGrid || !_kias.CanConfigure(accessGrid, args.User))
        {
            _popup.PopupEntity(Loc.GetString("kias-owner-only"), target, args.User);
            return;
        }
        ent.Comp.Target = target;
        _popup.PopupEntity(EntityManager.System<KiasDeviceIdentitySystem>().Label(target), target, args.User);
        if (ent.Comp.Mode is KiasServiceMode.Coverage or KiasServiceMode.Diagnose or KiasServiceMode.Monitor)
        {
            ent.Comp.Geometry = ent.Comp.Mode == KiasServiceMode.Coverage ? _display.BuildCoverage(accessGrid, target) : null;
            _ui.TryOpenUi(ent.Owner, KiasUiKey.Service, args.User);
            _display.Refresh(ent);
            return;
        }
        if (HasComp<KiasDeviceComponent>(target) && !_kias.IsOnline(target) || Transform(target).GridUid is not { } grid)
        {
            _popup.PopupEntity(Loc.GetString("kias-status-offline"), target, args.User);
            return;
        }
        switch (ent.Comp.Mode)
        {
            case KiasServiceMode.Room:
                var room = ent.Comp.Message.Trim();
                Comp<KiasDeviceComponent>(target).Room = room[..Math.Min(room.Length, 64)];
                _kias.Invalidate(grid);
                _popup.PopupEntity(Loc.GetString("kias-room-set", ("room", Comp<KiasDeviceComponent>(target).Room)), target, args.User);
                break;
            case KiasServiceMode.Link:
                if (ent.Comp.Source is { } transmitter && transmitter != target && HasComp<KiasWirelessComponent>(transmitter)
                    && HasComp<KiasWirelessComponent>(target) && Transform(transmitter).GridUid != grid)
                {
                    if (!EntityManager.System<KiasAccessSystem>().SetWirelessTrust(target, transmitter, args.User)) break;
                    if (Comp<KiasWirelessComponent>(target).TrustedTransmitters.Contains(transmitter))
                        EntityManager.System<KiasDeviceSystem>().LinkDefaults(args.User, transmitter, target);
                    else
                        _links.RemoveSinkFromSource(transmitter, target);
                    ent.Comp.Source = null;
                    _display.RefreshOpen(grid);
                    _popup.PopupEntity(Loc.GetString("kias-wireless-trust-updated"), target, args.User);
                    break;
                }
                if (ent.Comp.Source is { } selectedSource && selectedSource != target && _kias.IsOnline(selectedSource)
                    && Transform(selectedSource).GridUid == grid && HasComp<DeviceLinkSinkComponent>(target))
                {
                    EntityManager.System<KiasDeviceSystem>().LinkDefaults(args.User, selectedSource, target);
                    if (TryComp<KiasSpeakerComponent>(target, out var selectedSpeaker))
                    {
                        foreach (var (sourcePort, sinkPort) in _links.GetLinks(selectedSource, target))
                        {
                            if (sinkPort.ToString() != "KiasAnnounce") continue;
                            selectedSpeaker.Links.RemoveAll(link => link.Source == selectedSource && link.SourcePort == sourcePort.ToString());
                            selectedSpeaker.Links.Add(new KiasSpeakerLink { Source = selectedSource, SourcePort = sourcePort, Message = ent.Comp.Message });
                        }
                    }
                    ent.Comp.Source = null;
                }
                else if (HasComp<DeviceLinkSourceComponent>(target) || HasComp<KiasResourceMonitorComponent>(target))
                {
                    ent.Comp.Source = target;
                    _popup.PopupEntity(Loc.GetString("kias-source-selected"), target, args.User);
                }
                else if (ent.Comp.Source is { } source && _kias.IsOnline(source)
                         && Transform(source).GridUid == grid && TryComp<KiasSpeakerComponent>(target, out var speaker))
                {
                    EntityManager.System<KiasDeviceSystem>().LinkDefaults(args.User, source, target);
                    foreach (var (sourcePort, sinkPort) in _links.GetLinks(source, target))
                    {
                        if (sinkPort.ToString() != "KiasAnnounce")
                            continue;
                        speaker.Links.RemoveAll(link => link.Source == source && link.SourcePort == sourcePort.ToString());
                        speaker.Links.Add(new KiasSpeakerLink { Source = source, SourcePort = sourcePort, Message = ent.Comp.Message });
                    }
                    ent.Comp.Source = null;
                }
                break;
            case KiasServiceMode.Diagnose:
                _popup.PopupEntity(Loc.GetString("kias-device-status", ("status", Loc.GetString($"kias-status-{Comp<KiasDeviceComponent>(target).Status.ToString().ToLowerInvariant()}"))), target, args.User);
                break;
            case KiasServiceMode.Coverage:
                ent.Comp.Target = target;
                ent.Comp.Geometry = EntityManager.System<KiasDisplaySystem>().BuildCoverage(grid, target);
                _ui.TryOpenUi(ent.Owner, KiasUiKey.Service, args.User);
                _display.Refresh(ent);
                break;
            case KiasServiceMode.Test:
                if (_timing.CurTime < ent.Comp.NextTest || _tests.ContainsKey(grid))
                {
                    _popup.PopupEntity(Loc.GetString("kias-test-cooldown"), target, args.User);
                    break;
                }
                ent.Comp.NextTest = _timing.CurTime + TimeSpan.FromSeconds(10);
                var runtime = Comp<KiasGridComponent>(grid);
                _tests[grid] = _timing.CurTime + TimeSpan.FromSeconds(5);
                var announce = new KiasAnnouncementEvent(grid, Loc.GetString("kias-test-start"));
                RaiseLocalEvent(grid, ref announce, true);
                _popup.PopupEntity(string.Join("\n", runtime.Devices.Take(64).Select(uid =>
                    $"{Name(uid)}: {Loc.GetString($"kias-status-{Comp<KiasDeviceComponent>(uid).Status.ToString().ToLowerInvariant()}")}")), target, args.User);
                if (TryComp<DeviceLinkSourceComponent>(target, out var ports) && ports.Ports.Any(port => port.ToString() == "KiasMotion"))
                    EntityManager.System<Controllers.KiasControllerRuntimeSystem>().ProbeSource(target, "KiasMotion");
                break;
        }
    }

    private void OnSettings(Entity<KiasServiceToolComponent> ent, ref KiasDeviceSettingsMessage args)
    {
        if (ent.Comp.Mode is not (KiasServiceMode.Diagnose or KiasServiceMode.Monitor)
            || ent.Comp.Target is not { } target || TerminatingOrDeleted(target)
            || Transform(target).GridUid is not { } grid || !_kias.CanConfigure(grid, args.Actor)
            || !EntityManager.System<Content.Shared.Interaction.SharedInteractionSystem>().InRangeAndAccessible(args.Actor, target)) return;
        if (TryComp<KiasRotaryComponent>(target, out var rotary))
        {
            if (args.RotaryPositions is < 2 or > 4 || args.RotarySignals.Count != args.RotaryPositions
                || args.RotarySignals.Any(signal => signal is < 0 or > 3)) return;
            rotary.Positions = args.RotaryPositions;
            rotary.Position = Math.Clamp(rotary.Position, 0, rotary.Positions - 1);
            rotary.Signals = args.RotarySignals.ToList();
        }
        else if (_display.IsSensor(target) && float.IsFinite(args.Range)) _display.SetSensorRange(target, args.Range);
        else return;
        _display.Refresh(target);
        _display.Refresh(ent);
    }

    public override void Update(float frameTime)
    {
        if (_tests.Count == 0)
            return;
        foreach (var (grid, test) in _tests.ToArray())
        {
            if (TerminatingOrDeleted(grid))
                _tests.Remove(grid);
            else if (_timing.CurTime >= test)
            {
                _tests.Remove(grid);
                var announce = new KiasAnnouncementEvent(grid, Loc.GetString("kias-test-done"));
                RaiseLocalEvent(grid, ref announce, true);
            }
        }
    }

    private void OnAvailability(ref KiasAvailabilityChangedEvent args)
    {
        if (!args.Active) _tests.Remove(args.Grid);
    }
}
