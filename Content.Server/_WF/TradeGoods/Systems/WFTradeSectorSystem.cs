using System.Linq;
using Content.Server._Mono.AlertLevel;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>A blockade attempt on a chute station's cell, and the ships that kept its lane busy.</summary>
public sealed class WFTradeBlockadeAttempt
{
    public TimeSpan Started;
    public TimeSpan Deadline;
    public EntityUid? Source;
    public readonly HashSet<EntityUid> Sellers = new();
}

/// <summary>
/// Price modifiers from the sector. Blockaded stations pay more for essentials, a station under a blockade attempt pays
/// relief rates, the navies pay war demand while the war is hot, and a raided station pays more for repairs. When
/// traffic breaks a blockade attempt, the ships that ran it are named and paid.
/// </summary>
public sealed class WFTradeSectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;
    [Dependency] private WFSectorActivitySystem _activity = default!;
    [Dependency] private WarLevelSystem _war = default!;
    [Dependency] private WFReaverCampaignSystem _reaver = default!;
    [Dependency] private WFEncounterRewardSystem _rewards = default!;

    /// <summary>The shortest time between two traffic stamps a station's deliveries make.</summary>
    private static readonly TimeSpan StampCooldown = TimeSpan.FromMinutes(5);

    /// <summary>Smallest sale that counts as traffic at its station.</summary>
    private const int StampValue = 2000;

    private readonly Dictionary<WFSectorCell, WFTradeBlockadeAttempt> _attempts = new();
    private readonly Dictionary<EntityUid, TimeSpan> _stamped = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTradeQuoteEvent>(OnQuote);
        SubscribeLocalEvent<WFTradeBuyQuoteEvent>(OnBuyQuote);
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<WFSectorCellContestedEvent>(OnContested);
        SubscribeLocalEvent<WFSectorContestResolvedEvent>(OnResolved, after: [typeof(WFReaverCampaignSystem)]);
        SubscribeLocalEvent<WarLevelChangedEvent>(OnWarLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _attempts.Clear();
        _stamped.Clear();
    }

    private void OnQuote(WFTradeQuoteEvent args)
    {
        var profile = _market.Profile(args.Grid);
        var good = args.Good;

        if (TryStationCell(args.Grid, out var map, out var cell))
        {
            if (BlockadingOwner(map, cell, args.Grid) && InAny(good, profile.BlockadeGroups))
                args.Modifiers.Add(new WFTradeModifier(profile.BlockadeFactor, "wf-trade-reason-blockade"));

            if (UnderAttempt(map, cell, args.Grid))
                args.Modifiers.Add(new WFTradeModifier(profile.ReliefFactor, "wf-trade-reason-relief"));
        }

        if (_war.GetWarLevel(args.Grid)
            && MetaData(args.Chute).EntityPrototype is { } chute
            && profile.WarDemandChutes.Contains(chute.ID)
            && InAny(good, profile.WarGroups))
            args.Modifiers.Add(new WFTradeModifier(profile.WarFactor, "wf-trade-reason-war"));

        if (_reaver.RecentlyRaided(args.Grid, profile.RaidWindow) && InAny(good, profile.RaidGroups))
            args.Modifiers.Add(new WFTradeModifier(profile.RaidFactor, "wf-trade-reason-raid"));
    }

    /// <summary>A blockaded or recently raided station charges more to buy the goods it is short of.</summary>
    private void OnBuyQuote(WFTradeBuyQuoteEvent args)
    {
        var profile = _market.Profile(args.Grid);
        if (TryStationCell(args.Grid, out var map, out var cell)
            && BlockadingOwner(map, cell, args.Grid)
            && InAny(args.Good, profile.BlockadeGroups))
            args.Modifiers.Add(new WFTradeModifier(profile.BlockadeFactor, "wf-trade-reason-blockade"));

        if (_reaver.RecentlyRaided(args.Grid, profile.RaidWindow) && InAny(args.Good, profile.RaidGroups))
            args.Modifiers.Add(new WFTradeModifier(profile.RaidFactor, "wf-trade-reason-raid"));
    }

    private bool InAny(string good, List<string> groups)
    {
        return groups.Any(group => _market.InGroup(good, group));
    }

    /// <summary>The sector cell a station grid is in, if territory is running on its map.</summary>
    private bool TryStationCell(EntityUid grid, out MapId map, out WFSectorCell cell)
    {
        map = Transform(grid).MapID;
        cell = default;
        if (!_territory.Enabled || map != _territory.SectorMap)
            return false;

        cell = _territory.GridCell(grid);
        return true;
    }

    /// <summary>Whether a station's cell is held by a faction that blockades it.</summary>
    public bool BlockadingOwner(MapId map, WFSectorCell cell, EntityUid station)
    {
        return _territory.Owner(map, cell) is { } owner
               && _proto.TryIndex(owner, out var faction)
               && faction.Blockades
               && !_activity.IsNeutralStation(owner, station);
    }

    /// <summary>Whether a blockading faction is contesting a station's unheld cell right now.</summary>
    public bool UnderAttempt(MapId map, WFSectorCell cell, EntityUid station)
    {
        return _territory.ContestOf(map, cell) is { Holder: null } contest
               && _proto.TryIndex(contest.Faction, out var faction)
               && faction.Blockades
               && !_activity.IsNeutralStation(contest.Faction, station);
    }

    private void OnContested(ref WFSectorCellContestedEvent args)
    {
        if (args.Holder != null || args.Map != _territory.SectorMap
            || _territory.ContestOf(args.Map, args.Cell) is not { } contest
            || !_proto.TryIndex(args.Faction, out var faction) || !faction.Blockades
            || ChuteStation(args.Map, args.Cell) is not { } station
            || _activity.IsNeutralStation(args.Faction, station))
            return;

        if (!_attempts.TryGetValue(args.Cell, out var attempt))
        {
            attempt = new WFTradeBlockadeAttempt();
            _attempts[args.Cell] = attempt;
        }

        attempt.Started = contest.Started;
        attempt.Deadline = contest.Deadline;
        attempt.Source = contest.Source;
    }

    private void OnResolved(ref WFSectorContestResolvedEvent args)
    {
        if (args.Map != _territory.SectorMap || ChuteStation(args.Map, args.Cell) is not { } station)
            return;

        if (args.Claimed)
        {
            _attempts.Remove(args.Cell);
            if (args.Holder == null && _proto.TryIndex(args.Faction, out var blockader) && blockader.Blockades
                && !_activity.IsNeutralStation(args.Faction, station))
            {
                var profile = _market.Profile(station);
                _market.Announce(Loc.GetString("wf-trade-blockade-landed",
                    ("station", Name(station)), ("factor", WFTradeMarketSystem.Shown(profile.BlockadeFactor))), station);
            }

            return;
        }

        // A contest called off, such as by its convoy arriving, was neither broken nor driven off.
        if (args.Holder != null || !_attempts.Remove(args.Cell, out var attempt) || args.Dropped)
            return;

        var now = _timing.CurTime;
        var sourceAlive = attempt.Source is not { } source || !TerminatingOrDeleted(source);
        if (!sourceAlive && now < attempt.Deadline)
        {
            _market.Announce(Loc.GetString("wf-trade-blockade-driven-off", ("station", Name(station))), station);
            return;
        }

        if (!sourceAlive || now < attempt.Deadline || _activity.IsQuiet(args.Map, args.Cell, attempt.Started))
            return;

        var ships = _activity.ActiveShips(args.Map, args.Cell).ToHashSet();
        ships.UnionWith(attempt.Sellers.Where(ship => !TerminatingOrDeleted(ship)));
        if (ships.Count == 0)
            return;

        var names = string.Join(", ", ships.Select(ship => Name(ship)));
        _market.Announce(Loc.GetString("wf-trade-blockade-broken", ("station", Name(station)), ("ships", names)), station);

        var bounty = _market.Profile(station).ReliefBounty;
        if (bounty > 0)
        {
            _rewards.PayCrews(Loc.GetString("wf-trade-relief-name", ("station", Name(station))),
                $"blockade relief at {ToPrettyString(station)}", bounty, ships, "running the blockade of");
        }
    }

    /// <summary>The station with a chute whose grid is centred in a cell, if any.</summary>
    private EntityUid? ChuteStation(MapId map, WFSectorCell cell)
    {
        foreach (var (_, _, grid) in _market.Chutes())
        {
            if (Transform(grid).MapID == map && _territory.GridCell(grid) == cell)
                return grid;
        }

        return null;
    }

    private void OnSale(WFTradeSaleEvent args)
    {
        if (!TryStationCell(args.Grid, out var map, out var cell))
            return;

        if (args.Ship is { } ship && _attempts.TryGetValue(cell, out var attempt))
            attempt.Sellers.Add(ship);

        // A real delivery to a station counts as traffic in its cell, so a docked seller keeps the lane open.
        if (args.Amount >= StampValue
            && _market.WasHauled(args.Chute, args.Crate)
            && (!_stamped.TryGetValue(args.Grid, out var last) || _timing.CurTime - last >= StampCooldown))
        {
            _stamped[args.Grid] = _timing.CurTime;
            _activity.Stamp(map, cell);
        }
    }

    private void OnWarLevelChanged(WarLevelChangedEvent args)
    {
        if (!args.WarLevel)
            return;

        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var grids = _market.Chutes()
            .Where(chute => MetaData(chute.Chute).EntityPrototype is { } proto && profile.WarDemandChutes.Contains(proto.ID))
            .Select(chute => chute.Grid)
            .Distinct()
            .ToList();

        // Off-the-books stations are only named to the underground companies.
        foreach (var hidden in new[] { false, true })
        {
            var stations = grids.Where(grid => _market.IsHiddenMarket(grid) == hidden).Select(grid => Name(grid)).ToList();
            if (stations.Count == 0)
                continue;

            var message = Loc.GetString("wf-trade-war-demand", ("stations", string.Join(", ", stations)), ("factor", WFTradeMarketSystem.Shown(profile.WarFactor)));
            if (hidden)
                _market.TellCompanies(profile.UndergroundCompanies, message);
            else
                _market.Radio(message);
        }
    }

    /// <summary>Live blockade attempts at chute stations and when they resolve, for the Market app.</summary>
    public IEnumerable<(EntityUid Station, TimeSpan Deadline)> Attempts()
    {
        foreach (var (cell, attempt) in _attempts)
        {
            if (ChuteStation(_territory.SectorMap, cell) is { } station)
                yield return (station, attempt.Deadline);
        }
    }

    /// <summary>Chute stations under a raid alert and when it lapses, for the Market app.</summary>
    public IEnumerable<(EntityUid Station, TimeSpan Ends)> Raided()
    {
        foreach (var grid in _market.Chutes().Select(chute => chute.Grid).Distinct())
        {
            var window = _market.Profile(grid).RaidWindow;
            if (_reaver.LastRaid(grid) is { } at && _timing.CurTime - at < window)
                yield return (grid, at + window);
        }
    }

    /// <summary>Chute stations held by a blockading faction now.</summary>
    public IEnumerable<EntityUid> Blockaded()
    {
        foreach (var grid in _market.Chutes().Select(chute => chute.Grid).Distinct())
        {
            if (TryStationCell(grid, out var map, out var cell) && BlockadingOwner(map, cell, grid))
                yield return grid;
        }
    }
}
