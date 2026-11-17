using Content.Shared._Forge.Atmos.Components;
using Content.Shared.Movement.Systems;

namespace Content.Shared._Forge.Atmos.Systems;

public abstract partial class SharedGasCloudSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasCloudAffectedComponent, ComponentStartup>(OnAffectedStartup);
        SubscribeLocalEvent<GasCloudAffectedComponent, ComponentShutdown>(OnAffectedShutdown);
        SubscribeLocalEvent<GasCloudAffectedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<GasCloudAffectedComponent, RefreshWeightlessModifiersEvent>(OnRefreshWeightless);
    }

    private void OnAffectedStartup(Entity<GasCloudAffectedComponent> ent, ref ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);
        _movement.RefreshWeightlessModifiers(ent);
    }

    private void OnAffectedShutdown(Entity<GasCloudAffectedComponent> ent, ref ComponentShutdown args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);
        _movement.RefreshWeightlessModifiers(ent);
    }

    private void OnRefreshMoveSpeed(Entity<GasCloudAffectedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.WalkSpeedModifier, ent.Comp.SprintSpeedModifier);
    }

    private void OnRefreshWeightless(Entity<GasCloudAffectedComponent> ent, ref RefreshWeightlessModifiersEvent args)
    {
        args.ModifyAcceleration(ent.Comp.SprintSpeedModifier);
    }

    protected void RefreshCloudMovement(EntityUid uid)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
        _movement.RefreshWeightlessModifiers(uid);
    }
}
