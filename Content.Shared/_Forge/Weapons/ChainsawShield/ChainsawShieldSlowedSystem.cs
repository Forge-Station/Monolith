using Content.Shared.Movement.Systems;

namespace Content.Shared._Forge.Weapons.ChainsawShield;

public sealed partial class ChainsawShieldSlowedSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChainsawShieldSlowedComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ChainsawShieldSlowedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ChainsawShieldSlowedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
    }

    private void OnInit(EntityUid uid, ChainsawShieldSlowedComponent component, ComponentInit args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnShutdown(EntityUid uid, ChainsawShieldSlowedComponent component, ComponentShutdown args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefreshMovementSpeed(EntityUid uid, ChainsawShieldSlowedComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.WalkSpeedModifier, component.SprintSpeedModifier);
    }
}
