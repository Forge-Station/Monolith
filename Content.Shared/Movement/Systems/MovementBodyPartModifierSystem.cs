using Content.Shared.Body.Components;
using Content.Shared.Movement.Components;

namespace Content.Shared.Movement.Systems;

public sealed class MovementBodyPartModifierSystem : EntitySystem
{
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
        var walkModifier = 0f;
        var sprintModifier = 0f;
        var modifierCount = 0;

        foreach (var leg in body.LegEntities)
        {
            if (!TryComp<MovementBodyPartModifierComponent>(
                    leg,
                    out var modifier))
            {
                continue;
            }

            walkModifier += modifier.WalkModifier;
            sprintModifier += modifier.SprintModifier;
            modifierCount++;
        }

        if (modifierCount == 0)
            return;

        walkModifier /= modifierCount;
        sprintModifier /= modifierCount;

        args.ModifySpeed(walkModifier, sprintModifier);
    }
}