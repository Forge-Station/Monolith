using Content.Shared.Interaction;
using Robust.Shared.Containers;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Lets telekinesis treat in-LOS targets within <see cref="GeneticTelekinesisComponent.Range"/> as accessible,
/// so RMB verbs (rack, unload, etc.) work. Shooting still requires a held gun.
/// </summary>
public sealed class SharedGeneticTelekinesisSystem : EntitySystem
{
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticTelekinesisComponent, AccessibleOverrideEvent>(OnAccessible);
        SubscribeLocalEvent<GeneticTelekinesisComponent, InRangeOverrideEvent>(OnInRange);
    }

    private void OnAccessible(Entity<GeneticTelekinesisComponent> ent, ref AccessibleOverrideEvent args)
    {
        if (args.User != ent.Owner || args.Handled)
            return;

        args.Handled = true;

        // Don't reach into sealed foreign containers; world / same-container is fine.
        if (_containers.IsEntityInContainer(args.Target) &&
            !_containers.IsInSameOrParentContainer(args.User, args.Target, out _, out _))
            return;

        args.Accessible = true;
    }

    private void OnInRange(Entity<GeneticTelekinesisComponent> ent, ref InRangeOverrideEvent args)
    {
        if (args.User != ent.Owner || args.Handled)
            return;

        if (!TryComp(args.Target, out TransformComponent? targetXform))
            return;

        args.Handled = true;
        // Coordinates overload does not re-raise InRangeOverride (avoids recursion) and still raycasts LOS.
        args.InRange = _interaction.InRangeUnobstructed(
            (args.User, null),
            (args.Target, targetXform),
            targetXform.Coordinates,
            targetXform.LocalRotation,
            range: ent.Comp.Range);
    }
}
