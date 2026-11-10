using Content.Shared.Containers.ItemSlots;
using Content.Shared._Forge.Crypto.Components;
using JetBrains.Annotations;

namespace Content.Shared._Forge.Crypto.Systems;

[UsedImplicitly]
public abstract partial class SharedCryptoSellConsoleSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CryptoSellConsoleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CryptoSellConsoleComponent, ComponentRemove>(OnRemove);
    }

    private void OnInit(EntityUid uid, CryptoSellConsoleComponent component, ComponentInit args)
    {
        _itemSlots.AddItemSlot(uid, CryptoSellConsoleComponent.BitcoinSlotId, component.BitcoinSlot);
    }

    private void OnRemove(EntityUid uid, CryptoSellConsoleComponent component, ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(uid, component.BitcoinSlot);
    }
}
