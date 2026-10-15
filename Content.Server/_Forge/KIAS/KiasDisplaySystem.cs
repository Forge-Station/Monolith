using System.Linq;
using System.Text;
using Content.Server.Chat.Systems;
using Content.Shared._Forge.KIAS;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceLinking;
using Content.Shared.Chat;
using Robust.Shared.Map.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared.Power.EntitySystems;

namespace Content.Server._Forge.KIAS;

public sealed partial class KiasDisplaySystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;
    private readonly Queue<EntityUid> _pendingRefresh = new();
    private readonly HashSet<EntityUid> _queuedRefresh = new();
    private TimeSpan _nextMonitorRefresh;

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology, after: new[] { typeof(KiasCrewSystem) });
        SubscribeLocalEvent<KiasAnnouncementEvent>(OnAnnouncement);
        SubscribeLocalEvent<KiasDisplayComponent, BoundUIOpenedEvent>(OnOpen);
        SubscribeLocalEvent<KiasDisplayComponent, BoundUIClosedEvent>(OnDisplayClosed);
        SubscribeLocalEvent<KiasDisplayComponent, KiasRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<KiasSpeakerComponent, SignalReceivedEvent>(OnSignal);
        SubscribeLocalEvent<KiasSpeakerComponent, ComponentShutdown>(OnSpeakerShutdown);
        SubscribeLocalEvent<KiasCountsChangedEvent>(OnCounts);
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
        InitializeLocalUi();
        SubscribeLocalEvent<KiasManagementComponent, KiasAudioSettingsMessage>(OnAudioSettings);
    }

    private void OnTopology(ref KiasTopologyChangedEvent args)
    {
        if (!TryComp<KiasGridComponent>(args.Grid, out var runtime))
            return;
        RefreshCoverageTools(args.Grid);
        foreach (var uid in runtime.Devices)
        {
            if (!_kias.IsOnline(uid) && TryComp<KiasSpeakerComponent>(uid, out var speaker) && !CanFinishShutdownTone(uid, speaker))
                speaker.Tone = _audio.Stop(speaker.Tone);
        }
        if (!runtime.Devices.Any(uid => _ui.IsUiOpen(uid, UiKey(uid))))
            return;
        foreach (var uid in runtime.Devices)
        {
            if (_ui.IsUiOpen(uid, UiKey(uid)))
                Refresh(uid);
        }
    }

    private void OnOpen(Entity<KiasDisplayComponent> ent, ref BoundUIOpenedEvent args)
    {
        _sentStates.Remove(ent);
        if (HasComp<KiasServiceToolComponent>(ent)) _coverageTools.Add(ent);
        Refresh(ent);
    }
    private void OnCounts(ref KiasCountsChangedEvent args)
    {
        RefreshOpen(args.Grid);
    }
    private void OnRefresh(Entity<KiasDisplayComponent> ent, ref KiasRefreshMessage args) => Refresh(ent);

    public void Refresh(EntityUid display)
    {
        if (!HasComp<KiasManagementComponent>(display))
        {
            SetState(display, UiKey(display), BuildLocalState(display));
            return;
        }
        var state = new KiasManagementState();
        if (Transform(display).GridUid is { } audioGrid && AudioSettings(audioGrid) is { } audio)
            state.Audio = new KiasAudioSettingsView { Notification = audio.Notification, Warning = audio.Warning, Battle = audio.Battle, Emergency = audio.Emergency };
        if (Transform(display).GridUid is { } grid && TryComp<KiasGridComponent>(grid, out var runtime))
        {
            state.Online = _kias.IsOnline(display);
            state.Entities = runtime.Entities;
            state.Crew = runtime.Crew;
            state.Log = runtime.CrewDetails + "\n" + string.Join("\n", runtime.Log);
            state.CrewDetails = runtime.CrewDetails;
            state.Power = _kias.HasRole(grid, KiasDeviceRole.Power) ? runtime.PowerDetails : Loc.GetString("kias-power-unavailable");
            if (TryComp<KiasResourceMonitorComponent>(display, out var resources)) state.Resources = ResourceDetails(display, resources, runtime);
            var atmos = new StringBuilder();
            var defence = new StringBuilder();
            var navigation = new StringBuilder();
            var faults = new StringBuilder();
            var devices = new StringBuilder();
            foreach (var uid in runtime.Devices.OrderBy(uid => uid.Id))
            {
                if (!TryComp<KiasDeviceComponent>(uid, out var device))
                    continue;
                devices.AppendLine($"{Name(uid)}: {Loc.GetString($"kias-status-{device.Status.ToString().ToLowerInvariant()}")}");
                var line = $"{Name(uid)}: {Loc.GetString($"kias-status-{device.Status.ToString().ToLowerInvariant()}")}";
                if (device.Role is KiasDeviceRole.Atmosphere or KiasDeviceRole.Suppression or KiasDeviceRole.Scanner)
                    atmos.AppendLine(line);
                if (device.Role is KiasDeviceRole.Defence or KiasDeviceRole.PdcRadar or KiasDeviceRole.PdcWeapon or KiasDeviceRole.Hull or KiasDeviceRole.WeaponFlash)
                    defence.AppendLine(line);
                if (device.Role is KiasDeviceRole.Navigation or KiasDeviceRole.Horizon or KiasDeviceRole.Proximity)
                    navigation.AppendLine(line);
                if (device.Status != KiasDeviceStatus.Online)
                    faults.AppendLine(line);
                if (TryComp<KiasPdcWeaponComponent>(uid, out var weapon) && weapon.AutomaticGrid != null)
                    defence.AppendLine(Loc.GetString("kias-pdc-reserved", ("weapon", Name(uid))));
                if (TryComp<KiasDefenceComponent>(uid, out var server))
                {
                    defence.AppendLine(Loc.GetString(server.PdcEnabled ? "kias-pdc-enabled" : "kias-pdc-disabled"));
                    if (server.FireLock) defence.AppendLine(Loc.GetString("kias-fire-locked"));
                }
            }
            var controllerRuntime = EntityManager.System<Controllers.KiasControllerRuntimeSystem>();
            state.Automation = Loc.GetString("kias-controller-management-status",
                ("running", runtime.Devices.Where(HasComp<Content.Shared._Forge.KIAS.Controllers.KiasControllerRackComponent>).Sum(controllerRuntime.RunningCount)));
            state.Devices = devices.ToString();
            state.Atmos = atmos.ToString();
            state.Defence = defence.ToString();
            state.Navigation = navigation.ToString();
            state.Faults = faults.Length == 0 ? Loc.GetString("kias-no-faults") : faults.ToString();
            if (runtime.Core is { } core && TryComp<KiasProtocolComponent>(core, out var protocols))
            {
                state.Alert = Loc.GetString($"kias-alert-{protocols.Alert.ToString().ToLowerInvariant()}");
            }
        }
        if (TryComp<KiasRecorderComponent>(display, out var recorder))
            state.Log = string.Join("\n", recorder.Entries);
        SetState(display, KiasUiKey.Key, state);
    }

    public void RefreshOpen(EntityUid grid)
    {
        if (_queuedRefresh.Add(grid)) _pendingRefresh.Enqueue(grid);
    }

    public override void Update(float frameTime)
    {
        using var measurement = new KiasUpdateMeasurement(_kias);
        if (_timing.CurTime >= _nextMonitorRefresh && _coverageTools.Count > 0)
        {
            _nextMonitorRefresh = _timing.CurTime + TimeSpan.FromSeconds(1);
            foreach (var tool in _coverageTools.ToArray())
            {
                if (TerminatingOrDeleted(tool) || !_ui.IsUiOpen(tool, KiasUiKey.Service)) { _coverageTools.Remove(tool); continue; }
                if (TryComp<KiasServiceToolComponent>(tool, out var component) && component.Mode is KiasServiceMode.Diagnose or KiasServiceMode.Monitor) Refresh(tool);
            }
        }
        const int gridBudget = 4;
        for (var i = 0; i < gridBudget && _pendingRefresh.TryDequeue(out var grid); i++)
        {
            _queuedRefresh.Remove(grid);
            if (!TryComp<KiasGridComponent>(grid, out var runtime)) continue;
            foreach (var uid in runtime.Devices)
            {
                if (_ui.IsUiOpen(uid, UiKey(uid))) Refresh(uid);
            }
        }
    }

    private void OnAnnouncement(ref KiasAnnouncementEvent args)
    {
        if (!TryComp<KiasGridComponent>(args.Grid, out var runtime) || !runtime.Active || !runtime.Online.Any(HasComp<KiasSpeakerComponent>))
            return;
        Announce(args.Grid, args.Message, args.Warning, args.Speaker, args.Group, args.Key, args.Channel);
    }

    private void OnAvailability(ref KiasAvailabilityChangedEvent args)
    {
        if (!args.Active)
        {
            _speechGate.Remove(args.Grid);
            _audioGate.Remove(args.Grid);
            if (TryComp<KiasGridComponent>(args.Grid, out var runtime))
            {
                foreach (var uid in runtime.Devices)
                {
                    if (TryComp<KiasSpeakerComponent>(uid, out var speaker) && !CanFinishShutdownTone(uid, speaker))
                        speaker.Tone = _audio.Stop(speaker.Tone);
                }
            }
        }
    }
    private bool CanFinishShutdownTone(EntityUid uid, KiasSpeakerComponent speaker) => speaker.FinishesShutdownTone
        && _power.IsPowered(uid) && Transform(uid).GridUid is { } grid && TryComp<KiasGridComponent>(grid, out var runtime)
        && runtime.Core is { } core && TryComp<KiasCoreComponent>(core, out var component) && !component.Enabled;

    private void OnSpeakerShutdown(Entity<KiasSpeakerComponent> ent, ref ComponentShutdown args) => ent.Comp.Tone = _audio.Stop(ent.Comp.Tone);
    private void OnGridRemoval(GridRemovalEvent args)
    {
        _speechGate.Remove(args.EntityUid);
        _audioGate.Remove(args.EntityUid);
    }

    private void OnSignal(Entity<KiasSpeakerComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port != "KiasAnnounce" || !_kias.IsOnline(ent)
            || Transform(ent).GridUid is not { } grid)
            return;
        var trigger = args.Trigger;
        var sourcePort = args.SourcePort;
        if (trigger is { } source && (TerminatingOrDeleted(source) || Transform(source).GridUid != grid
            || HasComp<KiasDeviceComponent>(source) && !_kias.IsOnline(source)))
            return;
        var linkedMessage = ent.Comp.Links.FirstOrDefault(link => link.Source == trigger && link.SourcePort == sourcePort)?.Message;
        var message = string.IsNullOrWhiteSpace(linkedMessage) ? ent.Comp.Message : linkedMessage;
        if (!string.IsNullOrWhiteSpace(message))
            Announce(grid, message, selected: ent.Owner, key: $"link:{trigger}:{sourcePort}:{ent.Owner}");
    }

    private void Announce(EntityUid grid, string message, bool warning = false, EntityUid? selected = null, string group = "", string key = "", KiasAudioChannel? requestedChannel = null)
    {
        var speakers = Comp<KiasGridComponent>(grid).Online.Where(uid => _kias.IsOnline(uid)
            && TryComp<KiasSpeakerComponent>(uid, out var speaker)
            && (selected == null || selected == uid)
            && (string.IsNullOrWhiteSpace(group) || group == "SHIP" || speaker.Group == group)).ToArray();
        if (speakers.Length == 0)
            return;
        var settings = AudioSettings(grid);
        var alert = Comp<KiasGridComponent>(grid).Core is { } core && TryComp<KiasProtocolComponent>(core, out var protocol) ? protocol.Alert : KiasAlert.Normal;
        var channel = requestedChannel ?? (alert switch { KiasAlert.Emergency => KiasAudioChannel.Emergency, KiasAlert.Battle => KiasAudioChannel.Battle,
            _ => warning ? KiasAudioChannel.Warning : KiasAudioChannel.Notification });
        key = string.IsNullOrEmpty(key) ? $"{selected}:{group}:{message}" : key;
        var speak = _speechGate.Allow(grid, key, _timing.CurTime, settings?.SpeechCooldown ?? 5, (int) channel, out var repeated);
        var playTone = _audioGate.Allow(grid, key, _timing.CurTime, settings?.ToneCooldown ?? 10, (int) channel, out _);
        if (repeated > 0) message += $" ×{repeated + 1}";
        foreach (var speaker in speakers)
        {
            if (speak) _chat.TrySendInGameICMessage(speaker, message, InGameICChatType.Speak, hideChat: false,
                checkRadioPrefix: false, ignoreActionBlocker: true);
            if (playTone)
            {
                PlayTone(speaker, settings?.Preset(channel) ?? KiasTonePreset.Chime);
            }
        }
    }
}
