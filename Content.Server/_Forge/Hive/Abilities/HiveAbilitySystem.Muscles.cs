using Content.Shared._Forge.Hive;
using Content.Shared._Forge.Hive.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Popups;

namespace Content.Server._Forge.Hive.Abilities;


public sealed partial class HiveAbilitySystem
{
    private static readonly TimeSpan MusclesTickRate = TimeSpan.FromSeconds(1);

    private void InitializeMuscles()
    {
        SubscribeLocalEvent<HiveMemberComponent, HiveStrainedMusclesEvent>(OnStrainedMuscles);
    }

    private void OnStrainedMuscles(Entity<HiveMemberComponent> ent, ref HiveStrainedMusclesEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (TryComp<HiveStrainedMusclesComponent>(ent, out var active))
        {
            StopStrainedMuscles((ent.Owner, active));
            return;
        }

        var muscles = EnsureComp<HiveStrainedMusclesComponent>(ent);
        muscles.SpeedMultiplier = args.SpeedMultiplier;
        muscles.StaminaPerSecond = args.StaminaPerSecond;
        muscles.KnockdownOnEnd = args.KnockdownOnEnd;
        muscles.Action = args.Action.Owner;
        muscles.NextTick = _timing.CurTime + MusclesTickRate;
        Dirty(ent, muscles);


        _movementSpeed.RefreshMovementSpeedModifiers(ent);

        _actions.SetToggled(args.Action.Owner, true);
        _popup.PopupEntity(Loc.GetString("hive-muscles-on"), ent, ent);
    }

    private void StopStrainedMuscles(Entity<HiveStrainedMusclesComponent> ent)
    {
        _actions.SetToggled(ent.Comp.Action, false);
        var knockdown = ent.Comp.KnockdownOnEnd;

        RemCompDeferred<HiveStrainedMusclesComponent>(ent);

        _popup.PopupEntity(Loc.GetString("hive-muscles-off"), ent, ent, PopupType.MediumCaution);


        if (knockdown > TimeSpan.Zero)
            _stun.TryKnockdown(ent, knockdown, true);
    }

    private void UpdateMuscles(TimeSpan now)
    {
        var query = EntityQueryEnumerator<HiveStrainedMusclesComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + MusclesTickRate;

            if (!TryComp<StaminaComponent>(uid, out var stamina))
                continue;


            if (stamina.Critical || stamina.StaminaDamage + comp.StaminaPerSecond >= stamina.CritThreshold)
            {
                StopStrainedMuscles((uid, comp));
                continue;
            }

            _stamina.TakeStaminaDamage(uid, comp.StaminaPerSecond, stamina, visual: false);
        }
    }
}
