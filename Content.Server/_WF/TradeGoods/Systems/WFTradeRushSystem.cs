using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._WF.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>A station's rush order: so many crates of a group, before a time, at a premium.</summary>
public sealed class WFTradeRush
{
    public EntityUid Grid;
    public string Group = string.Empty;
    public LocId Goods;
    public int Total;
    public int Remaining;
    public float Factor;
    public TimeSpan Ends;

    /// <summary>A big order for a full hold, on its own schedule and wording, paying the same for its last crate as its first.</summary>
    public bool Bulk;

    /// <summary>Crates each ship or seller delivered toward it.</summary>
    public readonly Dictionary<string, int> Filled = new();
}

/// <summary>
/// Every so often a station posts a rush order for a number of crates of one group and pays a premium for each until
/// the order is filled or expires. The first ships to deliver split it. Bulk orders work the same way, for a hold's
/// worth of crates over a longer window at a flat price. Orders go to the stations furthest under target for their goods.
/// </summary>
public sealed partial class WFTradeRushSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;

    /// <summary>Most rush orders, and most bulk orders, open across the sector at once.</summary>
    private const int MaxActive = 2;

    /// <summary>How much likelier an order is for goods a station is scarce of.</summary>
    private const float ScarceDraw = 8f;

    private readonly List<WFTradeRush> _active = new();
    private TimeSpan? _next;
    private TimeSpan? _nextBulk;

    public IReadOnlyList<WFTradeRush> Active => _active;

    /// <summary>Withdraws every open rush order without an announcement, and returns how many.</summary>
    public int Clear()
    {
        var count = _active.Count;
        _active.Clear();
        return count;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTradeQuoteEvent>(OnQuote);
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _active.Clear();
        _next = null;
        _nextBulk = null;
    }

    /// <summary>Whether a station has an open order that still wants a good.</summary>
    public bool Wants(EntityUid grid, string good)
    {
        return RushFor(grid, good) != null;
    }

    private WFTradeRush? RushFor(EntityUid grid, string good)
    {
        return _active.FirstOrDefault(rush => rush.Grid == grid && rush.Remaining > 0 && _market.InGroup(good, rush.Group));
    }

    private void OnQuote(WFTradeQuoteEvent args)
    {
        if (RushFor(args.Grid, args.Good) is not { } rush)
            return;

        args.Modifiers.Add(new WFTradeModifier(rush.Factor, rush.Bulk ? "wf-trade-reason-bulk" : "wf-trade-reason-rush"));
        args.NoGlut |= rush.Bulk;
    }

    private void OnSale(WFTradeSaleEvent args)
    {
        if (RushFor(args.Grid, args.Good) is not { } rush)
            return;

        var seller = args.Ship is { } ship ? Name(ship) : Name(args.Seller);
        rush.Remaining--;
        rush.Filled[seller] = rush.Filled.GetValueOrDefault(seller) + 1;
        if (rush.Remaining > 0)
            return;

        _active.Remove(rush);
        var fills = string.Join(", ", rush.Filled.OrderByDescending(fill => fill.Value)
            .Select(fill => Loc.GetString("wf-trade-rush-fill", ("seller", fill.Key), ("count", fill.Value))));
        _market.Announce(Loc.GetString("wf-trade-rush-filled", ("bulk", rush.Bulk), ("station", Name(rush.Grid)), ("fills", fills)), rush.Grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _timing.CurTime;
        foreach (var rush in _active.ToList())
        {
            if (TerminatingOrDeleted(rush.Grid))
            {
                _active.Remove(rush);
                continue;
            }

            if (now < rush.Ends)
                continue;

            _active.Remove(rush);
            _market.Announce(Loc.GetString("wf-trade-rush-expired", ("bulk", rush.Bulk), ("station", Name(rush.Grid)), ("goods", Loc.GetString(rush.Goods))), rush.Grid);
        }

        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        _next = PostDue(_next, profile.RushMinutes, false);
        _nextBulk = PostDue(_nextBulk, profile.BulkMinutes, true);
    }

    /// <summary>Posts an order of one kind if it's due, and returns when the next one is due.</summary>
    private TimeSpan PostDue(TimeSpan? next, Vector2i minutes, bool bulk)
    {
        var now = _timing.CurTime;
        if (next is { } due && now < due)
            return due;

        if (next != null && _active.Count(rush => rush.Bulk == bulk) < MaxActive)
            Start(bulk: bulk);

        return now + TimeSpan.FromMinutes(_random.Next(minutes.X, Math.Max(minutes.X, minutes.Y) + 1));
    }

    /// <summary>
    /// Posts a rush or bulk order, random unless forced, at a station that buys its goods, isn't blockaded and has no
    /// order open.
    /// </summary>
    public WFTradeRush? Start(ProtoId<WFTradeRushPrototype>? forced = null, bool bulk = false)
    {
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var blockaded = _sector.Blockaded().ToHashSet();
        var chutes = _market.Chutes();
        var options = new List<(WFTradeRushPrototype Proto, List<EntityUid> Grids, float Weight)>();
        foreach (var proto in _proto.EnumeratePrototypes<WFTradeRushPrototype>())
        {
            if (forced != null ? proto.ID != forced : proto.Bulk != bulk)
                continue;

            var goods = _market.Group(profile, proto.Group);
            var grids = chutes
                .Where(chute => (proto.Chutes.Count == 0 || MetaData(chute.Chute).EntityPrototype is { } chuteProto && proto.Chutes.Contains(chuteProto.ID))
                                && goods.Any(good => chute.Dispenser.Inventory.ContainsKey(good) || _market.Restocking(chute.Grid, good))
                                && !blockaded.Contains(chute.Grid)
                                && _active.All(rush => rush.Grid != chute.Grid))
                .Select(chute => chute.Grid)
                .Distinct()
                .ToList();

            if (grids.Count == 0)
                continue;

            // A station scarce of the goods posts first, and the worst-off of those.
            var scarce = grids.Any(grid => goods.Any(good => _market.Restocking(grid, good)));
            options.Add((proto, grids, scarce ? proto.Weight * ScarceDraw : proto.Weight));
        }

        if (options.Count == 0)
            return null;

        var total = options.Sum(option => option.Weight);
        var roll = _random.NextFloat() * total;
        var (picked, candidates, _) = options[^1];
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll <= 0f)
            {
                (picked, candidates, _) = option;
                break;
            }
        }

        _random.Shuffle(candidates);
        var pickedGoods = _market.Group(profile, picked.Group);
        var count = _random.Next(picked.Count.X, Math.Max(picked.Count.X, picked.Count.Y) + 1);
        var minutes = _random.Next(picked.Minutes.X, Math.Max(picked.Minutes.X, picked.Minutes.Y) + 1);
        var rush = new WFTradeRush
        {
            Grid = candidates.MinBy(grid => pickedGoods.Min(good => _market.KnownLevel(grid, good))),
            Group = picked.Group,
            Goods = picked.Goods,
            Total = count,
            Remaining = count,
            Factor = MathF.Round(_random.NextFloat(picked.MinFactor, picked.MaxFactor), 2),
            Ends = _timing.CurTime + TimeSpan.FromMinutes(minutes),
            Bulk = picked.Bulk,
        };

        _active.Add(rush);
        _market.Announce(Loc.GetString("wf-trade-rush-start", ("bulk", rush.Bulk),
            ("station", Name(rush.Grid)), ("count", count), ("goods", Loc.GetString(rush.Goods)),
            ("factor", WFTradeMarketSystem.Shown(rush.Factor)), ("minutes", minutes)), rush.Grid);

        return rush;
    }
}
