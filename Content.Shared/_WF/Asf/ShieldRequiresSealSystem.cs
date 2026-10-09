using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared._Goobstation.Clothing.Systems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Shared._WF.Asf;

/// <summary>
/// Ties a modsuit's personal shield to its seal: no shield on an unsealed suit, and unsealing drops it.
/// </summary>
public sealed class ShieldRequiresSealSystem : EntitySystem
{
    [Dependency] private ItemToggleSystem _toggle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShieldRequiresSealComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<ShieldRequiresSealComponent, ClothingControlSealCompleteEvent>(OnSealComplete);
    }

    private void OnActivateAttempt(Entity<ShieldRequiresSealComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (args.Cancelled || TryComp<SealableClothingControlComponent>(ent, out var control) && control.IsCurrentlySealed)
            return;

        args.Cancelled = true;
        args.Popup = Loc.GetString(ent.Comp.UnsealedPopup);
    }

    private void OnSealComplete(Entity<ShieldRequiresSealComponent> ent, ref ClothingControlSealCompleteEvent args)
    {
        if (!args.IsSealed)
            _toggle.TryDeactivate(ent.Owner);
    }
}
