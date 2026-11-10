using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._WF.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// An active shortage at one station, or a boom across the sector when <c>Grid</c> is null. It raises a group's goods,
/// or exactly <c>Goods</c> when set, such as the cargo a lost freight run was bringing. A report names a real gap on a
/// station's shelf, which the price already shows: it adds no modifier, says why, and ends when the shelf recovers.
/// </summary>
public sealed record WFTradeShortage(EntityUid? Grid, string Group, float Factor, TimeSpan Ends, string Name, HashSet<string>? Goods = null, string? Cause = null)
{
    public bool Report => Cause != null;
}

/// <summary>
/// Every so often the worst real gap in the sector's ledgers is reported as a shortage, with its cause, or the whole
/// sector booms for a group of goods and pays more for it for a while, as outside demand.
/// </summary>
public sealed class WFTradeShortageSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ServerGameTicker _ticker = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;

    /// <summary>How long a lost freighter is remembered as the cause of a station's shortages.</summary>
    private static readonly TimeSpan LostMemory = TimeSpan.FromHours(1);

    private readonly List<WFTradeShortage> _active = new();
    private TimeSpan? _next;

    /// <summary>When each station last lost a freighter on its way.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _lost = new();

    public IReadOnlyList<WFTradeShortage> Active => _active;

    /// <summary>Ends every shortage and boom without an announcement, and returns how many.</summary>
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
        SubscribeLocalEvent<WFTradeBuyQuoteEvent>(OnBuyQuote);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _active.Clear();
        _lost.Clear();
        _next = null;
    }

    private void OnQuote(WFTradeQuoteEvent args)
    {
        var now = _timing.CurTime;
        foreach (var shortage in _active)
        {
            if (shortage.Ends > now
                && !shortage.Report
                && (shortage.Grid == null || shortage.Grid == args.Grid)
                && (shortage.Goods?.Contains(args.Good) ?? _market.InGroup(args.Good, shortage.Group)))
            {
                args.Modifiers.Add(new WFTradeModifier(shortage.Factor,
                    shortage.Grid == null ? "wf-trade-reason-boom" : "wf-trade-reason-short"));
            }
        }
    }

    /// <summary>A station short of a good, or a sector booming for it, also charges more to buy it.</summary>
    private void OnBuyQuote(WFTradeBuyQuoteEvent args)
    {
        var now = _timing.CurTime;
        foreach (var shortage in _active)
        {
            if (shortage.Ends > now
                && !shortage.Report
                && (shortage.Grid == null || shortage.Grid == args.Grid)
                && (shortage.Goods?.Contains(args.Good) ?? _market.InGroup(args.Good, shortage.Group)))
            {
                args.Modifiers.Add(new WFTradeModifier(shortage.Factor,
                    shortage.Grid == null ? "wf-trade-reason-boom" : "wf-trade-reason-short"));
            }
        }
    }

    /// <summary>Whether a station has any shortage running now.</summary>
    public bool IsShort(EntityUid grid)
    {
        return _active.Any(shortage => shortage.Grid == grid && shortage.Ends > _timing.CurTime);
    }

    /// <summary>Ends a station's shortages of some goods because a freighter brought them.</summary>
    public void Relieve(EntityUid grid, HashSet<string> goods)
    {
        _active.RemoveAll(shortage => shortage.Grid == grid
                                      && (shortage.Goods?.Overlaps(goods) ?? goods.Any(good => _market.InGroup(good, shortage.Group))));
    }

    /// <summary>Remembers that a freighter bound for a station never arrived.</summary>
    public void NoteLost(EntityUid grid)
    {
        _lost[grid] = _timing.CurTime;
    }

    /// <summary>Ends every report whose station has recovered.</summary>
    public void Review()
    {
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        _active.RemoveAll(shortage => shortage is { Report: true, Grid: { } grid }
                                      && !_market.Group(profile, shortage.Group).Any(good => _market.TryGetRow(grid, good, out var row) && row.ScarceSince != null));
    }

    /// <summary>Whether a good has been scarce at a station long enough to report.</summary>
    private bool Due(EntityUid grid, string good, WFTradeMarketPrototype profile)
    {
        return _market.TryGetRow(grid, good, out var row) && row.ScarceSince is { } since && _timing.CurTime - since >= profile.ShortageDelay;
    }

    /// <summary>Why a station is short of a good, as players are told it.</summary>
    private string Cause(EntityUid grid, string good, WFTradeMarketPrototype profile)
    {
        if (_sector.IsBlockaded(grid))
            return Loc.GetString("wf-trade-shortage-cause-blockade");

        if (_lost.TryGetValue(grid, out var lost) && _timing.CurTime - lost < LostMemory)
            return Loc.GetString("wf-trade-shortage-cause-lost");

        foreach (var id in _market.Role(grid)?.Industries ?? new List<ProtoId<WFTradeIndustryPrototype>>())
        {
            var industry = _proto.Index(id);
            if (industry.Input is { } input && industry.Outputs.Contains(good) && _market.Restocking(grid, input))
                return Loc.GetString("wf-trade-shortage-cause-input", ("input", _market.GoodName(input)));
        }

        return Loc.GetString("wf-trade-shortage-cause-supply");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _timing.CurTime;
        _active.RemoveAll(shortage => now >= shortage.Ends || shortage.Grid is { } grid && TerminatingOrDeleted(grid));

        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        _next ??= now + Minutes(profile.ShortageMinutes);
        if (now < _next)
            return;

        _next = now + Minutes(profile.ShortageMinutes);
        Start();
    }

    private TimeSpan Minutes(Vector2i range)
    {
        return TimeSpan.FromMinutes(_random.Next(range.X, Math.Max(range.X, range.Y) + 1));
    }

    /// <summary>
    /// Reports the worst shortage that fits a station buying its goods, or starts a random boom. A forced shortage with no
    /// real gap behind it runs anyway at its prototype's price.
    /// </summary>
    public WFTradeShortage? Start(ProtoId<WFTradeShortagePrototype>? forced = null)
    {
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var chutes = _market.Chutes();
        var options = new List<(WFTradeShortagePrototype Proto, List<EntityUid> Grids)>();
        foreach (var proto in _proto.EnumeratePrototypes<WFTradeShortagePrototype>())
        {
            if (forced != null && proto.ID != forced)
                continue;

            var goods = _market.Group(profile, proto.Group);
            var grids = chutes
                .Where(chute => (proto.Chutes.Count == 0 || MetaData(chute.Chute).EntityPrototype is { } chuteProto && proto.Chutes.Contains(chuteProto.ID))
                                && goods.Any(good => chute.Dispenser.Inventory.ContainsKey(good))
                                && !_active.Any(active => active.Grid == chute.Grid && active.Group == proto.Group))
                .Select(chute => chute.Grid)
                .Distinct()
                .ToList();

            // Only a real gap makes a station short; booms come from outside.
            if (!proto.SectorWide && forced == null)
                grids.RemoveAll(grid => !goods.Any(good => Due(grid, good, profile)));

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

        var goodsName = Loc.GetString(picked.Goods);
        var minutes = (int) picked.Duration.TotalMinutes;
        if (picked.SectorWide)
            return StartBoom(picked, goodsName, minutes);

        // The worst-off station first, and among equals any of them, judged only by goods scarce long enough to report.
        _random.Shuffle(candidates);
        var groupGoods = _market.Group(profile, picked.Group);
        var due = candidates
            .Select(grid => (Grid: grid, Goods: groupGoods.Where(good => Due(grid, good, profile)).ToList()))
            .Where(entry => entry.Goods.Count > 0)
            .ToList();
        if (due.Count > 0)
        {
            var (target, goods) = due.MinBy(entry => entry.Goods.Min(good => _market.KnownLevel(entry.Grid, good)));
            var worst = goods.MinBy(good => _market.KnownLevel(target, good))!;
            var level = WFTradeMarketSystem.LevelFactor(profile, _market.PricedLevel(target, worst));
            var cause = Cause(target, worst, profile);
            var report = new WFTradeShortage(target, picked.Group, level, _timing.CurTime + TimeSpan.FromHours(12), goodsName, null, cause);
            _active.Add(report);
            _market.Announce(Loc.GetString("wf-trade-shortage-report", ("station", Name(target)), ("goods", goodsName), ("cause", cause), ("factor", WFTradeMarketSystem.Shown(level))), target);
            return report;
        }

        // Only a forced shortage gets here: no gap behind it, so it runs at its prototype's price.
        var forcedTarget = candidates.MinBy(grid => groupGoods.Min(good => _market.KnownLevel(grid, good)));
        var factor = MathF.Round(_random.NextFloat(picked.MinFactor, picked.MaxFactor), 1);
        var shortage = new WFTradeShortage(forcedTarget, picked.Group, factor, _timing.CurTime + picked.Duration, goodsName);
        _active.Add(shortage);
        _market.Announce(Loc.GetString("wf-trade-shortage-start", ("station", Name(forcedTarget)), ("goods", goodsName), ("factor", WFTradeMarketSystem.Shown(factor)), ("minutes", minutes)), forcedTarget);
        return shortage;
    }

    private WFTradeShortage StartBoom(WFTradeShortagePrototype picked, string goodsName, int minutes)
    {
        var factor = MathF.Round(_random.NextFloat(picked.MinFactor, picked.MaxFactor), 1);
        var boom = new WFTradeShortage(null, picked.Group, factor, _timing.CurTime + picked.Duration, goodsName);
        _active.Add(boom);
        _market.Announce(Loc.GetString("wf-trade-boom-start", ("goods", goodsName), ("percent", (int) MathF.Round((factor - 1f) * 100f)), ("minutes", minutes)));
        return boom;
    }

    /// <summary>Starts a quiet shortage of exact goods at one station, such as those a lost freight run was bringing.</summary>
    public void StartLocal(EntityUid grid, HashSet<string> goods, float factor, TimeSpan duration, string name)
    {
        _active.Add(new WFTradeShortage(grid, string.Empty, factor, _timing.CurTime + duration, name, goods));
    }
}
