using Content.Server._Crescent.ShipShields.Components;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio;
using Content.Server.Shuttles.Components;
using Content.Shared._Forge.CloakingShuttle;
using Content.Shared.Power;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Timing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Forge.CloakingShuttle;

public sealed class CloakingShuttleSystem : EntitySystem
{
    [Dependency] private readonly SharedShuttleSystem _sharedShuttleSystem = default!;
    [Dependency] private readonly PowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShuttleConsoleComponent, CloakingShuttleMessage>(OnShuttleConsoleActivate);
        SubscribeLocalEvent<SpaceArtilleryComponent, GunShotEvent>(OnSpaceArtilleryShot);

        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSendAttempt);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);

        SubscribeLocalEvent<CloakingShuttleDeviceComponent, ComponentStartup>(OnDeviceStartup);
        SubscribeLocalEvent<CloakingShuttleDeviceComponent, ComponentShutdown>(OnDeviceShutdown);
        SubscribeLocalEvent<CloakingShuttleDeviceComponent, AnchorStateChangedEvent>(OnDeviceAnchorChanged);
        SubscribeLocalEvent<CloakingShuttleDeviceComponent, PowerChangedEvent>(OnDevicePowerChanged);

        SubscribeLocalEvent<CloakingShuttleComponent, ComponentShutdown>(OnCloakShutdown);
    }

    private void OnRadioReceiveAttempt(ref RadioReceiveAttemptEvent ev)
    {
        var gridUid = Transform(ev.RadioReceiver).GridUid;
        if (TryComp<CloakingShuttleComponent>(gridUid, out var cloakingShuttleComponent)
            && cloakingShuttleComponent is { Active: true, DeviceRadioReceiveBlocking: true })
            ev.Cancelled = true;
    }

    private void OnRadioSendAttempt(ref RadioSendAttemptEvent ev)
    {
        var gridUid = Transform(ev.RadioSource).GridUid;
        if (TryComp<CloakingShuttleComponent>(gridUid, out var cloakingShuttleComponent)
            && cloakingShuttleComponent is { Active: true, DeviceRadioSendBlocking: true })
            ev.Cancelled = true;
    }

    private void OnCloakShutdown(EntityUid uid, CloakingShuttleComponent cloakingShuttleDeviceComponent, ComponentShutdown args)
    {
        var gridUid = Transform(uid).GridUid;
        if (gridUid is not { } grid)
            return;

        _sharedShuttleSystem.RemoveIFFFlag(grid, IFFFlags.Hide);
    }

    private void OnDevicePowerChanged(EntityUid uid, CloakingShuttleDeviceComponent cloakingShuttleDeviceComponent, PowerChangedEvent args)
    {
        RefreshDevicePresence(uid);
    }

    private void OnDeviceAnchorChanged(EntityUid uid, CloakingShuttleDeviceComponent cloakingShuttleDeviceComponent, AnchorStateChangedEvent args)
    {
        RefreshDevicePresence(uid);
    }

    private void OnDeviceShutdown(EntityUid uid, CloakingShuttleDeviceComponent cloakingShuttleDeviceComponent, ComponentShutdown args)
    {
        RefreshDevicePresence(uid);
    }

    private void OnDeviceStartup(EntityUid uid, CloakingShuttleDeviceComponent cloakingShuttleDeviceComponent, ComponentStartup args)
    {
        RefreshDevicePresence(uid);
    }

    private void OnSpaceArtilleryShot(EntityUid uid, SpaceArtilleryComponent spaceArtilleryComponent, GunShotEvent args)
    {
        var grid = Transform(uid).GridUid;
        if (grid == null || !TryComp<CloakingShuttleComponent>(grid, out var cloakingShuttleComponent) || !cloakingShuttleComponent.Active)
            return;

        Deactivate(grid.Value, cloakingShuttleComponent);
    }

    private void RefreshDevicePresence(EntityUid deviceUid)
    {
        var gridUid = Transform(deviceUid).GridUid;
        if (gridUid is not { } grid)
            return;

        var cloakingShuttleComponent = EnsureComp<CloakingShuttleComponent>(grid);
        var hasDevice = TryGetDevice(grid, out _);

        switch (cloakingShuttleComponent.Status)
        {
            case not ShuttleCloakingStatus.None when !hasDevice:
                Deactivate(grid, cloakingShuttleComponent);
                UpdateAppearanceInGrid(grid);
                cloakingShuttleComponent.Status = ShuttleCloakingStatus.None;
                break;

            case ShuttleCloakingStatus.None when hasDevice:
                cloakingShuttleComponent.Status = cloakingShuttleComponent.TimeCooldown != default
                    ? ShuttleCloakingStatus.Cooldown
                    : ShuttleCloakingStatus.Ready;
                UpdateAppearanceInGrid(grid);
                break;

            default:
                UpdateAppearanceInGrid(grid);
                break;
        }

        RaiseLocalEvent(grid, new CloakingShuttleStateChangedEvent(), true);
    }

    private bool TryGetDevice(EntityUid gridUid, out EntityUid device)
    {
        device = default;
        var best = EntityUid.Invalid;

        var query = EntityQueryEnumerator<CloakingShuttleDeviceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transformComponent))
        {
            if (transformComponent.GridUid != gridUid || !transformComponent.Anchored || !_powerReceiverSystem.IsPowered(uid))
                continue;

            if (best == EntityUid.Invalid || uid.CompareTo(best) < 0)
                best = uid;
        }

        if (best == EntityUid.Invalid)
            return false;

        device = best;
        return true;
    }

    private void UpdateAppearanceInGrid(EntityUid gridUid)
    {
        var query = EntityQueryEnumerator<CloakingShuttleDeviceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transformComponent))
        {
            if (transformComponent.GridUid != gridUid || !TryComp<CloakingShuttleComponent>(gridUid, out var cloakingShuttleComponent))
                continue;

            var status = cloakingShuttleComponent.Status;
            _appearance.SetData(uid, ShuttleCloakingStatus.Ready, status == ShuttleCloakingStatus.Ready);
            _appearance.SetData(uid, ShuttleCloakingStatus.Active, status == ShuttleCloakingStatus.Active);
            _appearance.SetData(uid, ShuttleCloakingStatus.Cooldown, status == ShuttleCloakingStatus.Cooldown);
            _appearance.SetData(uid, ShuttleCloakingStatus.None, status == ShuttleCloakingStatus.None);

        }
    }

    private void Deactivate(EntityUid gridUid, CloakingShuttleComponent cloakingShuttleComponent)
    {
        if (!cloakingShuttleComponent.Active)
            return;

        cloakingShuttleComponent.Active = false;
        cloakingShuttleComponent.TimeActive = default;
        cloakingShuttleComponent.TimeCooldown = StartEndTime.FromCurTime(_timing, cloakingShuttleComponent.DeviceCooldown);
        cloakingShuttleComponent.Status = ShuttleCloakingStatus.Cooldown;

        Dirty(gridUid, cloakingShuttleComponent);
        _sharedShuttleSystem.RemoveIFFFlag(gridUid, IFFFlags.Hide);
        UpdateAppearanceInGrid(gridUid);

        if (cloakingShuttleComponent.DeviceShieldDisabling)
            RemComp<ShipShieldDisabledGridComponent>(gridUid);

        RaiseLocalEvent(gridUid, new CloakingShuttleStateChangedEvent(), true);
    }

    private void Activate(EntityUid gridUid, CloakingShuttleComponent cloakingShuttleComponent)
    {
        if (cloakingShuttleComponent.Active
            || cloakingShuttleComponent.Status == ShuttleCloakingStatus.Cooldown
            && cloakingShuttleComponent.TimeCooldown != default
            && _timing.CurTime < cloakingShuttleComponent.TimeCooldown.End
            || !TryGetDevice(gridUid, out var deviceUid)
            || !TryComp<CloakingShuttleDeviceComponent>(deviceUid, out var cloakingShuttleDeviceComponent)
            || !TryComp<IFFComponent>(gridUid, out var iffComponent)
            || (iffComponent.Flags & IFFFlags.IsPlayerShuttle) == 0)
            return;

        cloakingShuttleComponent.DeviceCooldown = MathF.Max(cloakingShuttleDeviceComponent.Cooldown, 0f);
        cloakingShuttleComponent.DeviceDuration = MathF.Max(cloakingShuttleDeviceComponent.Duration, 0.1f);
        cloakingShuttleComponent.DeviceShieldDisabling = cloakingShuttleDeviceComponent.ShieldDisablingInCloaking;
        cloakingShuttleComponent.DeviceRadioSendBlocking = cloakingShuttleDeviceComponent.RadioSendBlockingInCloaking;
        cloakingShuttleComponent.DeviceRadioReceiveBlocking = cloakingShuttleDeviceComponent.RadioReceiveBlockingInCloaking;

        cloakingShuttleComponent.Active = true;
        cloakingShuttleComponent.TimeCooldown = default;
        cloakingShuttleComponent.TimeActive = StartEndTime.FromCurTime(_timing, cloakingShuttleComponent.DeviceDuration);
        cloakingShuttleComponent.Status = ShuttleCloakingStatus.Active;

        Dirty(gridUid, cloakingShuttleComponent);
        _sharedShuttleSystem.AddIFFFlag(gridUid, IFFFlags.Hide);
        UpdateAppearanceInGrid(gridUid);

        if (cloakingShuttleComponent.DeviceShieldDisabling)
            EnsureComp<ShipShieldDisabledGridComponent>(gridUid);

        RaiseLocalEvent(gridUid, new CloakingShuttleStateChangedEvent(), true);
    }

    private void ToggleCloakFromConsole(EntityUid consoleUid)
    {
        var gridUid = Transform(consoleUid).GridUid;

        if (gridUid is not { } grid || !TryComp<CloakingShuttleComponent>(grid, out var cloakingShuttleComponent))
            return;

        if (cloakingShuttleComponent.Active)
            Deactivate(grid, cloakingShuttleComponent);
        else
            Activate(grid, cloakingShuttleComponent);
    }

    private void OnShuttleConsoleActivate(EntityUid uid, ShuttleConsoleComponent shuttleConsoleComponent, CloakingShuttleMessage args)
    {
        ToggleCloakFromConsole(uid);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<CloakingShuttleComponent>();
        while (query.MoveNext(out var uid, out var cloakingShuttleComponent))
        {
            if (cloakingShuttleComponent is { Active: true, Status: ShuttleCloakingStatus.Active }
                && cloakingShuttleComponent.TimeActive != default
                && _timing.CurTime >= cloakingShuttleComponent.TimeActive.End)
            {
                Deactivate(uid, cloakingShuttleComponent);
                continue;
            }

            if (cloakingShuttleComponent.Status == ShuttleCloakingStatus.Cooldown
                && cloakingShuttleComponent.TimeCooldown != default
                && _timing.CurTime >= cloakingShuttleComponent.TimeCooldown.End)
            {
                cloakingShuttleComponent.TimeCooldown = default;
                cloakingShuttleComponent.Status = ShuttleCloakingStatus.Ready;
                RaiseLocalEvent(uid, new CloakingShuttleStateChangedEvent(), true);
                UpdateAppearanceInGrid(uid);
            }
        }
    }
}
