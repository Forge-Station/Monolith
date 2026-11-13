using Content.Shared._Forge.Hive;
using Content.Shared._Forge.Hive.Components;
using Content.Shared._White.Overlays;
using Content.Shared.Stealth.Components;

namespace Content.Server._Forge.Hive.Abilities;


public sealed partial class HiveAbilitySystem
{
    #region Продвинутое зрение

    private void InitializeEyesight()
    {
        SubscribeLocalEvent<HiveMemberComponent, HiveAugmentedEyesightEvent>(OnAugmentedEyesight);
        SubscribeLocalEvent<HiveAugmentedEyesightComponent, ComponentShutdown>(OnEyesightShutdown);
    }

    private void OnAugmentedEyesight(Entity<HiveMemberComponent> ent, ref HiveAugmentedEyesightEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var eyes = EnsureComp<HiveAugmentedEyesightComponent>(ent);


        if (!TryComp<WWDPNightVisionComponent>(ent, out var night))
        {
            night = EntityManager.ComponentFactory.GetComponent<WWDPNightVisionComponent>();
            night.ToggleAction = null;
            AddComp(ent, night);
            eyes.AddedNightVision = true;
        }

        if (!TryComp<ThermalVisionComponent>(ent, out var thermal))
        {
            thermal = EntityManager.ComponentFactory.GetComponent<ThermalVisionComponent>();
            thermal.ToggleAction = null;
            thermal.FlashDurationMultiplier = args.ThermalFlashMultiplier;
            AddComp(ent, thermal);
            eyes.AddedThermalVision = true;
        }

        eyes.Mode = eyes.Mode switch
        {
            HiveEyesightMode.Off => HiveEyesightMode.Night,
            HiveEyesightMode.Night => HiveEyesightMode.Thermal,
            _ => HiveEyesightMode.Off,
        };

        night.IsActive = eyes.Mode == HiveEyesightMode.Night;
        thermal.IsActive = eyes.Mode == HiveEyesightMode.Thermal;
        Dirty(ent, night);
        Dirty(ent, thermal);
        Dirty(ent, eyes);

        var popup = eyes.Mode switch
        {
            HiveEyesightMode.Night => "hive-eyesight-night",
            HiveEyesightMode.Thermal => "hive-eyesight-thermal",
            _ => "hive-eyesight-off",
        };
        _popup.PopupEntity(Loc.GetString(popup), ent, ent);
        _actions.SetToggled(args.Action.Owner, eyes.Mode != HiveEyesightMode.Off);
    }

    private void OnEyesightShutdown(Entity<HiveAugmentedEyesightComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (ent.Comp.AddedNightVision)
            RemComp<WWDPNightVisionComponent>(ent);

        if (ent.Comp.AddedThermalVision)
            RemComp<ThermalVisionComponent>(ent);
    }

    #endregion

    #region Кожа-хамелеон

    private void InitializeChameleon()
    {
        SubscribeLocalEvent<HiveMemberComponent, HiveChameleonSkinEvent>(OnChameleonSkin);
    }

    private void OnChameleonSkin(Entity<HiveMemberComponent> ent, ref HiveChameleonSkinEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (TryComp<HiveChameleonSkinComponent>(ent, out var active))
        {
            if (active.AddedStealth)
            {
                RemComp<StealthOnMoveComponent>(ent);
                RemComp<StealthComponent>(ent);
            }

            RemComp<HiveChameleonSkinComponent>(ent);
            _popup.PopupEntity(Loc.GetString("hive-chameleon-off"), ent, ent);
            _actions.SetToggled(args.Action.Owner, false);
            return;
        }

        var chameleon = AddComp<HiveChameleonSkinComponent>(ent);

        if (!HasComp<StealthComponent>(ent))
        {
            chameleon.AddedStealth = true;
            AddComp<StealthComponent>(ent);

            var onMove = EnsureComp<StealthOnMoveComponent>(ent);
            onMove.PassiveVisibilityRate = args.PassiveVisibilityRate;
            onMove.MovementVisibilityRate = args.MovementVisibilityRate;
            Dirty(ent, onMove);
        }

        _popup.PopupEntity(Loc.GetString("hive-chameleon-on"), ent, ent);
        _actions.SetToggled(args.Action.Owner, true);
    }

    #endregion
}
