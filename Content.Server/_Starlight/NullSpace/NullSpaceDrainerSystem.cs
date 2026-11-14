using Content.Server._Starlight.Shadekin;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;

namespace Content.Server._Starlight.NullSpace;

public sealed class NullSpaceDrainerSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<NullSpaceDrainerComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<NullSpaceDrainerComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipped(EntityUid uid, NullSpaceDrainerComponent component, GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing)
            || !clothing.Slots.HasFlag(args.SlotFlags))
            return;

        EnsureComp<NullSpaceDrainerComponent>(args.Equipee);
    }

    private void OnUnequipped(EntityUid uid, NullSpaceDrainerComponent component, GotUnequippedEvent args)
    {
        RemComp<NullSpaceDrainerComponent>(args.Equipee);
    }
}
