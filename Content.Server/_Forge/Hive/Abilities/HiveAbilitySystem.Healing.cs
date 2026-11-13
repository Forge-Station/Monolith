using Content.Shared._Forge.Hive;
using Content.Shared._Forge.Hive.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Popups;

namespace Content.Server._Forge.Hive.Abilities;


public sealed partial class HiveAbilitySystem
{
    private static readonly string[] StunStatuses = { "Stun", "KnockedDown", "ForcedSleep" };

    private static readonly TimeSpan FleshmendTickRate = TimeSpan.FromSeconds(1);

    #region Выброс адреналина

    private void InitializeAdrenaline()
    {
        SubscribeLocalEvent<HiveMemberComponent, HiveAdrenalineEvent>(OnAdrenaline);
    }

    private void OnAdrenaline(Entity<HiveMemberComponent> ent, ref HiveAdrenalineEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        foreach (var key in StunStatuses)
        {
            _status.TryRemoveStatusEffect(ent, key);
        }

        if (args.ResetStamina && TryComp<StaminaComponent>(ent, out var stamina) && stamina.StaminaDamage > 0)
            _stamina.TakeStaminaDamage(ent, -stamina.StaminaDamage, stamina, visual: false, immediate: false);

        var now = _timing.CurTime;
        var adrenaline = EnsureComp<HiveAdrenalineComponent>(ent);
        adrenaline.StunImmuneUntil = now + args.StunImmunity;

        if (args.PainResistance > TimeSpan.Zero)
        {
            adrenaline.PainResistUntil = now + args.PainResistance;
            if (!HasComp<IgnoreSlowOnDamageComponent>(ent))
            {
                AddComp<IgnoreSlowOnDamageComponent>(ent);
                adrenaline.AddedPainResist = true;
            }
        }

        Dirty(ent, adrenaline);

        if (args.Reagents is { } reagents && reagents.Volume > 0)
            _bloodstream.TryAddToChemicals(ent, reagents.Clone());

        _audio.PlayPvs(args.Sound, ent);
        _popup.PopupEntity(Loc.GetString(args.Popup), ent, ent, PopupType.Medium);
    }

    private void UpdateAdrenaline(TimeSpan now)
    {
        var query = EntityQueryEnumerator<HiveAdrenalineComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.AddedPainResist && now > comp.PainResistUntil)
            {
                RemComp<IgnoreSlowOnDamageComponent>(uid);
                comp.AddedPainResist = false;
            }

            if (!comp.AddedPainResist && now > comp.StunImmuneUntil)
                RemCompDeferred<HiveAdrenalineComponent>(uid);
        }
    }

    #endregion

    #region Исцеление плоти

    private void InitializeFleshmend()
    {
        SubscribeLocalEvent<HiveMemberComponent, HiveFleshmendEvent>(OnFleshmend);
    }

    private void OnFleshmend(Entity<HiveMemberComponent> ent, ref HiveFleshmendEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var now = _timing.CurTime;

        if (TryComp<HiveFleshmendComponent>(ent, out var active))
        {

            active.EndTime += args.Duration;
            active.HealPerSecond *= args.RecastMultiplier;
            active.BloodPerSecond *= args.RecastMultiplier;
            _popup.PopupEntity(Loc.GetString(args.PopupRecast), ent, ent);
            return;
        }

        var fleshmend = AddComp<HiveFleshmendComponent>(ent);
        fleshmend.HealPerSecond = new(args.HealPerSecond);
        fleshmend.BloodPerSecond = args.BloodPerSecond;
        fleshmend.EndTime = now + args.Duration;
        fleshmend.NextTick = now;

        _popup.PopupEntity(Loc.GetString(args.Popup), ent, ent, PopupType.Medium);
    }

    private void UpdateFleshmend(TimeSpan now)
    {
        var query = EntityQueryEnumerator<HiveFleshmendComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextTick)
                continue;

            if (now >= comp.EndTime)
            {
                RemCompDeferred<HiveFleshmendComponent>(uid);
                continue;
            }

            comp.NextTick = now + FleshmendTickRate;

            _damageable.TryChangeDamage(uid, comp.HealPerSecond, ignoreResistances: true, interruptsDoAfters: false);

            if (comp.BloodPerSecond > 0)
                _bloodstream.TryModifyBloodLevel(uid, comp.BloodPerSecond);
        }
    }

    #endregion
}
