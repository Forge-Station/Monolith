using Content.Shared.Movement.Systems;

namespace Content.Shared._Forge.Genetics.Mutations;

public sealed class GeneticMovespeedSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movespeed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticMovespeedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticMovespeedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GeneticMovespeedComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
    }

    private void OnStartup(Entity<GeneticMovespeedComponent> ent, ref ComponentStartup args)
    {
        _movespeed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnShutdown(Entity<GeneticMovespeedComponent> ent, ref ComponentShutdown args)
    {
        _movespeed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRefresh(Entity<GeneticMovespeedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier);
    }
}
