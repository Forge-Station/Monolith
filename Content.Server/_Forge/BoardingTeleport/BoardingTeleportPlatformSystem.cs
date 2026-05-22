using Content.Server.EUI;
using Content.Server._Mono.Radar;
using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Server.Stunnable;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared._Forge.BoardingTeleport.Components;
using Content.Shared._Mono.Radar;
using Content.Shared.Database;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceNetwork;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Sprite;
using Content.Shared.Tiles;
using System.Linq;
using System.Numerics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.BoardingTeleport;

public sealed class BoardingTeleportPlatformSystem : EntitySystem
{
    private const int WarmupPulseSteps = 3;

    private readonly HashSet<EntityUid> _chargingPlatforms = new();

    private sealed class PendingReturnConfirm
    {
        public required EntityUid Platform;
        public required BoardingTeleportReturnConfirmKind Kind;
        public float ExplosionRisk;
    }

    private readonly Dictionary<EntityUid, PendingReturnConfirm> _pendingReturnConfirmByUser = new();

    [Dependency] private readonly BoardingTeleportConsoleSystem _console = default!;
    [Dependency] private readonly BoardingTeleportLockSystem _lock = default!;
    [Dependency] private readonly DeviceListSystem _deviceList = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly ActorSystem _actors = default!;
    [Dependency] private readonly EuiManager _euis = default!;
    [Dependency] private readonly SharedScaleVisualsSystem _scaleVisuals = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingTeleportPlatformComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, ComponentShutdown>(OnPlatformShutdown);
        SubscribeLocalEvent<BoardingTeleportSyncVolleyEvent>(OnSyncVolley);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        ClearPendingReturnConfirm(args.Entity);
        CancelChargesForUser(args.Entity);
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        var uid = args.Entity;
        ClearPendingReturnConfirm(uid);
        CancelChargesForUser(uid);
    }

    private void CancelChargesForUser(EntityUid user)
    {
        foreach (var platformUid in _chargingPlatforms.ToArray())
        {
            if (!TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform) ||
                platform.ActiveChargeUser != user)
            {
                continue;
            }

            CancelPendingTeleport(platformUid, platform);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        PruneStaleReturnConfirms();

        if (_chargingPlatforms.Count == 0)
            return;

        _chargingPlatforms.RemoveWhere(uid => !Exists(uid) || !TryComp<BoardingTeleportPlatformComponent>(uid, out var p) || !p.DeparturePending);

        foreach (var platformUid in _chargingPlatforms)
        {
            if (!TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform))
                continue;

            if (platform.ActiveChargeUser is { } chargeUser &&
                (!Exists(chargeUser) || _mobState.IsDead(chargeUser)))
            {
                CancelPendingTeleport(platformUid, platform);
                continue;
            }

            platform.PendingCountdownElapsed += frameTime;
            TickPendingCountdownAudio(platformUid, platform);

            platform.PendingLockCheckAccumulator += frameTime;
            if (platform.PendingLockCheckAccumulator < BoardingTeleportConstants.ChargeLockCheckIntervalSeconds)
                continue;

            platform.PendingLockCheckAccumulator = 0f;

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

        var platformCoords = Transform(platformUid).Coordinates;
        var found = false;

        foreach (var user in _lookup.GetEntitiesInRange(platformCoords, platform.ActivationRadius))
        {
            if (!TryComp<MobStateComponent>(user, out _) || _mobState.IsDead(user))
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

        if (!TryResolveSignalUser(args, out var user))
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
            PromptReturnConfirm(ent, user, anchor, BoardingTeleportReturnConfirmKind.Emergency);
            return;
        }

        if (!expired && TryGetEarlyReturnRisk(anchor, out var explosionRisk))
        {
            PromptReturnConfirm(ent, user, anchor, BoardingTeleportReturnConfirmKind.Early, explosionRisk);
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

    private bool TryGetEarlyReturnRisk(BoardingTeleportAnchorComponent anchor, out float explosionRisk)
    {
        explosionRisk = 0f;

        if (anchor.ExpiresAt is not { } expires || anchor.ReturnWindowSeconds <= 0.01f)
            return false;

        var remaining = (float) (expires - _timing.CurTime).TotalSeconds;
        if (remaining <= 0f)
            return false;

        if (!BoardingTeleportBalance.RequiresEarlyReturnConfirm(remaining, anchor.ReturnWindowSeconds))
            return false;

        explosionRisk = BoardingTeleportBalance.ComputeEarlyReturnExplosionRisk(remaining, anchor.ReturnWindowSeconds);
        return true;
    }

    private void PromptReturnConfirm(
        Entity<BoardingTeleportPlatformComponent> ent,
        EntityUid user,
        BoardingTeleportAnchorComponent anchor,
        BoardingTeleportReturnConfirmKind kind,
        float explosionRisk = 0f)
    {
        _pendingReturnConfirmByUser.Remove(user);

        if (!_actors.TryGetSession(user, out var session) || session == null)
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-emergency-return-no-session"), user, user);
            return;
        }

        _pendingReturnConfirmByUser[user] = new PendingReturnConfirm
        {
            Platform = ent.Owner,
            Kind = kind,
            ExplosionRisk = explosionRisk,
        };

        string title;
        string message;
        string confirmButton;

        if (kind == BoardingTeleportReturnConfirmKind.Emergency)
        {
            var riskPercent = BoardingTeleportConstants.EmergencyReturnRisk * 100f;
            title = Loc.GetString("boarding-teleport-emergency-return-confirm-title");
            message = Loc.GetString("boarding-teleport-emergency-return-confirm-message",
                ("seconds", $"{BoardingTeleportConstants.EmergencyReturnDelay:0}"),
                ("risk", $"{riskPercent:0}"),
                ("scatter", $"{BoardingTeleportConstants.EmergencyReturnScatter:0.#}"));
            confirmButton = Loc.GetString("boarding-teleport-emergency-return-confirm-button");
        }
        else
        {
            var remaining = anchor.ExpiresAt is { } expires
                ? MathF.Max(0f, (float) (expires - _timing.CurTime).TotalSeconds)
                : 0f;
            var riskPercent = explosionRisk * 100f;
            title = Loc.GetString("boarding-teleport-early-return-confirm-title");
            message = Loc.GetString("boarding-teleport-early-return-confirm-message",
                ("remaining", $"{remaining:0}"),
                ("risk", $"{riskPercent:0}"));
            confirmButton = Loc.GetString("boarding-teleport-early-return-confirm-button");
        }

        _euis.OpenEui(new BoardingTeleportEmergencyReturnEui(ent.Owner, user, title, message, confirmButton, kind, this), session);
    }

    public void OnReturnConfirmEuiClosed(EntityUid user)
    {
        _pendingReturnConfirmByUser.Remove(user);
    }

    public void CompleteReturnConfirmResponse(
        EntityUid user,
        EntityUid platformUid,
        BoardingTeleportReturnConfirmKind kind,
        bool accepted)
    {
        if (!_pendingReturnConfirmByUser.TryGetValue(user, out var pending) || pending.Kind != kind)
            return;

        _pendingReturnConfirmByUser.Remove(user);

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

        if (kind == BoardingTeleportReturnConfirmKind.Emergency)
        {
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
            return;
        }

        if (!_console.TryResolveHome(platformUid, platform, anchor, user, out var home, out _))
        {
            RemCompDeferred<BoardingTeleportAnchorComponent>(user);
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-home-invalid"), user, user);
            return;
        }

        BeginDelayedTeleport(
            (platformUid, platform),
            user,
            home,
            null,
            null,
            requirePlatform: false,
            returning: true,
            earlyReturn: true,
            earlyReturnExplosionRisk: pending.ExplosionRisk);
    }

    public void ClearPendingReturnConfirm(EntityUid user)
    {
        _pendingReturnConfirmByUser.Remove(user);
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
        bool earlyReturn = false,
        float earlyReturnExplosionRisk = 0f,
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
        ent.Comp.PendingEarlyReturn = earlyReturn;
        ent.Comp.PendingEarlyReturnExplosionRisk = earlyReturnExplosionRisk;
        ent.Comp.PendingReturning = returning;
        ent.Comp.PendingLandingCoordinates = destination;
        ent.Comp.PendingDetectionBlipSpawned = false;
        ent.Comp.PendingDetectionBlipGrid = null;
        ent.Comp.PendingLockCheckAccumulator = 0f;
        ent.Comp.PendingCountdownElapsed = 0f;
        ent.Comp.PendingCountdownLastTick = -1;
        ent.Comp.PendingCountdownTotalSeconds = 0;
        Dirty(ent);

        var delay = emergencyReturn
            ? BoardingTeleportConstants.EmergencyReturnDelay
            : BoardingTeleportBalance.ComputeDepartureDelay(ent.Comp.DepartureDelay, mode, distanceScale, returning);
        var cancel = new System.Threading.CancellationTokenSource();

        ent.Comp.DeparturePending = true;
        ent.Comp.PendingCountdownTotalSeconds = Math.Max(1, (int) MathF.Ceiling(delay));
        _chargingPlatforms.Add(ent.Owner);
        Dirty(ent);
        UpdatePlatformAppearance(ent.Owner, ent.Comp);
        var platformCoords = Transform(ent.Owner).Coordinates;
        _audio.PlayPvs(ent.Comp.ActivationSound, platformCoords);
        if (destination.IsValid(EntityManager))
            _audio.PlayPvs(ent.Comp.ActivationSound, destination);

        TickPendingCountdownAudio(ent.Owner, ent.Comp);

        if (!returning && console != null)
            TrySpawnDetectionBlip(ent.Owner, ent.Comp, console);

        if (returning)
        {
            var messageKey = emergencyReturn
                ? "boarding-teleport-platform-emergency-return-started"
                : earlyReturn
                    ? "boarding-teleport-platform-early-return-started"
                    : "boarding-teleport-platform-return-started";
            var popupType = emergencyReturn || earlyReturn ? PopupType.MediumCaution : PopupType.Medium;
            _popup.PopupEntity(Loc.GetString(messageKey, ("seconds", delay)), user, user, popupType);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-departure-delay", ("seconds", delay)), ent.Owner, user);
        }

        Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(delay), () =>
            CompletePendingTeleport(ent.Owner, chargeToken, user, destination, consoleUid, requirePlatform, returning, emergencyReturn, earlyReturn, earlyReturnExplosionRisk, mode, distanceScale),
            cancel.Token);

        ent.Comp.PendingCancel = cancel;
    }

    private bool TryValidatePendingLock(EntityUid platformUid, BoardingTeleportPlatformComponent platform, out BoardingTeleportStatus status)
    {
        status = BoardingTeleportStatus.None;

        if (platform.PendingEmergencyReturn || platform.PendingReturning || platform.PendingEarlyReturn)
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
        bool requirePlatform,
        bool returning,
        bool emergencyReturn,
        bool earlyReturn,
        float earlyReturnExplosionRisk,
        BoardingTeleportInsertionMode mode,
        float distanceScale)
    {
        if (!Exists(platformUid) || !TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform))
            return;

        if (platform.ChargeToken != chargeToken || platform.ActiveChargeUser != user)
            return;

        var savedEarlyReturn = platform.PendingEarlyReturn;
        var savedEarlyReturnRisk = platform.PendingEarlyReturnExplosionRisk;
        var savedLanding = platform.PendingLandingCoordinates ?? pendingLanding;
        ClearPendingChargeState(platformUid, platform);

        TryComp(consoleUid, out BoardingTeleportConsoleComponent? console);

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

            distanceScale = _console.GetDistanceScale(platformUid, destination);
            SpawnLandingWarmupBurst(destination, platform, mode);
        }

        if (!destination.IsValid(EntityManager))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-landing-invalid"), user, user);
            return;
        }

        if (!returning &&
            TryComp<ProtectedGridComponent>(destination.EntityId, out var protectedGrid) &&
            protectedGrid.PreventTeleportation)
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-status-TargetGridProtected"), user, user);
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
            anchor.CreatedAt = _timing.CurTime;
            anchor.ReturnWindowSeconds = platform.ReturnWindowSeconds;
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

        var earlyReturnSwelling = returning && savedEarlyReturn && ScheduleEarlyReturnCatastrophe(user, savedEarlyReturnRisk);

        if (!returning && consoleUid is { } cUid && console != null)
            _console.StartEngineCooldown(cUid, console);

        var targetGridNet = console?.TargetGrid is { } tg ? GetNetEntity(tg) : NetEntity.Invalid;
        var outcome = earlyReturnSwelling ? "early-return-swelling" : destabilized ? "destabilized" : "stable";
        _adminLogger.Add(LogType.Teleport, LogImpact.Medium,
            $"{ToPrettyString(user):player} boarding-teleport mode={mode} emergency={emergencyReturn} earlyReturn={savedEarlyReturn} targetGrid={targetGridNet} distScale={distanceScale:0.00} outcome={outcome} from {source} to {destination} via {ToPrettyString(platformUid)}");

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
        platform.PendingEarlyReturn = false;
        platform.PendingEarlyReturnExplosionRisk = 0f;
        platform.PendingReturning = false;
        platform.PendingDetectionBlipSpawned = false;
        ClearPendingDetectionBlip(platform);
        platform.PendingLockCheckAccumulator = 0f;
        platform.PendingCountdownElapsed = 0f;
        platform.PendingCountdownLastTick = -1;
        platform.PendingCountdownTotalSeconds = 0;
        DisposePendingCancel(platform);
        _chargingPlatforms.Remove(platformUid);
        Dirty(platformUid, platform);
        UpdatePlatformAppearance(platformUid, platform);
    }

    private void CancelPendingTeleport(EntityUid platformUid, BoardingTeleportPlatformComponent? platform = null, bool notify = false)
    {
        platform ??= CompOrNull<BoardingTeleportPlatformComponent>(platformUid);
        if (platform == null)
            return;

        DisposePendingCancel(platform);
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

    private void SpawnLandingWarmupBurst(
        EntityCoordinates coordinates,
        BoardingTeleportPlatformComponent component,
        BoardingTeleportInsertionMode mode)
    {
        if (!coordinates.IsValid(EntityManager))
            return;

        for (var i = 0; i < WarmupPulseSteps; i++)
        {
            var progress = (i + 1f) / WarmupPulseSteps;
            var pulseProto = GetWarmupPrototype(mode, progress);
            Spawn(pulseProto, coordinates);
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
        platform.PendingDetectionBlipGrid = targetGrid;
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
            if (!Exists(targetGrid))
                return;

            if (TryComp<BoardingTeleportPlatformComponent>(platformUid, out var current) &&
                current.PendingDetectionBlipGrid == targetGrid)
            {
                return;
            }

            RemCompDeferred<RadarBlipComponent>(targetGrid);
        });
    }

    private void ClearPendingDetectionBlip(BoardingTeleportPlatformComponent platform)
    {
        if (platform.PendingDetectionBlipGrid is not { } grid)
            return;

        platform.PendingDetectionBlipGrid = null;

        if (Exists(grid) && HasComp<RadarBlipComponent>(grid))
            RemCompDeferred<RadarBlipComponent>(grid);
    }

    private static void DisposePendingCancel(BoardingTeleportPlatformComponent platform)
    {
        var cancel = platform.PendingCancel;
        platform.PendingCancel = null;
        if (cancel == null)
            return;

        cancel.Cancel();
        cancel.Dispose();
    }

    private void PruneStaleReturnConfirms()
    {
        if (_pendingReturnConfirmByUser.Count == 0)
            return;

        foreach (var user in _pendingReturnConfirmByUser.Keys.ToArray())
        {
            if (!Exists(user))
                _pendingReturnConfirmByUser.Remove(user);
        }
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

    private void TickPendingCountdownAudio(EntityUid platformUid, BoardingTeleportPlatformComponent platform)
    {
        if (!platform.DeparturePending || platform.PendingCountdownTotalSeconds <= 0)
            return;

        var tick = Math.Min(
            (int) MathF.Floor(platform.PendingCountdownElapsed),
            platform.PendingCountdownTotalSeconds - 1);

        if (tick <= platform.PendingCountdownLastTick)
            return;

        for (var t = platform.PendingCountdownLastTick + 1; t <= tick; t++)
        {
            var remaining = platform.PendingCountdownTotalSeconds - t;
            var sound = remaining <= platform.CountdownFinalThreshold
                ? platform.CountdownFinalSound
                : platform.CountdownSound;

            _audio.PlayPvs(sound, Transform(platformUid).Coordinates);

            if (platform.PendingLandingCoordinates is { } dest && dest.IsValid(EntityManager))
                _audio.PlayPvs(sound, dest);
        }

        platform.PendingCountdownLastTick = tick;
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

    private bool ScheduleEarlyReturnCatastrophe(EntityUid user, float explosionRisk)
    {
        if (explosionRisk <= 0f || !_random.Prob(explosionRisk))
            return false;

        _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-early-return-swelling"), user, user, PopupType.MediumCaution);
        _stun.TryParalyze(
            user,
            TimeSpan.FromSeconds(BoardingTeleportConstants.EarlyReturnCatastropheDelaySeconds),
            true);

        var steps = BoardingTeleportConstants.EarlyReturnCatastropheSwellingSteps;
        var delay = BoardingTeleportConstants.EarlyReturnCatastropheDelaySeconds;
        var scaleFactor = Math.Clamp(explosionRisk, 0f, 1f);
        var targetScale = BoardingTeleportConstants.EarlyReturnCatastropheMinScale +
                          (BoardingTeleportConstants.EarlyReturnCatastropheMaxScale -
                           BoardingTeleportConstants.EarlyReturnCatastropheMinScale) * scaleFactor;
        var targetVec = new Vector2(targetScale, targetScale);

        for (var i = 0; i < steps; i++)
        {
            var stepIndex = i;
            var t = (stepIndex + 1f) / steps;
            Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(delay * t), () =>
            {
                if (!Exists(user))
                    return;

                if (TryComp<MobStateComponent>(user, out var mobState) && _mobState.IsDead(user, mobState))
                    return;

                if (stepIndex < steps - 1)
                {
                    var scale = Vector2.Lerp(Vector2.One, targetVec, t);
                    _scaleVisuals.SetSpriteScale(user, scale);
                    return;
                }

                DetonateEarlyReturnCatastrophe(user, explosionRisk);
            });
        }

        _adminLogger.Add(LogType.Teleport, LogImpact.Medium,
            $"{ToPrettyString(user):player} boarding-teleport early-return swelling scheduled risk={explosionRisk:0.00}");
        return true;
    }

    private void DetonateEarlyReturnCatastrophe(EntityUid user, float explosionRisk)
    {
        if (!Exists(user))
            return;

        if (TryComp<MobStateComponent>(user, out var mobState) && _mobState.IsDead(user, mobState))
            return;

        if (HasComp<ScaleVisualsComponent>(user))
            RemCompDeferred<ScaleVisualsComponent>(user);

        _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-early-return-catastrophe"), user, user, PopupType.LargeCaution);

        var intensity = 35f + explosionRisk * 40f;
        var maxTile = 4f + explosionRisk * 3f;
        _explosion.QueueExplosion(user, "Default", intensity, 5f, maxTile, user: user);
        _body.GibBody(user, gibOrgans: true);
        _adminLogger.Add(LogType.Explosion, LogImpact.High,
            $"{ToPrettyString(user):player} boarding-teleport early-return catastrophe risk={explosionRisk:0.00} intensity={intensity:0}");
    }

    private bool TryResolveSignalUser(SignalReceivedEvent args, out EntityUid user)
    {
        if (args.Data?.TryGetValue(BoardingTeleportRemoteSystem.UserPayloadKey, out var rawUser) == true &&
            rawUser is NetEntity netUser &&
            TryGetEntity(netUser, out var payloadUser) &&
            payloadUser is { } resolvedUser &&
            HasComp<MobStateComponent>(resolvedUser))
        {
            user = resolvedUser;
            return true;
        }

        if (args.Trigger is { } trigger && TryGetSignalUser(trigger, out user))
            return true;

        user = default;
        return false;
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

        var handsQuery = EntityQueryEnumerator<HandsComponent>();
        while (handsQuery.MoveNext(out var holder, out var hands))
        {
            foreach (var hand in hands.Hands.Values)
            {
                if (hand.HeldEntity == trigger)
                {
                    user = holder;
                    return true;
                }
            }
        }

        user = default;
        return false;
    }
}
