using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WF.WildSlimes;

/// <summary>Digs a mob with <see cref="WFDigInComponent"/> in when it is hurt, and out again once it has been left alone.</summary>
public sealed class WFDigInSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFDigInComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WFDigInComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<WFDigInComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnDamageChanged(Entity<WFDigInComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || _mob.IsDead(ent))
            return;

        var wasDugIn = ent.Comp.DugInUntil != null;
        ent.Comp.DugInUntil = _timing.CurTime + ent.Comp.Duration;
        if (wasDugIn)
            return;

        _popup.PopupEntity(Loc.GetString(ent.Comp.Popup, ("slime", ent.Owner)), ent, PopupType.Small);
        _speed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnDamageModify(Entity<WFDigInComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.DugInUntil != null)
            args.Damage *= ent.Comp.DamageCoefficient;
    }

    private void OnRefreshSpeed(Entity<WFDigInComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.DugInUntil != null)
            args.ModifySpeed(ent.Comp.SpeedModifier);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFDigInComponent>();
        while (query.MoveNext(out var uid, out var dig))
        {
            if (dig.DugInUntil is not { } until || now < until)
                continue;

            dig.DugInUntil = null;
            _speed.RefreshMovementSpeedModifiers(uid);
        }
    }
}
