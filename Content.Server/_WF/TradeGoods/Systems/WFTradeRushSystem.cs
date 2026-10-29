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

    /// <summary>Crates each ship or seller delivered toward it.</summary>
    public readonly Dictionary<string, int> Filled = new();
}

/// <summary>
/// Every so often a station posts a rush order for a number of crates of one group and pays a premium for each until
/// the order is filled or expires. The first ships to deliver split it.
/// </summary>
public sealed class WFTradeRushSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;

    /// <summary>Most rush orders open across the sector at once.</summary>
    private const int MaxActive = 2;

    private readonly List<WFTradeRush> _active = new();
    private TimeSpan? _next;

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
    }

    private WFTradeRush? RushFor(EntityUid grid, string good)
    {
        return _active.FirstOrDefault(rush => rush.Grid == grid && rush.Remaining > 0 && _market.InGroup(good, rush.Group));
    }

    private void OnQuote(WFTradeQuoteEvent args)
    {
        if (RushFor(args.Grid, args.Good) is { } rush)
            args.Modifiers.Add(new WFTradeModifier(rush.Factor, "wf-trade-reason-rush"));
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
        _market.Announce(Loc.GetString("wf-trade-rush-filled", ("station", Name(rush.Grid)), ("fills", fills)), rush.Grid);
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
            _market.Announce(Loc.GetString("wf-trade-rush-expired", ("station", Name(rush.Grid)), ("goods", Loc.GetString(rush.Goods))), rush.Grid);
        }

        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        _next ??= now + TimeSpan.FromMinutes(_random.Next(profile.RushMinutes.X, Math.Max(profile.RushMinutes.X, profile.RushMinutes.Y) + 1));
        if (now < _next)
            return;

        _next = now + TimeSpan.FromMinutes(_random.Next(profile.RushMinutes.X, Math.Max(profile.RushMinutes.X, profile.RushMinutes.Y) + 1));
        if (_active.Count < MaxActive)
            Start();
    }

    /// <summary>Posts a random rush order at a station that buys its goods, isn't blockaded and has no rush open.</summary>
    public WFTradeRush? Start(ProtoId<WFTradeRushPrototype>? forced = null)
    {
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var blockaded = _sector.Blockaded().ToHashSet();
        var chutes = _market.Chutes();
        var options = new List<(WFTradeRushPrototype Proto, List<EntityUid> Grids)>();
        foreach (var proto in _proto.EnumeratePrototypes<WFTradeRushPrototype>())
        {
            if (forced != null && proto.ID != forced)
                continue;

            var goods = _market.Group(profile, proto.Group);
            var grids = chutes
                .Where(chute => (proto.Chutes.Count == 0 || MetaData(chute.Chute).EntityPrototype is { } chuteProto && proto.Chutes.Contains(chuteProto.ID))
                                && goods.Any(good => chute.Dispenser.Inventory.ContainsKey(good))
                                && !blockaded.Contains(chute.Grid)
                                && _active.All(rush => rush.Grid != chute.Grid))
                .Select(chute => chute.Grid)
                .Distinct()
                .ToList();

            if (grids.Count > 0)
                options.Add((proto, grids));
        }

        if (options.Count == 0)
            return null;

        var total = options.Sum(option => option.Proto.Weight);
        var roll = _random.NextFloat() * total;
        var (picked, candidates) = options[^1];
        foreach (var option in options)
        {
            roll -= option.Proto.Weight;
            if (roll <= 0f)
            {
                (picked, candidates) = option;
                break;
            }
        }

        var count = _random.Next(picked.Count.X, Math.Max(picked.Count.X, picked.Count.Y) + 1);
        var minutes = _random.Next(picked.Minutes.X, Math.Max(picked.Minutes.X, picked.Minutes.Y) + 1);
        var rush = new WFTradeRush
        {
            Grid = _random.Pick(candidates),
            Group = picked.Group,
            Goods = picked.Goods,
            Total = count,
            Remaining = count,
            Factor = MathF.Round(_random.NextFloat(picked.MinFactor, picked.MaxFactor), 2),
            Ends = _timing.CurTime + TimeSpan.FromMinutes(minutes),
        };

        _active.Add(rush);
        _market.Announce(Loc.GetString("wf-trade-rush-start",
            ("station", Name(rush.Grid)), ("count", count), ("goods", Loc.GetString(rush.Goods)),
            ("factor", WFTradeMarketSystem.Shown(rush.Factor)), ("minutes", minutes)), rush.Grid);

        return rush;
    }
}
