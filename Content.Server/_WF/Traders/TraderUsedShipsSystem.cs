using System.Text;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._Mono.Shipyard;
using Content.Server._WF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Server.GameTicking;
using Content.Server.Radio.EntitySystems;
using Content.Shared._NF.Bank;
using Content.Shared._WF.Traders;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Traders;

/// <summary>
/// The used ship salesman: buys ships off customers under a real shipyard console's rules, and
/// sells the round's stock of bought-back hulls back out at a markup.
/// </summary>
public sealed partial class TraderUsedShipsSystem : EntitySystem
{
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private ShipyardDirectionSystem _direction = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private TraderSystem _trader = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private UsedShipMarketSystem _market = default!;

    private float _refreshAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderUsedShipsComponent, TraderActionEvent>(OnAction);
        SubscribeLocalEvent<TraderUsedShipsComponent, TraderConfirmedEvent>(OnConfirmed);
        SubscribeLocalEvent<TraderUsedShipsComponent, TraderConversationEndedEvent>(OnConversationEnded);
        SubscribeLocalEvent<TraderUsedShipsComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<TraderUsedShipsComponent, TraderUsedShipBuyMessage>(OnBuy);
        SubscribeLocalEvent<UsedShipListedEvent>(OnListed);
    }

    private void OnAction(Entity<TraderUsedShipsComponent> ent, ref TraderActionEvent args)
    {
        // Any other service taking over means an older quote is dead.
        ent.Comp.AwaitingConfirmation = false;
        ent.Comp.QuotedShip = null;

        if (args.Handled || !TryComp<TraderComponent>(ent, out var trader))
            return;

        var traderEnt = (ent.Owner, trader);

        switch (args.Action)
        {
            case TraderAction.SellShip:
                args.Handled = true;
                QuoteSale(ent, traderEnt, args.Customer);
                return;

            case TraderAction.UsedShips:
                args.Handled = true;
                OpenLot(ent, traderEnt, args.Customer);
                return;
        }
    }

    #region Selling to the salesman

    /// <summary>
    /// Appraises the ship on the customer's deed and asks whether they want that for it.
    /// </summary>
    private void QuoteSale(Entity<TraderUsedShipsComponent> ent, Entity<TraderComponent> trader, EntityUid customer)
    {
        if (!TryGetDeedShip(trader, customer, out _, out var shuttle))
        {
            _trader.SayAndShow(trader, Loc.GetString("trader-used-no-ship"));
            return;
        }

        if (!CanTakeShip(trader, shuttle))
            return;

        if (!_shipyard.TryHostConsole(ent.Owner, ent.Comp.Console, out _))
        {
            _trader.SayAndShow(trader, Loc.GetString("trader-cannot-help"));
            return;
        }

        var quote = _shipyard.GetShipResaleValue(ent.Owner, shuttle);
        _shipyard.ClearHostedConsole(ent.Owner);

        ent.Comp.AwaitingConfirmation = true;
        ent.Comp.QuotedShip = shuttle;

        _trader.AskConfirmation(trader, Loc.GetString("trader-used-quote",
            ("amount", BankSystemExtensions.ToSpesoString(quote)),
            ("ship", Name(shuttle))));
    }

    private void OnConfirmed(Entity<TraderUsedShipsComponent> ent, ref TraderConfirmedEvent args)
    {
        if (!ent.Comp.AwaitingConfirmation)
            return;

        ent.Comp.AwaitingConfirmation = false;
        var quoted = ent.Comp.QuotedShip;
        ent.Comp.QuotedShip = null;

        if (!args.Accepted || !TryComp<TraderComponent>(ent, out var trader))
            return;

        var traderEnt = (ent.Owner, trader);
        var customer = args.Customer;

        // The card and the ship can both have moved while the customer was thinking.
        if (!TryGetDeedShip(traderEnt, customer, out var idCard, out var shuttle) || shuttle != quoted)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-used-no-ship"));
            return;
        }

        // Somebody could have walked a borg aboard while the customer was thinking.
        if (!CanTakeShip(traderEnt, shuttle))
            return;

        if (!_shipyard.TryHostConsole(ent.Owner, ent.Comp.Console, out var uiKey))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-cannot-help"));
            return;
        }

        if (!_trader.TryHoldItem(traderEnt, idCard) || !_shipyard.TryInsertHostedId(ent.Owner, idCard))
        {
            _trader.ReturnHeldItems(traderEnt);
            _shipyard.ClearHostedConsole(ent.Owner);
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-cannot-help"));
            return;
        }

        var bill = _shipyard.GetShipResaleValue(ent.Owner, shuttle);
        var shipName = Name(shuttle);

        // Everything past here is the console's own sale: docking, organics, taxes, the bank deposit.
        var sold = _shipyard.TryHostedSell(ent.Owner, customer, uiKey, idCard, out var refusal);

        _trader.ReturnHeldItems(traderEnt);
        _shipyard.ClearHostedConsole(ent.Owner);

        if (!sold)
        {
            _trader.SayAndShow(traderEnt, refusal == null
                ? Loc.GetString("trader-used-sale-refused")
                : Loc.GetString("trader-used-sale-refused-reason", ("reason", refusal)));
            return;
        }

        PrintSaleReceipt(traderEnt, customer, shipName, bill);
        _trader.SayAndShow(traderEnt, Loc.GetString("trader-used-sale-done",
            ("amount", BankSystemExtensions.ToSpesoString(bill))));
    }

    /// <summary>
    /// Whether the salesman can copy the hull at all. Anything the map loader will not write out is
    /// gone the moment the ship is sold, so it is sent off the ship rather than quietly destroyed.
    /// </summary>
    private bool CanTakeShip(Entity<TraderComponent> trader, EntityUid shuttle)
    {
        var unsavable = _market.GetUnsavableAboard(shuttle);
        if (unsavable.Count == 0)
            return true;

        _trader.SayAndShow(trader, Loc.GetString("trader-used-unsavable-aboard",
            ("thing", Name(unsavable[0]))));
        return false;
    }

    /// <summary>
    /// The customer's own deed card in the barter zone, and the ship it points at.
    /// </summary>
    private bool TryGetDeedShip(Entity<TraderComponent> trader, EntityUid customer, out EntityUid idCard, out EntityUid shuttle)
    {
        shuttle = default;

        if (!_trader.TryGetZoneId(trader, customer, out idCard, out var wrongOwner))
        {
            if (wrongOwner)
                _trader.SayAndShow(trader, Loc.GetString("trader-not-your-id"));

            return false;
        }

        return _shipyard.TryGetDeedShip(idCard, out shuttle);
    }

    #endregion

    #region Buying off the lot

    /// <summary>
    /// The lot is free to look at; the card and the money are only wanted at the point of sale. The
    /// conversation window stays up alongside it, the way the shop's does, because that is where the
    /// salesman answers a refused purchase.
    /// </summary>
    private void OpenLot(Entity<TraderUsedShipsComponent> ent, Entity<TraderComponent> trader, EntityUid customer)
    {
        _ui.TryOpenUi(ent.Owner, TraderUiKey.UsedShips, customer);
        UpdateState(ent, trader, customer);
    }

    private void OnUiClosed(Entity<TraderUsedShipsComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not TraderUiKey.UsedShips)
            return;

        if (!TryComp<TraderComponent>(ent, out var trader) || trader.Customer != args.Actor)
            return;

        _trader.EndConversation((ent.Owner, trader));
    }

    private void OnConversationEnded(Entity<TraderUsedShipsComponent> ent, ref TraderConversationEndedEvent args)
    {
        ent.Comp.AwaitingConfirmation = false;
        ent.Comp.QuotedShip = null;
        _shipyard.ClearHostedConsole(ent.Owner);
    }

    private void OnBuy(Entity<TraderUsedShipsComponent> ent, ref TraderUsedShipBuyMessage args)
    {
        if (!TryComp<TraderComponent>(ent, out var trader) || trader.Customer != args.Actor)
            return;

        var traderEnt = (ent.Owner, trader);
        _trader.NoteInput(traderEnt);

        if (!_market.TryGetAvailableListing(args.Listing, out var listing))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-used-gone"));
            UpdateState(ent, traderEnt, args.Actor);
            return;
        }

        if (_market.GetStation(ent.Owner) is not { } station)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-cannot-help"));
            return;
        }

        // Same rule the console has: one ship per card.
        if (!_trader.TryGetZoneId(traderEnt, args.Actor, out var idCard, out var wrongOwner))
        {
            _trader.SayAndShow(traderEnt, wrongOwner
                ? Loc.GetString("trader-not-your-id")
                : Loc.GetString("trader-request-item", ("thing", Loc.GetString("trader-thing-id"))));
            return;
        }

        if (_shipyard.HasDeed(idCard))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("shipyard-console-already-deeded"));
            return;
        }

        // The deed needs a player behind the customer; find out before anything changes hands.
        if (!TryComp<ActorComponent>(args.Actor, out var actor) || actor.PlayerSession is not { } session)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-used-no-session"));
            return;
        }

        // Fetched before it is paid for: a hull that will not come out of the yard costs nothing,
        // and nobody's cash has to be handed back as change it did not arrive as.
        if (!_market.TryLoadListing(listing, station, out var shuttle))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-used-load-failed"));
            return;
        }

        if (!_trader.TryTakePayment(traderEnt, args.Actor, listing.Price))
        {
            QueueDel(shuttle.Value);
            return;
        }

        if (!AssignDeed(shuttle.Value, idCard, session, listing))
        {
            // Paid but not deeded: scrap the hull, keep the listing and put the money back in the bank.
            QueueDel(shuttle.Value);
            _bank.TryBankDeposit(args.Actor, listing.Price, tax: false);
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-used-deed-failed"));
            UpdateState(ent, traderEnt, args.Actor);
            return;
        }

        _market.RemoveListing(listing);
        GrantConsolePerks(ent, idCard, args.Actor);

        PrintPurchaseReceipt(traderEnt, args.Actor, listing);
        var sold = Loc.GetString("trader-used-sold", ("ship", listing.ShipName));
        _trader.SayAndShow(traderEnt, sold);
        _trader.Say(traderEnt, sold);
        _radio.SendRadioMessage(ent.Owner, Loc.GetString("shipyard-console-docking",
            ("owner", Name(args.Actor)), ("vessel", listing.ShipName)), ent.Comp.RadioChannel, ent.Owner);
        _direction.SendShipDirectionMessage(args.Actor, shuttle.Value);
        UpdateState(ent, traderEnt, args.Actor);
    }

    /// <summary>
    /// What the console this salesman stands in for would put on the buyer's card: the access levels
    /// the ship's own doors expect, and the job title.
    /// </summary>
    private void GrantConsolePerks(Entity<TraderUsedShipsComponent> ent, EntityUid idCard, EntityUid buyer)
    {
        if (!_proto.TryIndex(ent.Comp.Console, out var consoleProto)
            || !consoleProto.TryGetComponent<ShipyardConsoleComponent>(out var console, EntityManager.ComponentFactory))
        {
            return;
        }

        if (console.NewAccessLevels.Count > 0 && TryComp<AccessComponent>(idCard, out var access))
        {
            var tags = access.Tags.ToList();
            tags.AddRange(console.NewAccessLevels);
            _access.TrySetTags(idCard, tags, access);
        }

        if (!string.IsNullOrEmpty(console.NewJobTitle))
            _idCard.TryChangeJobTitle(idCard, console.NewJobTitle, player: buyer);
    }

    /// <summary>
    /// Registers the loaded hull to the buyer exactly as a fresh purchase would, keeping its name.
    /// </summary>
    public bool AssignDeed(EntityUid shuttle, EntityUid idCard, ICommonSession buyer, UsedShipListing listing)
    {
        return _shipyard.TryAssignDeed(shuttle, idCard, buyer, _market.GetDesign(listing), listing.ShipName);
    }

    private void UpdateState(Entity<TraderUsedShipsComponent> ent, Entity<TraderComponent> trader, EntityUid customer)
    {
        var entries = new List<TraderUsedShipEntry>();
        foreach (var listing in _market.GetAvailable())
        {
            entries.Add(new TraderUsedShipEntry(listing.Id, listing.ShipName, listing.DesignName,
                listing.SellerName, listing.Price));
        }

        string? idName = null;
        if (_trader.TryGetZoneId(trader, customer, out var idCard, out _))
            idName = Name(idCard);

        _ui.SetUiState(ent.Owner, TraderUiKey.UsedShips,
            new TraderUsedShipsState(entries, _trader.GetZoneCash(trader), idName, _trader.GetBalance(customer)));
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _refreshAccumulator += frameTime;
        if (_refreshAccumulator < 1f)
            return;

        _refreshAccumulator = 0f;

        // What is on the table changes while the lot is open, and the footer has to keep up.
        var query = EntityQueryEnumerator<TraderUsedShipsComponent, TraderComponent>();
        while (query.MoveNext(out var uid, out var used, out var trader))
        {
            if (trader.Customer is not { } customer)
                continue;

            if (!_ui.IsUiOpen((uid, null), TraderUiKey.UsedShips, customer))
                continue;

            UpdateState((uid, used), (uid, trader), customer);
        }
    }

    /// <summary>
    /// Every salesman calls out a fresh arrival on the lot, out loud and on the traffic channel.
    /// </summary>
    private void OnListed(ref UsedShipListedEvent args)
    {
        var line = Loc.GetString("trader-used-new-stock",
            ("ship", args.Listing.ShipName),
            ("design", args.Listing.DesignName),
            ("price", BankSystemExtensions.ToSpesoString(args.Listing.Price)));

        var query = EntityQueryEnumerator<TraderUsedShipsComponent, TraderComponent>();
        while (query.MoveNext(out var uid, out var used, out var trader))
        {
            _trader.Say((uid, trader), line);
            _radio.SendRadioMessage(uid, line, used.RadioChannel, uid);
        }
    }

    #region Receipts

    private void PrintSaleReceipt(Entity<TraderComponent> trader, EntityUid customer, string ship, int amount)
    {
        var body = new StringBuilder();
        body.AppendLine(Loc.GetString("trader-used-sale-receipt-header",
            ("trader", Name(trader)),
            ("seller", Name(customer)),
            ("time", _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"))));
        body.AppendLine(Loc.GetString("trader-used-sale-receipt-line",
            ("ship", ship),
            ("amount", BankSystemExtensions.ToSpesoString(amount))));

        _trader.PrintReceipt(trader, Loc.GetString("trader-used-sale-receipt-name"), body.ToString());
    }

    private void PrintPurchaseReceipt(Entity<TraderComponent> trader, EntityUid customer, UsedShipListing listing)
    {
        var body = new StringBuilder();
        body.AppendLine(Loc.GetString("trader-used-buy-receipt-header",
            ("trader", Name(trader)),
            ("customer", Name(customer)),
            ("time", _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"))));
        body.AppendLine(Loc.GetString("trader-used-buy-receipt-line",
            ("ship", listing.ShipName),
            ("design", listing.DesignName),
            ("seller", listing.SellerName),
            ("price", BankSystemExtensions.ToSpesoString(listing.Price))));

        _trader.PrintReceipt(trader, Loc.GetString("trader-used-buy-receipt-name"), body.ToString());
    }

    #endregion
}
