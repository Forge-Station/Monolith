using Content.Shared._Forge.Hive.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffect;
using Robust.Shared.Timing;

namespace Content.Shared._Forge.Hive;


public sealed class SharedHiveAbilitySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;


    private static readonly HashSet<string> StunKeys = new() { "Stun", "KnockedDown", "ForcedSleep" };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HiveStrainedMusclesComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<HiveStrainedMusclesComponent, ComponentStartup>(OnMusclesStartup);
        SubscribeLocalEvent<HiveStrainedMusclesComponent, ComponentShutdown>(OnMusclesShutdown);

        SubscribeLocalEvent<HiveAdrenalineComponent, BeforeStatusEffectAddedEvent>(OnBeforeStatus);
    }

    private void OnRefreshSpeed(Entity<HiveStrainedMusclesComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.ModifySpeed(ent.Comp.SpeedMultiplier);
    }

    private void OnMusclesStartup(Entity<HiveStrainedMusclesComponent> ent, ref ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnMusclesShutdown(Entity<HiveStrainedMusclesComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnBeforeStatus(Entity<HiveAdrenalineComponent> ent, ref BeforeStatusEffectAddedEvent args)
    {
        if (args.Cancelled || _timing.CurTime > ent.Comp.StunImmuneUntil)
            return;

        if (StunKeys.Contains(args.Key))
            args.Cancelled = true;
    }
}
