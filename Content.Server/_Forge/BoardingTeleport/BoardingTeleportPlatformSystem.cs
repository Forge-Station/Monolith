using Content.Server.Administration.Logs;
using Content.Server.Power.EntitySystems;
using Content.Server.Stunnable;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared._Forge.BoardingTeleport.Components;
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
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.BoardingTeleport;

public sealed class BoardingTeleportPlatformSystem : EntitySystem
{
    private const int WarmupPulseSteps = 6;

    [Dependency] private readonly BoardingTeleportConsoleSystem _console = default!;
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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingTeleportPlatformComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<BoardingTeleportPlatformComponent, ComponentShutdown>(OnPlatformShutdown);
        SubscribeLocalEvent<BoardingTeleportAnchorComponent, MobStateChangedEvent>(OnAnchorMobStateChanged);
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

    private void OnAnchorMobStateChanged(Entity<BoardingTeleportAnchorComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            RemCompDeferred<BoardingTeleportAnchorComponent>(ent);
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

        if (TryComp<BoardingTeleportAnchorComponent>(user, out var anchor))
        {
            TryReturn(ent, user, anchor);
            return;
        }

        TryDepart(ent, user);
    }

    private void TryDepart(Entity<BoardingTeleportPlatformComponent> ent, EntityUid user)
    {
        if (!IsStandingOnPlatform(ent.Owner, user, ent.Comp))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-not-on-platform"), user, user);
            return;
        }

