using Content.Shared._Forge.Hive;
using Content.Shared._Forge.Hive.Components;
using Content.Shared._Goobstation.Flashbang;
using Content.Shared.Database;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;

namespace Content.Server._Forge.Hive.Abilities;


public sealed partial class HiveAbilitySystem
{
    private readonly HashSet<Entity<MobStateComponent>> _shriekTargets = new();
    private readonly HashSet<Entity<PoweredLightComponent>> _shriekLights = new();

    private void InitializeShrieks()
    {
        SubscribeLocalEvent<HiveMemberComponent, HiveDissonantShriekEvent>(OnDissonantShriek);
        SubscribeLocalEvent<HiveMemberComponent, HiveResonantShriekEvent>(OnResonantShriek);
    }

    private void OnDissonantShriek(Entity<HiveMemberComponent> ent, ref HiveDissonantShriekEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        _emp.EmpPulse(_transform.GetMapCoordinates(ent), args.Range, args.EnergyConsumption, args.Duration, ent.Owner);

        _audio.PlayPvs(args.Sound, ent);
        _popup.PopupEntity(Loc.GetString(args.Popup, ("user", ent.Owner)), ent, PopupType.MediumCaution);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(ent):user} used Dissonant Shriek (EMP, range {args.Range})");
    }

    private void OnResonantShriek(Entity<HiveMemberComponent> ent, ref HiveResonantShriekEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var coords = Transform(ent).Coordinates;

        _shriekTargets.Clear();
        _lookup.GetEntitiesInRange(coords, args.Range, _shriekTargets);

        foreach (var target in _shriekTargets)
        {
            if (target.Owner == ent.Owner || HasComp<HiveMemberComponent>(target))
                continue;


            var protection = new GetFlashbangedEvent(args.Range);
            RaiseLocalEvent(target, protection);
            if (protection.ProtectionRange < args.Range)
                continue;

            _jitter.DoJitter(target, args.Duration, true);
            _stutter.DoStutter(target, args.Duration, true);
            _drunk.TryApplyDrunkenness(target, args.Disorientation, applySlur: false);

            if (args.StaminaDamage > 0)
                _stamina.TakeStaminaDamage(target, args.StaminaDamage, source: ent.Owner);

            _popup.PopupEntity(Loc.GetString(args.PopupVictim), target, target, PopupType.LargeCaution);
        }

        if (args.BreakLights)
        {
            _shriekLights.Clear();
            _lookup.GetEntitiesInRange(coords, args.Range, _shriekLights);
            foreach (var light in _shriekLights)
            {
                _light.TryDestroyBulb(light.Owner, light.Comp, ent.Owner);
            }
        }

        _audio.PlayPvs(args.Sound, ent);
        _popup.PopupEntity(Loc.GetString(args.Popup, ("user", ent.Owner)), ent, PopupType.MediumCaution);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(ent):user} used Resonant Shriek (range {args.Range})");
    }
}
