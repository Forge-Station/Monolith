using System.Linq;
using System.Text;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.TradeGoods.Components;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Paper;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>A chute's live quote for one good: its base price, what it pays now and the reason players see.</summary>
public readonly record struct WFTradeQuote(int Base, int Amount, float Factor, LocId Reason);

/// <summary>
/// Station markets for the trade-goods chutes. Each chute grid keeps a pressure per good that sales raise and time
/// decays; sector modules add modifiers through <see cref="WFTradeQuoteEvent"/>. The chute pays the quote taken when
/// the crate went in.
/// </summary>
public sealed partial class WFTradeMarketSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFCargoLoadSystem _cargo = default!;

    public static readonly ProtoId<WFTradeMarketPrototype> DefaultProfile = "WFTradeMarketDefault";
    private static readonly EntProtoId Cash = "SpaceCash";

    /// <summary>Quotes this far below base suggest a better station in the sale popup.</summary>
    private const float LowQuote = 0.7f;

    /// <summary>Pressure above which a freighter's recent supply is named as the reason for a lower price.</summary>
    private const float SuppliedPressure = 0.02f;

    private readonly Dictionary<string, int?> _cashValues = new();

    /// <summary>Until when NPC freight supply explains a good's low price at a market.</summary>
    private readonly Dictionary<(EntityUid Grid, string Good), TimeSpan> _supplied = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DispenserComponent, ExaminedEvent>(OnChuteExamined);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        InitializeOrigin();
        InitializeBuy();
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _supplied.Clear();
        _pending.Clear();
        _exchange = null;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            _cashValues.Clear();
    }

    /// <summary>The market profile a grid uses.</summary>
    public WFTradeMarketPrototype Profile(EntityUid? grid)
    {
        if (TryComp<WFTradeMarketComponent>(grid, out var market) && _proto.TryIndex(market.Profile, out var profile))
            return profile;

        return _proto.Index(DefaultProfile);
    }

    /// <summary>What a chute's own table pays for a good, or null if it does not buy it.</summary>
    public int? BasePrice(DispenserComponent chute, string good)
    {
        return chute.Inventory.TryGetValue(good, out var cash) ? CashValue(cash) : null;
    }

    /// <summary>The spesos a cash prototype is worth, from its stack count.</summary>
    private int? CashValue(string cash)
    {
        if (_cashValues.TryGetValue(cash, out var cached))
            return cached;

        int? value = null;
        if (_proto.TryIndex<EntityPrototype>(cash, out var proto) && proto.TryGetComponent<StackComponent>(out var stack, _factory))
            value = stack.Count;

        _cashValues[cash] = value;
        return value;
    }

    /// <summary>A good's pressure at a market now, decayed since it was last touched.</summary>
    public float Pressure(EntityUid grid, string good)
    {
        if (!TryComp<WFTradeMarketComponent>(grid, out var market) || !market.Pressure.TryGetValue(good, out var entry))
            return 0f;

        var profile = Profile(grid);
        var elapsed = (_timing.CurTime - entry.Touched).TotalSeconds;
        var halfLife = Math.Max(1.0, profile.HalfLife.TotalSeconds);
        var pressure = (float) (entry.Pressure * Math.Pow(0.5, elapsed / halfLife));
        market.Pressure[good] = (pressure, _timing.CurTime);
        return pressure;
    }

    /// <summary>
    /// Adds pressure to a good at a market, creating the market if needed. Supply from freight is tagged as such, and
    /// eases the good's scarcity at the station's vendors by the same amount.
    /// </summary>
    public void AddPressure(EntityUid grid, string good, float amount, bool supplied = false)
    {
        var market = EnsureComp<WFTradeMarketComponent>(grid);
        var pressure = Pressure(grid, good);
        market.Pressure[good] = (Math.Max(0f, pressure + amount), _timing.CurTime);
        if (!supplied)
            return;

        _supplied[(grid, good)] = _timing.CurTime + Profile(grid).HalfLife;
        AddScarcity(grid, good, -amount);
    }

    /// <summary>Crates of a good delivered to a market this round.</summary>
    public int Delivered(EntityUid grid, string good)
    {
        return TryComp<WFTradeMarketComponent>(grid, out var market) ? market.Delivered.GetValueOrDefault(good) : 0;
    }

    /// <summary>What a chute pays for a good now, or for one crate of it from a seller when given.</summary>
    public WFTradeQuote? Quote(EntityUid chute, string good, EntityUid? crate = null, EntityUid? seller = null)
    {
        if (!TryComp<DispenserComponent>(chute, out var dispenser)
            || BasePrice(dispenser, good) is not { } basePrice
            || basePrice <= 0
            || Transform(chute).GridUid is not { } grid)
            return null;

        var profile = Profile(grid);
        // Contraband has a fixed street price: sales never glut it.
        var pressure = profile.ContrabandGoods.Contains(good) ? 0f : Pressure(grid, good);
        var floor = profile.FloorPrices.TryGetValue(good, out var floorPrice)
            ? Math.Min(floorPrice, basePrice)
            : basePrice * profile.Floor;
        var price = MathF.Max(basePrice * (1f - pressure), floor);

        var ev = new WFTradeQuoteEvent(grid, chute, good, crate, seller);
        RaiseLocalEvent(ev);

        var raise = 1f;
        var lower = 1f;
        var top = 1f;
        LocId? raiseReason = null;
        LocId? lowerReason = null;
        foreach (var modifier in ev.Modifiers)
        {
            if (modifier.Factor >= 1f)
            {
                raise *= modifier.Factor;
                if (modifier.Factor > top)
                {
                    top = modifier.Factor;
                    raiseReason = modifier.Reason;
                }
            }
            else
            {
                lower *= Math.Max(0f, modifier.Factor);
                lowerReason = modifier.Reason;
            }
        }

        // Contraband has a fixed street price: no event raises it.
        if (profile.ContrabandGoods.Contains(good))
        {
            raise = 1f;
            raiseReason = null;
        }

        raise = Math.Min(raise, profile.ModifierCap);
        var amount = (int) MathF.Round(price * raise * lower);
        var supplied = _supplied.TryGetValue((grid, good), out var until) && _timing.CurTime < until;
        var reason = lowerReason
                     ?? raiseReason
                     ?? (crate == null && IsSealedOnly(chute) ? "wf-trade-reason-sealed-only"
                         : supplied && pressure >= SuppliedPressure ? "wf-trade-reason-supplied"
                         : pressure < 0.15f ? "wf-trade-reason-steady"
                         : "wf-trade-reason-glutted");
        return new WFTradeQuote(basePrice, amount, amount / (float) basePrice, reason);
    }

    /// <summary>
    /// Takes a crate into a chute: quotes it, holds the amount for the payout, adds pressure and raises the sale.
    /// </summary>
    public void Sell(EntityUid chute, EntityUid crate, EntityUid seller, int? fixedAmount = null)
    {
        if (Transform(chute).GridUid is not { } grid || MetaData(crate).EntityPrototype?.ID is not { } good)
            return;

        int amount;
        if (fixedAmount is { } fixedPrice)
        {
            amount = fixedPrice;
        }
        else
        {
            if (Quote(chute, good, crate, seller) is not { } quote)
                return;

            amount = quote.Amount;
            var profile = Profile(grid);
            if (!profile.ContrabandGoods.Contains(good))
                AddPressure(grid, good, profile.PerSaleOverrides.GetValueOrDefault(good, profile.PerSale));
            if (quote.Factor < LowQuote && Best(good, grid) is { } best)
            {
                _popup.PopupEntity(Loc.GetString("wf-trade-sale-low",
                    ("amount", amount), ("station", best.Station), ("best", best.Amount)), chute, seller);
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("wf-trade-sale", ("amount", amount)), chute, seller);
            }
        }

        var market = EnsureComp<WFTradeMarketComponent>(grid);
        market.Delivered[good] = market.Delivered.GetValueOrDefault(good) + 1;
        EnsureComp<WFTradeChuteComponent>(chute).PendingAmount = amount;

        var ship = CompOrNull<WFTradeOriginComponent>(crate)?.Carrier;
        if (ship is { } carrier && TerminatingOrDeleted(carrier))
            ship = null;

        RaiseLocalEvent(new WFTradeSaleEvent(grid, chute, good, amount, seller, ship, crate));
    }

    /// <summary>
    /// Pays a held quote in cash, or prints live prices on the empty-hand paper. False leaves the chute to dispense as
    /// it always has.
    /// </summary>
    public bool Payout(EntityUid chute, DispenserComponent dispenser, string itemId)
    {
        var coordinates = Transform(chute).Coordinates;
        if (TryComp<WFTradeChuteComponent>(chute, out var pending) && pending.PendingAmount is { } amount)
        {
            pending.PendingAmount = null;
            if (amount > 0)
                _stack.SpawnMultiple(Cash, amount, coordinates);

            return true;
        }

        if (itemId != dispenser.DefaultItem)
            return false;

        var paper = Spawn(itemId, coordinates);
        if (TryComp<PaperComponent>(paper, out var paperComp))
            _paper.SetContent((paper, paperComp), PriceSheet(chute, dispenser));

        return true;
    }

    /// <summary>The live price list a chute prints.</summary>
    private string PriceSheet(EntityUid chute, DispenserComponent dispenser)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Loc.GetString("wf-trade-paper-header", ("station", StationName(chute))));
        foreach (var line in PriceLines(chute, dispenser))
        {
            builder.AppendLine(line);
        }

        builder.AppendLine();
        builder.Append(Loc.GetString("wf-trade-paper-footer"));
        return builder.ToString();
    }

    private void OnChuteExamined(Entity<DispenserComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.Inventory.Count == 0)
            return;

        using (args.PushGroup(nameof(WFTradeMarketSystem)))
        {
            args.PushMarkup(Loc.GetString("wf-trade-examine-header"));
            foreach (var line in PriceLines(ent, ent.Comp))
            {
                args.PushMarkup(line);
            }
        }
    }

    /// <summary>One line per good a chute buys, highest price first.</summary>
    private List<string> PriceLines(EntityUid chute, DispenserComponent dispenser)
    {
        var lines = new List<(int Amount, string Text)>();
        foreach (var good in dispenser.Inventory.Keys)
        {
            if (Quote(chute, good) is not { } quote)
                continue;

            var tonnes = _cargo.GoodWeight(good);
            lines.Add((quote.Amount, Loc.GetString("wf-trade-price-line",
                ("good", GoodName(good)),
                ("amount", quote.Amount),
                ("trend", Trend(quote.Factor)),
                ("reason", Loc.GetString(quote.Reason)),
                ("tonnes", tonnes),
                ("perTonne", tonnes > 0f ? (int) (quote.Amount / tonnes) : 0))));
        }

        return lines.OrderByDescending(line => line.Amount).Select(line => line.Text).ToList();
    }

    /// <summary>
    /// A price factor as players read it, to two decimals. A float handed to Fluent widens to double first, so 2.3 would
    /// print as 2.29999995.
    /// </summary>
    public static double Shown(float factor)
    {
        return Math.Round((double) factor, 2);
    }

    /// <summary>An arrow for a price against its base.</summary>
    public static string Trend(float factor)
    {
        return factor >= 1.02f ? "▲" : factor <= 0.98f ? "▼" : "–";
    }

    /// <summary>A trade good's display name.</summary>
    public string GoodName(string good)
    {
        return _proto.TryIndex<EntityPrototype>(good, out var proto) ? proto.Name : good;
    }

    /// <summary>The name players know a chute's station by: its grid's name.</summary>
    public string StationName(EntityUid chute)
    {
        return Transform(chute).GridUid is { } grid ? Name(grid) : Name(chute);
    }

    /// <summary>Every chute on a market map, with its grid.</summary>
    public List<(EntityUid Chute, DispenserComponent Dispenser, EntityUid Grid)> Chutes()
    {
        var chutes = new List<(EntityUid, DispenserComponent, EntityUid)>();
        var query = EntityQueryEnumerator<DispenserComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var dispenser, out var xform))
        {
            if (xform.GridUid is { } grid && xform.Anchored && IsMarketMap(xform.MapID) && dispenser.Inventory.Count > 0)
                chutes.Add((uid, dispenser, grid));
        }

        return chutes;
    }

    /// <summary>The open station paying most for a good now, other than one grid. Hidden markets are never suggested.</summary>
    public (string Station, int Amount, EntityUid Chute)? Best(string good, EntityUid? except = null)
    {
        (string, int, EntityUid)? best = null;
        foreach (var (chute, _, grid) in Chutes())
        {
            if (grid == except || IsHiddenChute(chute) || Quote(chute, good) is not { } quote)
                continue;

            if (best is not { } current || quote.Amount > current.Item2)
                best = (StationName(chute), quote.Amount, chute);
        }

        return best;
    }

    /// <summary>The goods of a named group in a profile, or none.</summary>
    public IReadOnlyList<EntProtoId> Group(WFTradeMarketPrototype profile, string group)
    {
        return profile.Groups.TryGetValue(group, out var goods) ? goods : Array.Empty<EntProtoId>();
    }

    /// <summary>Whether a good belongs to a named group of the default profile.</summary>
    public bool InGroup(string good, string group)
    {
        return _proto.Index(DefaultProfile).Groups.TryGetValue(group, out var goods) && goods.Contains(good);
    }

    /// <summary>Whether a map holds markets: the sector map, or one marked as a market map.</summary>
    public bool IsMarketMap(MapId map)
    {
        if (map == MapId.Nullspace)
            return false;

        if (map == _ticker.DefaultMap)
            return true;

        return _map.TryGetMap(map, out var uid) && HasComp<WFTradeMarketMapComponent>(uid);
    }
}
