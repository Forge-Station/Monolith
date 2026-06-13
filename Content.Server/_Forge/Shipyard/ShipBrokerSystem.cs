using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Database;
using Content.Server.Popups;
using Content.Shared._Forge.CCVar;
using Content.Shared._Forge.Shipyard;
using Content.Shared._Forge.Shipyard.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._Forge.Shipyard;

public sealed class ShipBrokerSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private int? _listedPrice;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipBrokerConsoleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ShipBrokerConsoleComponent, ShipBrokerSetPriceMessage>(OnSetPrice);
        SubscribeLocalEvent<ShipBrokerConsoleComponent, ShipBrokerConfirmSaleMessage>(OnConfirmSale);
        SubscribeLocalEvent<ShipBrokerConsoleComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<ShipBrokerConsoleComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<ShipBrokerConsoleComponent, EntRemovedFromContainerMessage>(OnSlotChanged);
    }

    private void OnInit(EntityUid uid, ShipBrokerConsoleComponent component, ComponentInit args)
    {
        _itemSlots.AddItemSlot(uid, ShipBrokerConsoleComponent.SellerSlotId, component.SellerSlot);
        _itemSlots.AddItemSlot(uid, ShipBrokerConsoleComponent.BuyerSlotId, component.BuyerSlot);
    }

    private void OnUiOpen(EntityUid uid, ShipBrokerConsoleComponent component, AfterActivatableUIOpenEvent args) =>
        UpdateUi(uid, component);

    private void OnSlotChanged(EntityUid uid, ShipBrokerConsoleComponent component, ContainerModifiedMessage args) =>
        UpdateUi(uid, component);

    private void OnSetPrice(EntityUid uid, ShipBrokerConsoleComponent component, ShipBrokerSetPriceMessage args)
    {
        _listedPrice = Math.Max(0, args.Price);
        UpdateUi(uid, component);
    }

    private async void OnConfirmSale(EntityUid uid, ShipBrokerConsoleComponent component, ShipBrokerConfirmSaleMessage args)
    {
        if (args.Actor is not { Valid: true } buyer)
            return;

        if (_listedPrice is not int price || price <= 0)
            return;

        var sellerId = component.SellerSlot.ContainerSlot?.ContainedEntity;
        var buyerId = component.BuyerSlot.ContainerSlot?.ContainedEntity;

        if (sellerId is not { Valid: true } || buyerId is not { Valid: true })
            return;

        if (!TryComp<ShuttleDeedComponent>(sellerId.Value, out var sellerDeed) || sellerDeed.PurchasedWithVoucher)
        {
            _popup.PopupEntity(Loc.GetString("ship-broker-invalid-sale"), uid, buyer);
            return;
        }

        if (TryComp<ShuttleDeedComponent>(buyerId.Value, out var buyerDeed) && buyerDeed.ShuttleUid is { Valid: true })
        {
            _popup.PopupEntity(Loc.GetString("ship-broker-buyer-has-deed"), uid, buyer);
            return;
        }

        if (!TryComp<BankAccountComponent>(buyer, out var buyerBank) || buyerBank.Balance < price)
        {
            _popup.PopupEntity(Loc.GetString("shipyard-console-no-bank"), uid, buyer);
            return;
        }

        EntityUid? sellerPlayer = null;
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } ent)
                continue;

            if (TryComp<IdCardComponent>(sellerId, out var card) && TryComp<IdCardComponent>(ent, out var playerCard)
                && playerCard.FullName == card.FullName)
            {
                sellerPlayer = ent;
                break;
            }
        }

        if (sellerPlayer == null || !TryComp<BankAccountComponent>(sellerPlayer, out _))
        {
            _popup.PopupEntity(Loc.GetString("ship-broker-seller-offline"), uid, buyer);
            return;
        }

        var feeRate = _cfg.GetCVar(ForgeCVars.BrokerFeeRate);
        var fee = (int) (price * feeRate);
        var payout = price - fee;

        if (sellerDeed.HangarState == HangarVesselState.InHangar && sellerDeed.PersistedVesselId is { } hangarGuid)
        {
            if (!_player.TryGetSessionByEntity(buyer, out var buyerSession))
                return;

            await _db.TransferHangarVesselAsync(hangarGuid, buyerSession.UserId);

            EntityManager.System<ShipyardSystem>()
                .TransferHangarDeed(sellerId.Value, buyerId.Value, buyer, hangarGuid, sellerDeed);
        }
        else if (sellerDeed.ShuttleUid is { Valid: true })
        {
            _popup.PopupEntity(Loc.GetString("ship-broker-must-hangar"), uid, buyer);
            return;
        }
        else
        {
            return;
        }

        _bank.TryBankWithdraw(buyer, price);
        _bank.TryBankDeposit(sellerPlayer.Value, payout);

        _listedPrice = null;
        _popup.PopupEntity(Loc.GetString("ship-broker-sale-complete"), uid, buyer);
        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, ShipBrokerConsoleComponent component)
    {
        var sellerPresent = component.SellerSlot.ContainerSlot?.ContainedEntity != null;
        var buyerPresent = component.BuyerSlot.ContainerSlot?.ContainedEntity != null;
        string? vesselName = null;
        int? appraisal = null;

        if (sellerPresent && TryComp<ShuttleDeedComponent>(component.SellerSlot.ContainerSlot!.ContainedEntity, out var deed))
        {
            vesselName = deed.ShuttleName;
            if (deed.ShuttleUid is { Valid: true } shuttle)
                appraisal = (int) EntityManager.System<Cargo.Systems.PricingSystem>().AppraiseGrid(shuttle, null);
        }

        var state = new ShipBrokerBoundUserInterfaceState(
            _listedPrice,
            appraisal,
            vesselName,
            sellerPresent,
            buyerPresent,
            component.BrokerFeeRate);

        _ui.SetUiState(uid, ShipBrokerUiKey.Key, state);
    }
}
