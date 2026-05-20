using Content.Server.EUI;
using Content.Server._Mono.Radar;
using Content.Server.Administration.Logs;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Stunnable;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared._Forge.BoardingTeleport.Components;
using Content.Shared._Mono.Radar;
using Content.Shared.Database;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.BoardingTeleport;

public sealed class BoardingTeleportPlatformSystem : EntitySystem
{
    private const int WarmupPulseSteps = 6;

    private readonly Dictionary<EntityUid, EntityUid> _pendingEmergencyConfirmByUser = new();

    [Dependency] private readonly BoardingTeleportConsoleSystem _console = default!;
    [Dependency] private readonly BoardingTeleportLockSystem _lock = default!;
    [Dependency] private readonly DeviceListSystem _deviceList = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly ActorSystem _actors = default!;
    [Dependency] private readonly EuiManager _euis = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingTeleportPlatformComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, ComponentShutdown>(OnPlatformShutdown);
        SubscribeLocalEvent<BoardingTeleportSyncVolleyEvent>(OnSyncVolley);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BoardingTeleportPlatformComponent>();
        while (query.MoveNext(out var platformUid, out var platform))
        {
            if (!platform.DeparturePending)
                continue;

            platform.PendingLockCheckAccumulator += frameTime;
            if (platform.PendingLockCheckAccumulator < BoardingTeleportConstants.ChargeLockCheckIntervalSeconds)
                continue;

            platform.PendingLockCheckAccumulator = 0f;
            Dirty(platformUid, platform);

            if (!TryValidatePendingLock(platformUid, platform, out _))
                CancelPendingTeleport(platformUid, platform, notify: true);
        }
    }

    private void OnSyncVolley(BoardingTeleportSyncVolleyEvent args)
    {
        if (!TryComp<BoardingTeleportConsoleComponent>(args.ConsoleUid, out var console))
            return;

        var launched = 0;
        foreach (var platformUid in _lock.GetOrderedPlatforms(args.ConsoleUid))
        {
            if (!TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform))
                continue;

            if (!TryLaunchVolleyUser(platformUid, platform, args.ConsoleUid, console))
                continue;

            launched++;
        }

        if (launched == 0)
            _popup.PopupEntity(Loc.GetString("boarding-teleport-console-volley-none"), args.ConsoleUid);
        else
            _popup.PopupEntity(Loc.GetString("boarding-teleport-console-volley-started", ("count", launched)), args.ConsoleUid);
    }

    private bool TryLaunchVolleyUser(EntityUid platformUid, BoardingTeleportPlatformComponent platform, EntityUid consoleUid, BoardingTeleportConsoleComponent console)
    {
        if (platform.DeparturePending || _timing.CurTime < platform.NextUse || !this.IsPowered(platformUid, EntityManager))
            return false;

        var platformPos = _transform.GetWorldPosition(platformUid);
        var found = false;

        var mobQuery = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobQuery.MoveNext(out var user, out _, out var xform))
        {
            if (_mobState.IsDead(user))
                continue;

            if ((platformPos - _transform.GetWorldPosition(user)).LengthSquared() > platform.ActivationRadius * platform.ActivationRadius)
                continue;

            if (TryComp<BoardingTeleportAnchorComponent>(user, out _))
                continue;

            TryDepart((platformUid, platform), user, consoleUid, console);
            found = true;
            break;
        }

        return found;
    }

    private void OnMapInit(EntityUid uid, BoardingTeleportPlatformComponent component, ref MapInitEvent args)
    {
        UpdatePlatformAppearance(uid, component);
    }

    private void OnPowerChanged(EntityUid uid, BoardingTeleportPlatformComponent component, ref PowerChangedEvent args)
    {
        UpdatePlatformAppearance(uid, component);
    }

    private void OnPlatformShutdown(Entity<BoardingTeleportPlatformComponent> ent, ref ComponentShutdown args)
    {
        CancelPendingTeleport(ent.Owner, ent.Comp);
    }

    private void UpdatePlatformAppearance(EntityUid uid, BoardingTeleportPlatformComponent platform)
    {
        BoardingTeleportPlatformState visual;
        if (!this.IsPowered(uid, EntityManager))
            visual = BoardingTeleportPlatformState.Offline;
        else if (platform.DeparturePending)
            visual = BoardingTeleportPlatformState.Charging;
        else
            visual = BoardingTeleportPlatformState.Idle;

        _appearance.SetData(uid, BoardingTeleportPlatformVisuals.State, visual);
    }

    private void OnSignalReceived(Entity<BoardingTeleportPlatformComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port != ent.Comp.ReceiverPort || args.Trigger == null)
            return;

        if (!TryGetSignalUser(args.Trigger.Value, out var user))
            return;

        if (TryComp<BoardingTeleportAnchorComponent>(user, out var anchor))
        {
            if (!TryGetEntity(anchor.HomePlatform, out var homeUidOptional) ||
                homeUidOptional is not { } homeUid ||
                !TryComp<BoardingTeleportPlatformComponent>(homeUid, out var homePlatform))
            {
                RemCompDeferred<BoardingTeleportAnchorComponent>(user);
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-home-invalid"), user, user);
                return;
            }

            if (homePlatform.DeparturePending)
            {
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-pending"), homeUid, user);
                return;
            }

            if (_timing.CurTime < homePlatform.NextUse)
            {
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-cooldown"), user, user);
                return;
            }

            if (!this.IsPowered(homeUid, EntityManager))
            {
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-unpowered"), homeUid, user);
                return;
            }

            TryReturn((homeUid, homePlatform), user, anchor);
            return;
        }

        if (ent.Comp.DeparturePending)
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-pending"), ent.Owner, user);
            return;
        }

        if (_timing.CurTime < ent.Comp.NextUse)
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-cooldown"), user, user);
            return;
        }

        if (!this.IsPowered(ent.Owner, EntityManager))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-unpowered"), ent.Owner, user);
            return;
        }

        if (ent.Comp.LinkedConsole is not { } consoleUid ||
            !TryComp<BoardingTeleportConsoleComponent>(consoleUid, out var console))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-no-console"), ent.Owner, user);
            return;
        }

        TryDepart(ent, user, consoleUid, console);
    }

    private void TryDepart(Entity<BoardingTeleportPlatformComponent> ent, EntityUid user, EntityUid consoleUid, BoardingTeleportConsoleComponent console)
    {
        if (!IsStandingOnPlatform(ent.Owner, user, ent.Comp))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-not-on-platform"), user, user);
            return;
        }

        var landing = _console.GetLandingForPlatform(consoleUid, console, ent.Owner);
        if (landing is not { } validLanding)
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-no-target"), ent.Owner, user);
            return;
        }

        if (!_console.TryValidateDeparture(consoleUid, console, validLanding, out var status))
        {
            _popup.PopupEntity(Loc.GetString($"boarding-teleport-status-{status}"), user, user);
            return;
        }

        var distanceScale = _console.GetDistanceScale(ent.Owner, validLanding);
        BeginDelayedTeleport(ent, user, validLanding, consoleUid, console, requirePlatform: true, returning: false, mode: console.Mode, distanceScale: distanceScale);
    }

    private void TryReturn(Entity<BoardingTeleportPlatformComponent> ent, EntityUid user, BoardingTeleportAnchorComponent anchor)
    {
        var expired = anchor.ExpiresAt is { } expires && _timing.CurTime >= expires;
        if (expired && !anchor.EmergencyReturnUsed)
        {
            PromptEmergencyReturn(ent, user, anchor);
            return;
        }

        if (!_console.TryResolveHome(ent.Owner, ent.Comp, anchor, user, out var home, out var unreachable))
        {
            RemCompDeferred<BoardingTeleportAnchorComponent>(user);
            if (unreachable)
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-home-invalid"), user, user);
            else if (expired)
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-return-expired"), user, user);
            else
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-wrong-platform"), user, user);
            return;
        }

        BeginDelayedTeleport(ent, user, home, null, null, requirePlatform: false, returning: true, distanceScale: 1f);
    }

    private void PromptEmergencyReturn(
        Entity<BoardingTeleportPlatformComponent> ent,
        EntityUid user,
        BoardingTeleportAnchorComponent anchor)
    {
        if (_pendingEmergencyConfirmByUser.ContainsKey(user))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-emergency-return-pending"), user, user);
            return;
        }

        if (!_actors.TryGetSession(user, out var session) || session == null)
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-emergency-return-no-session"), user, user);
            return;
        }

        _pendingEmergencyConfirmByUser[user] = ent.Owner;

        var riskPercent = BoardingTeleportConstants.EmergencyReturnRisk * 100f;
        var title = Loc.GetString("boarding-teleport-emergency-return-confirm-title");
        var message = Loc.GetString("boarding-teleport-emergency-return-confirm-message",
            ("seconds", $"{BoardingTeleportConstants.EmergencyReturnDelay:0}"),
            ("risk", $"{riskPercent:0}"),
            ("scatter", $"{BoardingTeleportConstants.EmergencyReturnScatter:0.#}"));

        _euis.OpenEui(new BoardingTeleportEmergencyReturnEui(ent.Owner, user, title, message, this), session);
    }

    public void OnEmergencyReturnEuiClosed(EntityUid user)
    {
        if (_pendingEmergencyConfirmByUser.ContainsKey(user))
            _pendingEmergencyConfirmByUser.Remove(user);
    }

    public void CompleteEmergencyReturnResponse(EntityUid user, EntityUid platformUid, bool accepted)
    {
        _pendingEmergencyConfirmByUser.Remove(user);

        if (!accepted)
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-emergency-return-cancelled"), user, user);
            return;
        }

        if (!Exists(platformUid) ||
            !TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform) ||
            !TryComp<BoardingTeleportAnchorComponent>(user, out var anchor))
        {
            return;
        }

        var expired = anchor.ExpiresAt is { } expires && _timing.CurTime >= expires;
        if (!expired || anchor.EmergencyReturnUsed || anchor.HomePlatform != GetNetEntity(platformUid))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-return-expired"), user, user);
            return;
        }

        anchor.EmergencyReturnUsed = true;
        Dirty(user, anchor);

        BeginDelayedTeleport(
            (platformUid, platform),
            user,
            default,
            null,
            null,
            requirePlatform: false,
            returning: true,
            emergencyReturn: true);
    }

    public void ClearPendingEmergencyConfirm(EntityUid user)
    {
        _pendingEmergencyConfirmByUser.Remove(user);
    }

    private void BeginDelayedTeleport(
        Entity<BoardingTeleportPlatformComponent> ent,
        EntityUid user,
        EntityCoordinates destination,
        EntityUid? consoleUid,
        BoardingTeleportConsoleComponent? console,
        bool requirePlatform,
        bool returning = false,
        bool emergencyReturn = false,
        BoardingTeleportInsertionMode mode = BoardingTeleportInsertionMode.Stealth,
        float distanceScale = 1f)
    {
        CancelPendingTeleport(ent.Owner, ent.Comp);

        ent.Comp.ChargeToken++;
        var chargeToken = ent.Comp.ChargeToken;
        ent.Comp.ActiveChargeUser = user;
        ent.Comp.PendingConsoleUid = consoleUid;
        ent.Comp.PendingMode = mode;
        ent.Comp.PendingDistanceScale = distanceScale;
        ent.Comp.PendingEmergencyReturn = emergencyReturn;
        ent.Comp.PendingReturning = returning;
        ent.Comp.PendingLandingCoordinates = destination;
        ent.Comp.PendingDetectionBlipSpawned = false;
        ent.Comp.PendingLockCheckAccumulator = 0f;
        Dirty(ent);

        var delay = emergencyReturn
            ? BoardingTeleportConstants.EmergencyReturnDelay
            : BoardingTeleportBalance.ComputeDepartureDelay(ent.Comp.DepartureDelay, mode, distanceScale, returning);
        var cancel = new System.Threading.CancellationTokenSource();

        ent.Comp.DeparturePending = true;
        Dirty(ent);
        UpdatePlatformAppearance(ent.Owner, ent.Comp);
        _audio.PlayPvs(ent.Comp.ActivationSound, Transform(ent.Owner).Coordinates);
        SpawnCountdownAudioSequence(ent.Owner, ent.Comp, delay, cancel.Token, chargeToken);

        if (!returning && destination.IsValid(EntityManager))
            SpawnWarmupSequence(ent.Owner, destination, ent.Comp, delay, cancel.Token, mode, consoleUid, console);

        if (returning)
        {
            var messageKey = emergencyReturn
                ? "boarding-teleport-platform-emergency-return-started"
                : "boarding-teleport-platform-return-started";
            var popupType = emergencyReturn ? PopupType.MediumCaution : PopupType.Medium;
            _popup.PopupEntity(Loc.GetString(messageKey, ("seconds", delay)), user, user, popupType);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-departure-delay", ("seconds", delay)), ent.Owner, user);
        }

        Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(delay), () =>
            CompletePendingTeleport(ent.Owner, chargeToken, user, destination, consoleUid, console, requirePlatform, returning, emergencyReturn, mode, distanceScale),
            cancel.Token);

        ent.Comp.PendingCancel = cancel;
    }

    private bool TryValidatePendingLock(EntityUid platformUid, BoardingTeleportPlatformComponent platform, out BoardingTeleportStatus status)
    {
        status = BoardingTeleportStatus.None;

        if (platform.PendingEmergencyReturn || platform.PendingReturning)
            return true;

        if (platform.PendingConsoleUid is not { } consoleUid ||
            !TryComp<BoardingTeleportConsoleComponent>(consoleUid, out var console))
        {
            status = BoardingTeleportStatus.NoEngine;
            return false;
        }

        if (platform.PendingLandingCoordinates is not { } landing)
        {
            status = BoardingTeleportStatus.InvalidLanding;
            return false;
        }

        return _console.TryValidateDeparture(consoleUid, console, landing, out status);
    }

    private void CompletePendingTeleport(
        EntityUid platformUid,
        uint chargeToken,
        EntityUid user,
        EntityCoordinates pendingLanding,
        EntityUid? consoleUid,
        BoardingTeleportConsoleComponent? console,
        bool requirePlatform,
        bool returning,
        bool emergencyReturn,
        BoardingTeleportInsertionMode mode,
        float distanceScale)
    {
        if (!Exists(platformUid) || !TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform))
            return;

        if (platform.ChargeToken != chargeToken || platform.ActiveChargeUser != user)
            return;

        var savedLanding = platform.PendingLandingCoordinates ?? pendingLanding;
        ClearPendingChargeState(platformUid, platform);

        if (!Exists(user) || _mobState.IsDead(user))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-charge-cancelled"), platformUid);
            return;
        }

        if (_timing.CurTime < platform.NextUse || !this.IsPowered(platformUid, EntityManager))
            return;

        if (requirePlatform && !IsStandingOnPlatform(platformUid, user, platform))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-not-on-platform"), user, user);
            return;
        }

        EntityCoordinates destination;

        if (returning)
        {
            if (!TryComp<BoardingTeleportAnchorComponent>(user, out var anchor))
            {
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-home-invalid"), user, user);
                return;
            }

            var homePlatform = GetEntity(anchor.HomePlatform);
            if (!Exists(homePlatform) || homePlatform != platformUid)
            {
                RemCompDeferred<BoardingTeleportAnchorComponent>(user);
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-home-invalid"), user, user);
                return;
            }

            destination = savedLanding.IsValid(EntityManager)
                ? savedLanding
                : Transform(homePlatform).Coordinates;

            if (!destination.IsValid(EntityManager))
            {
                RemCompDeferred<BoardingTeleportAnchorComponent>(user);
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-home-invalid"), user, user);
                return;
            }
        }
        else
        {
            if (consoleUid is not { } validConsoleUid || console is not { } validConsole)
                return;

            var landing = savedLanding;
            if (!_console.TryValidateDeparture(validConsoleUid, validConsole, landing, out var status))
            {
                _popup.PopupEntity(Loc.GetString($"boarding-teleport-status-{status}"), user, user);
                return;
            }

            if (!_console.TryResolveLanding(platformUid, platform, validConsole, landing, mode, distanceScale, out destination))
            {
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-landing-invalid"), user, user);
                return;
            }
        }

        if (!destination.IsValid(EntityManager))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-landing-invalid"), user, user);
            return;
        }

        if (returning)
        {
            RemCompDeferred<BoardingTeleportAnchorComponent>(user);
        }
        else
        {
            var anchor = EnsureComp<BoardingTeleportAnchorComponent>(user);
            anchor.HomePlatform = GetNetEntity(platformUid);
            anchor.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(platform.ReturnWindowSeconds);
            anchor.EmergencyReturnUsed = false;
            Dirty(user, anchor);
        }

        var source = Transform(user).Coordinates;
        var destabilized = !returning &&
                           TryApplyDestabilization(user, platformUid, destination, console, consoleUid, mode, distanceScale, platform.ExperimentalRisk, emergencyReturn);

        if (emergencyReturn && !destabilized && _random.Prob(BoardingTeleportConstants.EmergencyReturnRisk))
        {
            _stun.TryParalyze(user, TimeSpan.FromSeconds(2.5), true);
            destabilized = true;
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-destabilized"), user, user);
        }

        Teleport(user, destination, platform);
        StartCooldown((platformUid, platform));

        if (!returning && consoleUid is { } cUid && console != null)
            _console.StartEngineCooldown(cUid, console);

        var targetGridNet = console?.TargetGrid is { } tg ? GetNetEntity(tg) : NetEntity.Invalid;
        var outcome = destabilized ? "destabilized" : "stable";
        _adminLogger.Add(LogType.Teleport, LogImpact.Medium,
            $"{ToPrettyString(user):player} boarding-teleport mode={mode} emergency={emergencyReturn} targetGrid={targetGridNet} distScale={distanceScale:0.00} outcome={outcome} from {source} to {destination} via {ToPrettyString(platformUid)}");

        if (consoleUid is { } consoleEnt && console != null)
            _console.UpdateUi((consoleEnt, console));
    }

    private void ClearPendingChargeState(EntityUid platformUid, BoardingTeleportPlatformComponent platform)
    {
        platform.DeparturePending = false;
        platform.ActiveChargeUser = null;
        platform.PendingConsoleUid = null;
        platform.PendingLandingCoordinates = null;
        platform.PendingEmergencyReturn = false;
        platform.PendingReturning = false;
        platform.PendingDetectionBlipSpawned = false;
        platform.PendingLockCheckAccumulator = 0f;
        platform.PendingCancel?.Dispose();
        platform.PendingCancel = null;
        Dirty(platformUid, platform);
        UpdatePlatformAppearance(platformUid, platform);
    }

    private void CancelPendingTeleport(EntityUid platformUid, BoardingTeleportPlatformComponent? platform = null, bool notify = false)
    {
        platform ??= CompOrNull<BoardingTeleportPlatformComponent>(platformUid);
        if (platform == null)
            return;

        platform.PendingCancel?.Cancel();
        platform.ChargeToken++;

        if (!platform.DeparturePending)
            return;

        var user = platform.ActiveChargeUser;
        ClearPendingChargeState(platformUid, platform);

        if (notify)
        {
            if (user is { } validUser)
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-lock-broken"), validUser, validUser);
            else
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-lock-broken"), platformUid);
        }
    }

    private void Teleport(EntityUid user, EntityCoordinates coordinates, BoardingTeleportPlatformComponent component)
    {
        if (TryComp<PullableComponent>(user, out var pull) && _pulling.IsPulled(user, pull))
            _pulling.TryStopPull(user, pull);

        var source = Transform(user).Coordinates;
        SpawnTeleportEffect(source, component);
        _audio.PlayPvs(component.ArrivalSound, source);

        _transform.SetCoordinates(user, coordinates);
        SpawnTeleportEffect(coordinates, component);
        _audio.PlayPvs(component.ArrivalSound, coordinates);
    }

    private void SpawnWarmupSequence(
        EntityUid platformUid,
        EntityCoordinates coordinates,
        BoardingTeleportPlatformComponent component,
        float delaySeconds,
        System.Threading.CancellationToken cancelToken,
        BoardingTeleportInsertionMode mode,
        EntityUid? consoleUid,
        BoardingTeleportConsoleComponent? console)
    {
        for (var i = 0; i < WarmupPulseSteps; i++)
        {
            var pulse = i;
            var delay = TimeSpan.FromSeconds(delaySeconds * pulse / WarmupPulseSteps);
            Robust.Shared.Timing.Timer.Spawn(delay, () =>
            {
                if (!coordinates.EntityId.IsValid() || !Exists(coordinates.EntityId))
                    return;

                if (pulse == 0)
                    TrySpawnDetectionBlip(platformUid, component, console);

                var progress = (pulse + 1f) / WarmupPulseSteps;
                var pulseProto = GetWarmupPrototype(mode, progress);
                Spawn(pulseProto, coordinates);
            }, cancelToken);
        }
    }

    private void TrySpawnDetectionBlip(
        EntityUid platformUid,
        BoardingTeleportPlatformComponent platform,
        BoardingTeleportConsoleComponent? console)
    {
        if (platform.PendingDetectionBlipSpawned || console?.TargetGrid is not { } targetGrid)
            return;

        platform.PendingDetectionBlipSpawned = true;
        Dirty(platformUid, platform);

        var blip = EnsureComp<RadarBlipComponent>(targetGrid);
        blip.Config.Color = Color.FromHex("#ff6868");
        blip.Config.Shape = RadarBlipShape.Circle;
        blip.VisibleFromOtherGrids = true;
        blip.Enabled = true;

        _adminLogger.Add(LogType.Teleport, LogImpact.Low,
            $"Boarding teleport detection blip applied to {ToPrettyString(targetGrid)}");

        Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(BoardingTeleportConstants.DetectionBlipDurationSeconds), () =>
        {
            if (Exists(targetGrid))
                RemCompDeferred<RadarBlipComponent>(targetGrid);
        });
    }

    private static EntProtoId GetWarmupPrototype(BoardingTeleportInsertionMode mode, float progress)
    {
        return mode switch
        {
            BoardingTeleportInsertionMode.Stealth => progress >= 0.75f
                ? "BoardingTeleportWarmupEffectMedium"
                : "BoardingTeleportWarmupEffectStealth",
            BoardingTeleportInsertionMode.Rapid => progress >= 0.4f
                ? "BoardingTeleportWarmupEffectHigh"
                : "BoardingTeleportWarmupEffectMedium",
            _ => progress >= 0.75f
                ? "BoardingTeleportWarmupEffectHigh"
                : progress >= 0.4f
                    ? "BoardingTeleportWarmupEffectMedium"
                    : "BoardingTeleportWarmupEffect",
        };
    }

    private void SpawnCountdownAudioSequence(EntityUid platformUid, BoardingTeleportPlatformComponent component, float delaySeconds, System.Threading.CancellationToken cancelToken, uint chargeToken)
    {
        var totalSeconds = Math.Max(1, (int) MathF.Ceiling(delaySeconds));
        for (var i = 0; i < totalSeconds; i++)
        {
            var secondsPassed = i;
            Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(secondsPassed), () =>
            {
                if (!Exists(platformUid) ||
                    !TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform) ||
                    !platform.DeparturePending ||
                    platform.ChargeToken != chargeToken)
                {
                    return;
                }

                var remaining = totalSeconds - secondsPassed;
                var sound = remaining <= platform.CountdownFinalThreshold
                    ? platform.CountdownFinalSound
                    : platform.CountdownSound;
                _audio.PlayPvs(sound, Transform(platformUid).Coordinates);
            }, cancelToken);
        }
    }

    private bool TryApplyDestabilization(
        EntityUid user,
        EntityUid platformUid,
        EntityCoordinates destination,
        BoardingTeleportConsoleComponent? console,
        EntityUid? consoleUid,
        BoardingTeleportInsertionMode mode,
        float distanceScale,
        bool experimental,
        bool emergencyReturn)
    {
        var chance = emergencyReturn
            ? BoardingTeleportConstants.EmergencyReturnRisk
            : _console.GetDestabilizationChance(mode, distanceScale, console, consoleUid, platformUid, experimental);

        if (!_random.Prob(chance))
            return false;

        _stun.TryParalyze(user, TimeSpan.FromSeconds(2.5), true);
        _stun.TrySlowdown(user, TimeSpan.FromSeconds(8), true, walkSpeedMultiplier: 0.7f, runSpeedMultiplier: 0.6f);
        _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-destabilized"), user, user);
        _adminLogger.Add(LogType.Teleport, LogImpact.Medium,
            $"{ToPrettyString(user):player} suffered boarding teleport destabilization chance={chance:0.00} destination={destination}");
        return true;
    }

    private void SpawnTeleportEffect(EntityCoordinates coordinates, BoardingTeleportPlatformComponent component)
    {
        Spawn(component.TeleportEffect, coordinates);
    }

    private void StartCooldown(Entity<BoardingTeleportPlatformComponent> ent)
    {
        ent.Comp.NextUse = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.Cooldown);
        Dirty(ent);
    }

    private bool IsStandingOnPlatform(EntityUid platform, EntityUid user, BoardingTeleportPlatformComponent component)
    {
        var platformPos = _transform.GetWorldPosition(platform);
        var userPos = _transform.GetWorldPosition(user);
        return (platformPos - userPos).LengthSquared() <= component.ActivationRadius * component.ActivationRadius;
    }

    private bool TryGetSignalUser(EntityUid trigger, out EntityUid user)
    {
        var current = trigger;
        for (var i = 0; i < 16 && current.IsValid(); i++)
        {
            if (HasComp<MobStateComponent>(current))
            {
                user = current;
                return true;
            }

            var parent = Transform(current).ParentUid;
            if (parent == current || !parent.IsValid())
                break;

            current = parent;
        }

        user = default;
        return false;
    }
}