        if (ent.Comp.LinkedConsole is not { } consoleUid ||
            !TryComp<BoardingTeleportConsoleComponent>(consoleUid, out var console))
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-no-console"), ent.Owner, user);
            return;
        }

        if (console.LandingCoordinates is not { } landing)
        {
            _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-no-target"), ent.Owner, user);
            return;
        }

        if (!_console.TryValidateDeparture(consoleUid, console, landing, out var status))
        {
            _popup.PopupEntity(Loc.GetString($"boarding-teleport-status-{status}"), user, user);
            return;
        }

        var distanceScale = _console.GetDistanceScale(ent.Owner, landing);
        ent.Comp.PendingLandingCoordinates = landing;
        Dirty(ent);

        BeginDelayedTeleport(ent, user, landing, consoleUid, console, requirePlatform: true, returning: false, mode: console.Mode, distanceScale: distanceScale);
    }

    private void TryReturn(Entity<BoardingTeleportPlatformComponent> ent, EntityUid user, BoardingTeleportAnchorComponent anchor)
    {
        if (!_console.TryResolveHome(ent.Owner, ent.Comp, anchor, user, out var home, out var unreachable))
        {
            RemCompDeferred<BoardingTeleportAnchorComponent>(user);
            if (unreachable)
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-home-invalid"), user, user);
            else if (anchor.ExpiresAt is { } expires && _timing.CurTime >= expires)
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-return-expired"), user, user);
            else
                _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-wrong-platform"), user, user);
            return;
        }

        BeginDelayedTeleport(ent, user, home, null, null, requirePlatform: false, returning: true, distanceScale: 1f);
    }

    private void BeginDelayedTeleport(
        Entity<BoardingTeleportPlatformComponent> ent,
        EntityUid user,
        EntityCoordinates destination,
        EntityUid? consoleUid,
        BoardingTeleportConsoleComponent? console,
        bool requirePlatform,
        bool returning = false,
        BoardingTeleportInsertionMode mode = BoardingTeleportInsertionMode.Stealth,
        float distanceScale = 1f)
    {
        CancelPendingTeleport(ent.Owner, ent.Comp);

        ent.Comp.ChargeToken++;
        var chargeToken = ent.Comp.ChargeToken;
        ent.Comp.ActiveChargeUser = user;
        Dirty(ent);

        var delay = BoardingTeleportBalance.ComputeDepartureDelay(ent.Comp.DepartureDelay, mode, distanceScale, returning);
        var cancel = new System.Threading.CancellationTokenSource();

        ent.Comp.DeparturePending = true;
        Dirty(ent);
        UpdatePlatformAppearance(ent.Owner, ent.Comp);
        _audio.PlayPvs(ent.Comp.ActivationSound, Transform(ent.Owner).Coordinates);
        SpawnCountdownAudioSequence(ent.Owner, ent.Comp, delay, cancel.Token, chargeToken);
        SpawnWarmupSequence(destination, ent.Comp, delay, cancel.Token);
        _popup.PopupEntity(Loc.GetString("boarding-teleport-platform-departure-delay", ("seconds", delay)), ent.Owner, user);

        Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(delay), () =>
            CompletePendingTeleport(ent.Owner, chargeToken, user, destination, consoleUid, console, requirePlatform, returning, mode, distanceScale),
            cancel.Token);

        ent.Comp.PendingCancel = cancel;
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
        BoardingTeleportInsertionMode mode,
        float distanceScale)
    {
        if (!Exists(platformUid) || !TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform))
            return;

        if (platform.ChargeToken != chargeToken || platform.ActiveChargeUser != user)
            return;

        platform.DeparturePending = false;
        platform.ActiveChargeUser = null;
        platform.PendingLandingCoordinates = null;
        platform.PendingCancel?.Dispose();
        platform.PendingCancel = null;
        Dirty(platformUid, platform);
        UpdatePlatformAppearance(platformUid, platform);

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
            if (!TryComp<BoardingTeleportAnchorComponent>(user, out var anchor) ||
                !_console.TryResolveHome(platformUid, platform, anchor, user, out destination, out _))
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

            var landing = platform.PendingLandingCoordinates ?? pendingLanding;
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
            RemCompDeferred<BoardingTeleportAnchorComponent>(user);
        else
        {
            var anchor = EnsureComp<BoardingTeleportAnchorComponent>(user);
            anchor.HomePlatform = GetNetEntity(platformUid);
            anchor.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(platform.ReturnWindowSeconds);
            Dirty(user, anchor);
        }

        var source = Transform(user).Coordinates;
        var destabilized = !returning &&
                           TryApplyDestabilization(user, platformUid, destination, console, mode, distanceScale, platform.ExperimentalRisk);

        Teleport(user, destination, platform);
        StartCooldown((platformUid, platform));

        var targetGridNet = console?.TargetGrid is { } tg ? GetNetEntity(tg) : NetEntity.Invalid;
        var outcome = destabilized ? "destabilized" : "stable";
        _adminLogger.Add(LogType.Teleport, LogImpact.Medium,
            $"{ToPrettyString(user):player} boarding-teleport mode={mode} targetGrid={targetGridNet} distScale={distanceScale:0.00} outcome={outcome} from {source} to {destination} via {ToPrettyString(platformUid)}");

        if (consoleUid is { } consoleEnt && console != null)
            _console.UpdateUi((consoleEnt, console));
    }

    private void CancelPendingTeleport(EntityUid platformUid, BoardingTeleportPlatformComponent? platform = null)
    {
        platform ??= CompOrNull<BoardingTeleportPlatformComponent>(platformUid);
        if (platform == null)
            return;

        platform.PendingCancel?.Cancel();
        platform.PendingCancel?.Dispose();
        platform.PendingCancel = null;
        platform.ChargeToken++;

        if (!platform.DeparturePending)
            return;

        platform.DeparturePending = false;
        platform.ActiveChargeUser = null;
        platform.PendingLandingCoordinates = null;
        Dirty(platformUid, platform);
        UpdatePlatformAppearance(platformUid, platform);
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

    private void SpawnWarmupSequence(EntityCoordinates coordinates, BoardingTeleportPlatformComponent component, float delaySeconds, System.Threading.CancellationToken cancelToken)
    {
        for (var i = 0; i < WarmupPulseSteps; i++)
        {
            var pulse = i;
            var delay = TimeSpan.FromSeconds(delaySeconds * pulse / WarmupPulseSteps);
            Robust.Shared.Timing.Timer.Spawn(delay, () =>
            {
                if (!coordinates.EntityId.IsValid() || !Exists(coordinates.EntityId))
                    return;

                var progress = (pulse + 1f) / WarmupPulseSteps;
                var pulseProto = component.WarmupEffect;
                if (progress >= 0.75f)
                    pulseProto = "BoardingTeleportWarmupEffectHigh";
                else if (progress >= 0.4f)
                    pulseProto = "BoardingTeleportWarmupEffectMedium";

                Spawn(pulseProto, coordinates);
            }, cancelToken);
        }
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
        BoardingTeleportInsertionMode mode,
        float distanceScale,
        bool experimental)
    {
        var chance = _console.GetDestabilizationChance(mode, distanceScale, console, platformUid, experimental);
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
