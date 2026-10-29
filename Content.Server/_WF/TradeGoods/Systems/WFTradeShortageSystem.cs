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
/// or exactly <c>Goods</c> when set, such as the cargo a lost freight run was bringing.
/// </summary>
public sealed record WFTradeShortage(EntityUid? Grid, string Group, float Factor, TimeSpan Ends, LocId Name, HashSet<string>? Goods = null);

/// <summary>
/// Every so often a station runs short of a group of goods, or the whole sector booms for one, and pays more for it
/// for a while. Keeps prices moving when no campaign is running.
/// </summary>
public sealed class WFTradeShortageSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;

    private readonly List<WFTradeShortage> _active = new();
    private TimeSpan? _next;

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
        _next = null;
    }

    private void OnQuote(WFTradeQuoteEvent args)
    {
        var now = _timing.CurTime;
        foreach (var shortage in _active)
        {
            if (shortage.Ends > now
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
                && (shortage.Grid == null || shortage.Grid == args.Grid)
                && (shortage.Goods?.Contains(args.Good) ?? _market.InGroup(args.Good, shortage.Group)))
            {
                args.Modifiers.Add(new WFTradeModifier(shortage.Factor,
                    shortage.Grid == null ? "wf-trade-reason-boom" : "wf-trade-reason-short"));
            }
        }
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

    /// <summary>Starts a random shortage or boom that fits a station buying its goods.</summary>
    public WFTradeShortage? Start(ProtoId<WFTradeShortagePrototype>? forced = null)
    {
        var chutes = _market.Chutes();
        var options = new List<(WFTradeShortagePrototype Proto, List<EntityUid> Grids)>();
        foreach (var proto in _proto.EnumeratePrototypes<WFTradeShortagePrototype>())
        {
            if (forced != null && proto.ID != forced)
                continue;

            var goods = _market.Group(_proto.Index(WFTradeMarketSystem.DefaultProfile), proto.Group);
            var grids = chutes
                .Where(chute => (proto.Chutes.Count == 0 || MetaData(chute.Chute).EntityPrototype is { } chuteProto && proto.Chutes.Contains(chuteProto.ID))
                                && goods.Any(good => chute.Dispenser.Inventory.ContainsKey(good))
                                && !_active.Any(active => active.Grid == chute.Grid && active.Group == proto.Group))
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

        EntityUid? target = picked.SectorWide ? null : _random.Pick(candidates);
        var factor = MathF.Round(_random.NextFloat(picked.MinFactor, picked.MaxFactor), 1);
        var shortage = new WFTradeShortage(target, picked.Group, factor, _timing.CurTime + picked.Duration, picked.Goods);
        _active.Add(shortage);

        var goodsName = Loc.GetString(picked.Goods);
        var minutes = (int) picked.Duration.TotalMinutes;
        _market.Announce(target is { } grid
            ? Loc.GetString("wf-trade-shortage-start", ("station", Name(grid)), ("goods", goodsName), ("factor", WFTradeMarketSystem.Shown(factor)), ("minutes", minutes))
            : Loc.GetString("wf-trade-boom-start", ("goods", goodsName), ("percent", (int) MathF.Round((factor - 1f) * 100f)), ("minutes", minutes)),
            target);

        return shortage;
    }

    /// <summary>Starts a quiet shortage of exact goods at one station, such as those a lost freight run was bringing.</summary>
    public void StartLocal(EntityUid grid, HashSet<string> goods, float factor, TimeSpan duration, LocId name)
    {
        _active.Add(new WFTradeShortage(grid, string.Empty, factor, _timing.CurTime + duration, name, goods));
    }
}
