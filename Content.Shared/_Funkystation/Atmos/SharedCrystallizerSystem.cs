using Content.Shared._Funkystation.Atmos.Components;
using Content.Shared.Interaction;
using Content.Shared.UserInterface;

namespace Content.Shared._Funkystation.Atmos;

/// <summary>
/// Crystallizer sprite is offset from its grid origin; use unobstructed reach checks for BUI messages.
/// </summary>
public sealed class SharedCrystallizerSystem : EntitySystem
{
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    /// <summary>Matches <c>interactionRange</c> on the crystallizer UI in prototype YAML.</summary>
    public const float UiReachRange = 4f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystallizerComponent, BoundUserInterfaceCheckRangeEvent>(OnUiRangeCheck);
    }

    private void OnUiRangeCheck(Entity<CrystallizerComponent> ent, ref BoundUserInterfaceCheckRangeEvent args)
    {
        if (!Equals(args.UiKey, CrystallizerUiKey.Key))
            return;

        if (args.Result == BoundUserInterfaceRangeResult.Fail)
            return;

        args.Result = _interaction.InRangeUnobstructed(args.Actor.Owner, ent.Owner, range: UiReachRange)
            ? BoundUserInterfaceRangeResult.Pass
            : BoundUserInterfaceRangeResult.Fail;
    }
}
