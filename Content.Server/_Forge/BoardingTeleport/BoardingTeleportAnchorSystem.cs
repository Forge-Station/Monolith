using Content.Shared._Forge.BoardingTeleport.Components;
using Content.Shared.Alert;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.BoardingTeleport;

public sealed class BoardingTeleportAnchorSystem : EntitySystem
{
    private static readonly ProtoId<AlertPrototype> ReturnAlert = "BoardingTeleportReturn";

    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly BoardingTeleportPlatformSystem _platform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingTeleportAnchorComponent, ComponentStartup>(OnAnchorStartup);
        SubscribeLocalEvent<BoardingTeleportAnchorComponent, ComponentShutdown>(OnAnchorShutdown);
        SubscribeLocalEvent<BoardingTeleportRemoteComponent, ExaminedEvent>(OnRemoteExamined);
        SubscribeLocalEvent<BoardingTeleportAnchorComponent, MobStateChangedEvent>(OnAnchorMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BoardingTeleportAnchorComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var anchor, out _))
        {
            if (anchor.ExpiresAt is not { } expires)
                continue;

            var emergency = _timing.CurTime >= expires && !anchor.EmergencyReturnUsed;
            if (anchor.CachedReturnAlertEmergency == emergency)
                continue;

            anchor.CachedReturnAlertEmergency = emergency;
            ShowReturnAlert(uid, anchor, emergency);
        }
    }

    private void OnAnchorStartup(Entity<BoardingTeleportAnchorComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.CachedReturnAlertEmergency = false;
        ShowReturnAlert(ent.Owner, ent.Comp, emergency: false);
    }

    private void OnAnchorShutdown(Entity<BoardingTeleportAnchorComponent> ent, ref ComponentShutdown args)
    {
        _alerts.ClearAlert(ent.Owner, ReturnAlert);
    }

    private void OnAnchorMobStateChanged(Entity<BoardingTeleportAnchorComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        _platform.ClearPendingReturnConfirm(ent.Owner);
        _alerts.ClearAlert(ent.Owner, ReturnAlert);
        RemCompDeferred<BoardingTeleportAnchorComponent>(ent);
    }

    private void OnRemoteExamined(Entity<BoardingTeleportRemoteComponent> ent, ref ExaminedEvent args)
    {
        var user = args.Examiner;
        if (user == EntityUid.Invalid || !HasComp<BoardingTeleportAnchorComponent>(user))
            return;

        var anchor = Comp<BoardingTeleportAnchorComponent>(user);
        args.PushMarkup(GetReturnMarkup(anchor));
    }

    private void ShowReturnAlert(EntityUid user, BoardingTeleportAnchorComponent anchor, bool emergency)
    {
        if (anchor.ExpiresAt is not { } expires)
            return;

        if (emergency)
        {
            _alerts.ShowAlert(user, ReturnAlert, severity: 2);
            return;
        }

        var remaining = expires - _timing.CurTime;
        if (remaining <= TimeSpan.Zero)
            return;

        _alerts.ShowAlert(
            user,
            ReturnAlert,
            severity: 0,
            cooldown: (_timing.CurTime, expires),
            autoRemove: true,
            showCooldown: true);
    }

    private string GetReturnMarkup(BoardingTeleportAnchorComponent anchor)
    {
        if (anchor.ExpiresAt is not { } expires)
            return Loc.GetString("boarding-teleport-remote-no-anchor");

        var remaining = expires - _timing.CurTime;
        if (remaining > TimeSpan.Zero)
        {
            return Loc.GetString("boarding-teleport-remote-return-remaining",
                ("seconds", $"{remaining.TotalSeconds:0}"));
        }

        if (!anchor.EmergencyReturnUsed)
            return Loc.GetString("boarding-teleport-remote-emergency-available");

        return Loc.GetString("boarding-teleport-remote-return-expired");
    }
}
