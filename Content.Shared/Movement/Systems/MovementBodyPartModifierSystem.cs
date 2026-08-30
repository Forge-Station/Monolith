using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Movement.Components;

using Robust.Shared.Containers;

namespace Content.Shared.Movement.Systems;

public sealed class MovementBodyPartModifierSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, RefreshMovementSpeedModifiersEvent>(
            OnRefreshMovementSpeed);
    }

    private void OnRefreshMovementSpeed(
        EntityUid uid,
        BodyComponent body,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        if (body.RootContainer.ContainedEntity is not { } rootPart)
            return;

        var groups = new Dictionary<string, ModifierGroup>();

        CollectBodyParts(rootPart, groups);

        foreach (var group in groups.Values)
        {
            if (group.Count == 0)
                continue;

            var walkModifier = group.Walk / group.Count;
            var sprintModifier = group.Sprint / group.Count;

            args.ModifySpeed(walkModifier, sprintModifier);
        }
    }

    private void CollectBodyParts(
        EntityUid part,
        Dictionary<string, ModifierGroup> groups)
    {
        // Modifier on the body part itself.
        if (TryComp<MovementBodyPartModifierComponent>(
                part,
                out var modifier))
        {
            AddModifier(modifier, groups);
        }

        if (!TryComp<BodyPartComponent>(part, out var bodyPart))
            return;

        // Child body parts.
        foreach (var slot in bodyPart.Children.Values)
        {
            var containerId =
                SharedBodySystem.PartSlotContainerIdPrefix + slot.Id;

            var container =
                (ContainerSlot) _container.GetContainer(part, containerId);

            if (container.ContainedEntity is { } child)
                CollectBodyParts(child, groups);
        }

        // Organs attached to this body part.
        foreach (var slot in bodyPart.Organs.Values)
        {
            var containerId =
                SharedBodySystem.OrganSlotContainerIdPrefix + slot.Id;

            var container =
                (ContainerSlot) _container.GetContainer(part, containerId);

            if (container.ContainedEntity is { } organ)
                AddOrganModifier(organ, groups);
        }
    }

    private void AddOrganModifier(
        EntityUid organ,
        Dictionary<string, ModifierGroup> groups)
    {
        if (!TryComp<MovementBodyPartModifierComponent>(
                organ,
                out var modifier))
            return;

        AddModifier(modifier, groups);
    }

    private static void AddModifier(
        MovementBodyPartModifierComponent modifier,
        Dictionary<string, ModifierGroup> groups)
    {
        if (!groups.TryGetValue(modifier.Group, out var group))
        {
            group = new ModifierGroup();
            groups.Add(modifier.Group, group);
        }

        group.Walk += modifier.WalkModifier;
        group.Sprint += modifier.SprintModifier;
        group.Count++;
    }

    private sealed class ModifierGroup
    {
        public float Walk;
        public float Sprint;
        public int Count;
    }
}