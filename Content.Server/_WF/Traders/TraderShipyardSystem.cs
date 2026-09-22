using System.Linq;
using System.Text;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Shipyard;
using Content.Server.GameTicking;
using Content.Shared._Mono.Shipyard;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Traders;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Traders;

/// <summary>
/// Fronts real shipyard console listings over a counter. The trader carries a copy of the chosen
/// console's own components, so every upstream purchase rule runs unchanged with the NPC as the
/// console - it just refuses to buy anything back.
/// </summary>
public sealed partial class TraderShipyardSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private TraderSystem _trader = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderShipyardComponent, TraderActionEvent>(OnAction);
        SubscribeLocalEvent<TraderShipyardComponent, TraderConversationEndedEvent>(OnConversationEnded);
        SubscribeLocalEvent<TraderShipyardComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<TraderShipyardComponent, ShipyardConsoleActionAttemptEvent>(OnConsoleAction);
        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnShipPurchased);
    }

    private void OnAction(Entity<TraderShipyardComponent> ent, ref TraderActionEvent args)
    {
        if (args.Action != TraderAction.BuyShip || args.Handled)
            return;

        if (!TryComp<TraderComponent>(ent, out var trader))
            return;

        args.Handled = true;

        var traderEnt = (ent.Owner, trader);

        if (args.Argument is not { } argument
            || !ent.Comp.Consoles.Any(listing => listing.Console.Id == argument))
        {
            Log.Error($"{ToPrettyString(ent)} has no listing for '{args.Argument}'.");
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-cannot-help"));
            return;
        }

        // A voucher goes in the slot exactly like a card; the console redeems it on purchase.
        if (!_trader.TryHoldConsoleId(traderEnt, args.Customer, out var idCard))
            return;

        if (!_shipyard.TryHostConsole(ent.Owner, argument, out var uiKey)
            || !_shipyard.TryInsertHostedId(ent.Owner, idCard))
        {
            _trader.ReturnHeldItems(traderEnt);
            _shipyard.ClearHostedConsole(ent.Owner);
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-cannot-help"));
            return;
        }

        ent.Comp.ActiveConsole = argument;
        ent.Comp.ActiveKey = uiKey;

        // The listing replaces the conversation window for as long as the customer browses it.
        _trader.HideDialogue(traderEnt, args.Customer);
        _ui.TryOpenUi(ent.Owner, uiKey, args.Customer);
    }

    /// <summary>
    /// Closing the listing ends the conversation, which hands the card back.
    /// </summary>
    private void OnUiClosed(Entity<TraderShipyardComponent> ent, ref BoundUIClosedEvent args)
    {
        if (ent.Comp.ActiveKey is not { } key || !Equals(args.UiKey, key))
            return;

        if (!TryComp<TraderComponent>(ent, out var trader) || trader.Customer != args.Actor)
            return;

        _trader.EndConversation((ent.Owner, trader));
    }

    /// <summary>
    /// Whatever ended the conversation, the hosted listing goes with it.
    /// </summary>
    private void OnConversationEnded(Entity<TraderShipyardComponent> ent, ref TraderConversationEndedEvent args)
    {
        Teardown(ent, args.Customer);
    }

    private void Teardown(Entity<TraderShipyardComponent> ent, EntityUid? customer)
    {
        if (ent.Comp.ActiveKey is { } key)
        {
            if (customer is { } uid && !TerminatingOrDeleted(uid))
                _ui.CloseUi(ent.Owner, key, uid);

            ent.Comp.ActiveKey = null;
        }

        ent.Comp.ActiveConsole = null;
        _shipyard.ClearHostedConsole(ent.Owner);
    }

    /// <summary>
    /// A new ship dealer only sells. Selling back is the used ship salesman's trade.
    /// </summary>
    private void OnConsoleAction(Entity<TraderShipyardComponent> ent, ref ShipyardConsoleActionAttemptEvent args)
    {
        args.Cancelled = true;

        if (args.Action == ShipyardConsoleAction.Sell && TryComp<TraderComponent>(ent, out var trader))
            _trader.Say((ent.Owner, trader), Loc.GetString("trader-shipyard-no-selling"));
    }

    /// <summary>
    /// Prints a receipt when the purchase went over a trader's counter.
    /// </summary>
    private void OnShipPurchased(ShipyardShuttlePurchaseEvent args)
    {
        var query = EntityQueryEnumerator<TraderShipyardComponent, TraderComponent>();
        while (query.MoveNext(out var uid, out var dealer, out var trader))
        {
            if (dealer.ActiveConsole == null || trader.Customer != args.Purchaser)
                continue;

            PrintReceipt((uid, trader), args.Purchaser, args.Shuttle);
            _trader.Say((uid, trader), Loc.GetString("trader-shipyard-sold"));
            return;
        }
    }

    private void PrintReceipt(Entity<TraderComponent> trader, EntityUid customer, EntityUid shuttle)
    {
        var design = Loc.GetString("trader-shipyard-unknown-design");
        var price = 0;

        if (TryComp<VesselComponent>(shuttle, out var vessel)
            && _proto.TryIndex<VesselPrototype>(vessel.VesselId, out var proto))
        {
            design = proto.Name;
            price = proto.Price;
        }

        var body = new StringBuilder();
        body.AppendLine(Loc.GetString("trader-shipyard-receipt-header",
            ("trader", Name(trader)),
            ("customer", Name(customer)),
            ("time", _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"))));
        body.AppendLine(Loc.GetString("trader-shipyard-receipt-line",
            ("ship", Name(shuttle)),
            ("design", design),
            ("price", BankSystemExtensions.ToSpesoString(price))));

        _trader.PrintReceipt(trader, Loc.GetString("trader-shipyard-receipt-name"), body.ToString());
    }
}
