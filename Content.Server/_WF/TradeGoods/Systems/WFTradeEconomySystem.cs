using System.Linq;
using Content.Server._NF.Market.Systems;
using Content.Server._WF.Encounters;
using Content.Server._WF.TradeGoods.Components;
using Content.Server.GameTicking;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>A good across the whole sector: crates on hand, made and used per hour, and the stations scarce of it.</summary>
public readonly record struct WFTradeSectorGood(string Good, int Stock, int Owed, int Made, int Used, List<EntityUid> Short);

/// <summary>
/// The sector economy. Stations use goods up, their industries make more from what they hold, surplus goes into a sector
/// pool and the pool is owed out to whoever is short. It runs on rates, and only freighters turn what's owed into stock, so
/// losing one loses real supply. A blockaded station can't export or be reached and its imports go to hoarders.
/// </summary>
public sealed partial class WFTradeEconomySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;
    [Dependency] private WFTradeShortageSystem _shortages = default!;
    [Dependency] private MarketSystem _stock = default!;

    /// <summary>Freighter stops per hour each station is assumed to get while the sector settles at round start.</summary>
    private const float OpeningStops = 1f;

    /// <summary>The service patrols run on.</summary>
    public static readonly ProtoId<WFTradeServicePrototype> Patrols = "WFTradeServicePatrols";

    /// <summary>Crates of each good waiting to be owed to someone.</summary>
    private readonly Dictionary<string, float> _pool = new();

    private TimeSpan? _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<WFEncounterWeightEvent>(OnEncounterWeight);
    }

    /// <summary>Patrols fly less the less the sector has to arm them with.</summary>
    private void OnEncounterWeight(ref WFEncounterWeightEvent args)
    {
        if (args.Prototype.Category == WFEncounterCategory.Patrol)
            args.Weight *= ServiceState(Patrols);
    }

    /// <summary>
    /// How well the sector keeps a service running: 1 with its goods at their healthy level across the stations holding them,
    /// falling to its minimum with none left. An economy with no stations leaves it alone.
    /// </summary>
    public float ServiceState(ProtoId<WFTradeServicePrototype> id)
    {
        var service = _proto.Index(id);
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var lowest = 1f;
        foreach (var good in service.Goods)
        {
            var levels = new List<float>();
            foreach (var (grid, _) in Stations())
            {
                if (TryComp<WFTradeMarketComponent>(grid, out var market) && market.Ledger.TryGetValue(good, out var row))
                    levels.Add(row.Stock / profile.StockTarget);
            }

            if (levels.Count > 0)
                lowest = MathF.Min(lowest, levels.Average() / service.Healthy);
        }

        return MathF.Max(service.Minimum, lowest);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _pool.Clear();
        _next = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _timing.CurTime;
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        if (_next == null)
        {
            Open(profile.OpeningHours);
            _next = now + profile.EconomyInterval;
            return;
        }

        if (now < _next)
            return;

        _next = now + profile.EconomyInterval;
        Tick((float) profile.EconomyInterval.TotalHours);
    }

    /// <summary>The stations in the sector economy and their roles.</summary>
    public List<(EntityUid Grid, WFTradeStationPrototype Role)> Stations()
    {
        var stations = new List<(EntityUid, WFTradeStationPrototype)>();
        var query = EntityQueryEnumerator<WFTradeStationComponent>();
        while (query.MoveNext(out var grid, out _))
        {
            if (_market.Role(grid) is { } role)
                stations.Add((grid, role));
        }

        return stations;
    }

    /// <summary>The goods a station with a role keeps rows for from the start.</summary>
    private IEnumerable<string> Traded(EntityUid grid, WFTradeStationPrototype role)
    {
        foreach (var good in role.Uses)
        {
            yield return good;
        }

        foreach (var id in role.Industries)
        {
            var industry = _proto.Index(id);
            if (industry.Input is { } input)
                yield return input;

            foreach (var output in industry.Outputs)
            {
                yield return output;
            }
        }

        foreach (var good in _market.StockedGoods(grid))
        {
            yield return good;
        }

        foreach (var (_, dispenser, chuteGrid) in _market.Chutes())
        {
            if (chuteGrid != grid)
                continue;

            foreach (var good in dispenser.Inventory.Keys)
            {
                if (_proto.TryIndex<EntityPrototype>(good, out var proto) && proto.HasComp<WFTradeOriginComponent>(EntityManager.ComponentFactory))
                    yield return good;
            }
        }
    }

    /// <summary>The goods a station makes itself.</summary>
    private HashSet<string> Made(WFTradeStationPrototype role)
    {
        return role.Industries.SelectMany(id => _proto.Index(id).Outputs).Select(good => good.Id).ToHashSet();
    }

    /// <summary>
    /// Opens the ledgers and runs some hours of trade with freighters assumed, so a round opens with the sector's surpluses
    /// and gaps already in place and not a flat roll.
    /// </summary>
    public void Open(float hours)
    {
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var stations = Stations();
        foreach (var (grid, role) in stations)
        {
            foreach (var good in Traded(grid, role))
            {
                _market.Row(grid, good);
            }
        }

        var step = (float) profile.EconomyInterval.TotalHours;
        for (var elapsed = 0f; elapsed < hours; elapsed += step)
        {
            Tick(step, simulateFreight: true);
        }

        foreach (var (grid, _) in stations)
        {
            var market = Comp<WFTradeMarketComponent>(grid);
            foreach (var row in market.Ledger.Values)
            {
                row.Priced = row.Stock / profile.StockTarget;
                row.ScarceSince = null;
            }
        }

        var rows = stations.SelectMany(station => Comp<WFTradeMarketComponent>(station.Grid).Ledger.Values).ToList();
        if (rows.Count > 0)
            Log.Info($"Sector economy opened: {stations.Count} stations, {rows.Count} rows, mean level {rows.Average(row => row.Priced):F2}.");
    }

    /// <summary>One step of the sector: use, make, hoard, export, then share the pool out.</summary>
    public void Tick(float hours, bool simulateFreight = false)
    {
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var stations = Stations();
        var running = new Dictionary<string, int>();
        foreach (var (_, role) in stations)
        {
            foreach (var industry in role.Industries)
            {
                running[industry] = running.GetValueOrDefault(industry) + 1;
            }
        }

        var hoarded = stations.Select(station => station.Grid).Where(_sector.IsBlockaded).ToHashSet();
        foreach (var (grid, role) in stations)
        {
            var market = EnsureComp<WFTradeMarketComponent>(grid);
            foreach (var row in market.Ledger.Values)
            {
                row.Made = 0f;
                row.Used = 0f;
            }

            foreach (var good in role.Uses)
            {
                Use(grid, good, profile.UseRate * role.Population * hours, hours);
            }

            foreach (var id in role.Industries)
            {
                RunIndustry(grid, _proto.Index(id), running[id], profile, hours, !simulateFreight);
            }

            var made = Made(role);
            if (hoarded.Contains(grid))
                Hoard(market, made, profile, hours);
            else
                Export(market, profile, hours);
        }

        AddOutsideSupply(stations, profile, hours);
        ShareOutPool(stations, profile);
        Settle(profile, hours);

        if (simulateFreight)
        {
            foreach (var (grid, _) in stations)
            {
                if (!hoarded.Contains(grid) && _random.Prob(MathF.Min(OpeningStops * hours, 1f)))
                    DeliverAll(grid);
            }

            return;
        }

        NoteScarcity(stations, profile);
        _shortages.Review();
    }

    /// <summary>Marks when each good turned scarce at each station, which is what a reported shortage waits on.</summary>
    private void NoteScarcity(List<(EntityUid Grid, WFTradeStationPrototype Role)> stations, WFTradeMarketPrototype profile)
    {
        var now = _timing.CurTime;
        foreach (var (grid, _) in stations)
        {
            foreach (var row in Comp<WFTradeMarketComponent>(grid).Ledger.Values)
            {
                if (row.Stock < profile.StockTarget * profile.ScarceLevel)
                    row.ScarceSince ??= now;
                else if (row.Stock >= profile.StockTarget * profile.ShortageClearLevel)
                    row.ScarceSince = null;
            }
        }
    }

    private void DeliverAll(EntityUid grid)
    {
        foreach (var (good, row) in Comp<WFTradeMarketComponent>(grid).Ledger)
        {
            _market.Deliver(grid, good, row.Inbound);
        }
    }

    private void Use(EntityUid grid, string good, float amount, float hours)
    {
        var row = _market.Row(grid, good);
        var used = MathF.Min(row.Stock, amount);
        row.Stock -= used;
        row.Used += used / hours;
    }

    /// <summary>
    /// An industry makes its rate, split between the stations running it and scaled by its input, and more with materials to
    /// work from. A full output stops being made.
    /// </summary>
    private void RunIndustry(EntityUid grid, WFTradeIndustryPrototype industry, int stations, WFTradeMarketPrototype profile, float hours, bool useMaterials)
    {
        var input = industry.Input is { } good ? _market.Row(grid, good) : null;
        var share = MathF.Max(profile.MinOutput, MathF.Min(input == null ? 1f : input.Stock / profile.StockTarget, 1f));
        var amount = industry.Rate / stations * share * hours;
        if (useMaterials && industry.Materials.Count > 0)
        {
            var extra = amount * profile.MaterialBoost;
            amount += extra * _stock.WfDraw(grid, industry.Materials, extra * industry.Outputs.Count * profile.MaterialCrateValue);
        }

        var outputs = 0;
        foreach (var output in industry.Outputs)
        {
            var row = _market.Row(grid, output);
            if (row.Stock >= profile.VendorStock)
                continue;

            var made = MathF.Min(amount, profile.VendorStock - row.Stock);
            row.Stock += made;
            row.Made += made / hours;
            outputs++;
        }

        if (input != null)
            Use(grid, industry.Input!.Value, amount * outputs * profile.InputUse, hours);
    }

    /// <summary>Surplus goes into the sector's pool.</summary>
    private void Export(WFTradeMarketComponent market, WFTradeMarketPrototype profile, float hours)
    {
        foreach (var (good, row) in market.Ledger)
        {
            var surplus = row.Stock - profile.StockTarget;
            if (surplus <= 0f)
                continue;

            var exported = surplus * MathF.Min(profile.ExportRate * hours, 1f);
            row.Stock -= exported;
            _pool[good] = _pool.GetValueOrDefault(good) + exported;
        }
    }

    /// <summary>
    /// A blockaded station's imports go off the shelves: what's hoarded is owed back to it, so the first ship through brings
    /// relief. Most of the fall comes in the first half hour.
    /// </summary>
    private void Hoard(WFTradeMarketComponent market, HashSet<string> made, WFTradeMarketPrototype profile, float hours)
    {
        var keep = (float) Math.Pow(0.5, hours / Math.Max(profile.BlockadeHalfLife.TotalHours, 0.001));
        var floor = profile.BlockadeFloor * profile.StockTarget;
        foreach (var (good, row) in market.Ledger)
        {
            if (made.Contains(good) || row.Stock <= floor)
                continue;

            var kept = floor + (row.Stock - floor) * keep;
            row.Inbound += row.Stock - kept;
            row.Stock = kept;
        }
    }

    /// <summary>Goods nothing in the sector makes arrive from outside it, as long as someone is taking them.</summary>
    private void AddOutsideSupply(List<(EntityUid Grid, WFTradeStationPrototype Role)> stations, WFTradeMarketPrototype profile, float hours)
    {
        var made = stations.SelectMany(station => Made(station.Role)).ToHashSet();
        var wanted = stations.SelectMany(station => Comp<WFTradeMarketComponent>(station.Grid).Ledger.Keys).ToHashSet();
        foreach (var good in wanted)
        {
            if (!made.Contains(good) && !profile.ContrabandGoods.Contains(good))
                _pool[good] = MathF.Min(_pool.GetValueOrDefault(good) + profile.OutsideSupply * hours, profile.StockTarget);
        }
    }

    /// <summary>Owes each good's pool to the stations that don't make it, by how far each is under target.</summary>
    private void ShareOutPool(List<(EntityUid Grid, WFTradeStationPrototype Role)> stations, WFTradeMarketPrototype profile)
    {
        foreach (var (good, amount) in _pool.ToList())
        {
            var wants = new List<(WFTradeRow Row, float Need)>();
            var total = 0f;
            foreach (var (grid, role) in stations)
            {
                if (Made(role).Contains(good) || !Comp<WFTradeMarketComponent>(grid).Ledger.TryGetValue(good, out var row))
                    continue;

                var need = profile.StockTarget - row.Stock - row.Inbound;
                if (need <= 0f)
                    continue;

                wants.Add((row, need));
                total += need;
            }

            if (total <= 0f)
                continue;

            var given = MathF.Min(amount, total);
            foreach (var (row, need) in wants)
            {
                row.Inbound += given * need / total;
            }

            _pool[good] = amount - given;
        }
    }

    /// <summary>Stations outside the economy drift back to their target, as a market with nothing feeding it would.</summary>
    private void Settle(WFTradeMarketPrototype profile, float hours)
    {
        var keep = (float) Math.Pow(0.5, hours / Math.Max(profile.HalfLife.TotalHours, 0.001));
        var query = EntityQueryEnumerator<WFTradeMarketComponent>();
        while (query.MoveNext(out var grid, out var market))
        {
            if (_market.Role(grid) != null)
                continue;

            foreach (var row in market.Ledger.Values)
            {
                row.Stock = profile.StockTarget + (row.Stock - profile.StockTarget) * keep;
            }
        }
    }

    /// <summary>Every good the sector's stations trade, for the Market app and admins.</summary>
    public List<WFTradeSectorGood> Sector()
    {
        var goods = new Dictionary<string, (float Stock, float Owed, float Made, float Used, List<EntityUid> Short)>();
        foreach (var (grid, _) in Stations())
        {
            if (!TryComp<WFTradeMarketComponent>(grid, out var market))
                continue;

            foreach (var (good, row) in market.Ledger)
            {
                if (!goods.TryGetValue(good, out var entry))
                    entry = (0f, 0f, 0f, 0f, new List<EntityUid>());

                entry.Stock += row.Stock;
                entry.Owed += row.Inbound;
                entry.Made += row.Made;
                entry.Used += row.Used;
                if (_market.Restocking(grid, good))
                    entry.Short.Add(grid);

                goods[good] = entry;
            }
        }

        return goods.Select(entry => new WFTradeSectorGood(entry.Key, (int) entry.Value.Stock, (int) entry.Value.Owed,
            (int) MathF.Round(entry.Value.Made), (int) MathF.Round(entry.Value.Used), entry.Value.Short)).ToList();
    }
}
