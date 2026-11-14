using System.Linq;
using Content.Server._NF.Market.Systems;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Feeds NPC freight into the market. Each run's crates carry a manifest seal naming the run and its destination. A
/// freighter docking at a stop unloads what that station is owed of what it carries, so stolen cargo cuts the delivery
/// and a run lost to attack writes off what its remaining stops were owed. Sealed crates pay less at lawful chutes, full
/// price at a fence, and a bonus at their own destination.
/// </summary>
public sealed class WFTradeFreightSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeShortageSystem _shortages = default!;
    [Dependency] private MarketSystem _stock = default!;
    [Dependency] private WFTradeSupplySystem _supply = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How much a station pays for a lost run's goods, and for how long.</summary>
    private const float LostFactor = 1.5f;

    private static readonly TimeSpan LostDuration = TimeSpan.FromMinutes(30);

    /// <summary>How much likelier a short station is to be a freight run's first stop.</summary>
    private const float ShortageDraw = 4f;

    /// <summary>How much likelier a station is to be a run's first stop, per full target of goods it is owed.</summary>
    private const float NeedDraw = 4f;

    /// <summary>Crates owed at a run's stops that make a manifest twice as likely.</summary>
    private const float ManifestDraw = 20f;

    /// <summary>Stops each run has reached.</summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _reached = new();

    /// <summary>What each simulated run has left to unload.</summary>
    private readonly Dictionary<EntityUid, Dictionary<string, int>> _holds = new();

    /// <summary>Crates each run has unloaded so far, so a lost run writes off only what it still carried.</summary>
    private readonly Dictionary<EntityUid, Dictionary<string, int>> _unloaded = new();

    /// <summary>The hulls each run loaded cargo onto.</summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _freighters = new();

    /// <summary>Trade cargo value per grid, worked out once per tick for every grid asked about in it.</summary>
    private readonly Dictionary<EntityUid, int> _values = new();

    private GameTick _valuesTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFEncounterCargoLoadedEvent>(OnCargoLoaded);
        SubscribeLocalEvent<WFEncounterStopReachedEvent>(OnStopReached);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnResolved);
        SubscribeLocalEvent<WFCargoValueQueryEvent>(OnCargoValue);
        SubscribeLocalEvent<WFEncounterStationWeightEvent>(OnStationWeight);
        SubscribeLocalEvent<WFEncounterManifestWeightEvent>(OnManifestWeight);
        SubscribeLocalEvent<WFTradeQuoteEvent>(OnQuote);
        SubscribeLocalEvent<WFTradeOriginComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _reached.Clear();
        _freighters.Clear();
        _holds.Clear();
        _unloaded.Clear();
    }

    private void OnCargoLoaded(ref WFEncounterCargoLoadedEvent args)
    {
        if (!TryComp<WFTradeOriginComponent>(args.Crate, out var origin)
            || !TryComp<WFEncounterComponent>(args.Encounter, out var encounter)
            || encounter.Stops.Count == 0)
            return;

        if (!_freighters.TryGetValue(args.Encounter, out var hulls))
            _freighters[args.Encounter] = hulls = new HashSet<EntityUid>();

        hulls.Add(args.Grid);
        origin.Run = Name(args.Grid);
        origin.Destination = _random.Pick(encounter.Stops);
    }

    private void OnQuote(WFTradeQuoteEvent args)
    {
        if (args.Crate is not { } crate || !TryComp<WFTradeOriginComponent>(crate, out var origin) || origin.Run == null)
            return;

        var profile = _market.Profile(args.Grid);
        if (origin.Destination == args.Grid)
        {
            if (_market.WasHauled(args.Chute, crate))
                args.Modifiers.Add(new WFTradeModifier(profile.SalvageBonus, "wf-trade-reason-salvage"));

            return;
        }

        if (MetaData(args.Chute).EntityPrototype is { } chute && profile.HotCargoChutes.Contains(chute.ID))
            return;

        args.Modifiers.Add(new WFTradeModifier(profile.HotCargoLawful, "wf-trade-reason-sealed"));
    }

    private void OnStopReached(ref WFEncounterStopReachedEvent args)
    {
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter)
            || encounter.Manifest is not { } manifestId
            || !_proto.TryIndex(manifestId, out var manifest))
            return;

        if (!_reached.TryGetValue(args.Encounter, out var reached))
            _reached[args.Encounter] = reached = new HashSet<EntityUid>();

        if (!reached.Add(args.Station))
            return;

        // A crewed hull carries what is still aboard, so cargo taken off it never arrives; a simulated one, its manifest.
        var aboard = new Dictionary<string, List<EntityUid>>();
        var run = Name(args.Grid);
        var query = EntityQueryEnumerator<WFTradeOriginComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var origin, out var xform, out var meta))
        {
            if (xform.GridUid == args.Grid && origin.Run == run && meta.EntityPrototype is { } proto)
                aboard.GetOrNew(proto.ID).Add(uid);
        }

        var simulated = aboard.Count == 0 && _freighters.GetValueOrDefault(args.Encounter)?.Contains(args.Grid) != true;
        var hold = simulated ? SimulatedHold(args.Encounter, manifest) : aboard.ToDictionary(crates => crates.Key, crates => crates.Value.Count);

        var station = args.Station;
        if (_sector.IsBlockaded(station))
            return;

        var supplied = new List<string>();
        var goods = new HashSet<string>();
        var profile = _market.Profile(station);
        var carrying = hold.Values.Any(count => count > 0);
        var stopsLeft = Math.Max(1, encounter.Stops.Count - reached.Count + 1);
        foreach (var (good, count) in hold.ToList())
        {
            // The hold is shared out along the route, so it can bring less the more of it is gone.
            var room = (int) MathF.Ceiling(count / (float) stopsLeft);
            var delivered = _market.Deliver(station, good, room * profile.FreightCrateStock);
            if (delivered <= 0f)
                continue;

            goods.Add(good);
            supplied.Add(_market.GoodName(good));
            var unloaded = Math.Min(count, (int) MathF.Ceiling(delivered / profile.FreightCrateStock));
            var unloadedSoFar = _unloaded.GetOrNew(args.Encounter);
            unloadedSoFar[good] = unloadedSoFar.GetValueOrDefault(good) + unloaded;
            if (simulated)
            {
                hold[good] = count - unloaded;
                continue;
            }

            foreach (var crate in aboard[good].Take(unloaded))
            {
                QueueDel(crate);
            }
        }

        // Nothing else refills a station, so stopping the freighters starves it of what they carry.
        if (carrying && _stock.WfRestock(station, profile.FreightRestock, manifest.Materials))
            supplied.Add(Loc.GetString("wf-trade-goods-materials"));

        _shortages.Relieve(args.Station, goods);
        _supply.NoteStop(args.Station);

        if (supplied.Count > 0)
        {
            _market.Announce(Loc.GetString("wf-trade-freight-supplied",
                ("ship", Name(args.Grid)), ("goods", string.Join(", ", supplied.Take(3))), ("station", Name(args.Station))), args.Station);
        }
    }

    /// <summary>What a simulated run has left to unload, starting from everything on its manifest.</summary>
    private Dictionary<string, int> SimulatedHold(EntityUid encounter, WFEncounterManifestPrototype manifest)
    {
        if (_holds.TryGetValue(encounter, out var hold))
            return hold;

        _holds[encounter] = hold = new Dictionary<string, int>();
        foreach (var crate in manifest.Crates)
        {
            hold[crate] = hold.GetValueOrDefault(crate) + 1;
        }

        return hold;
    }

    private void OnResolved(ref WFEncounterResolvedEvent args)
    {
        _reached.Remove(args.Encounter, out var reached);
        _freighters.Remove(args.Encounter, out var hulls);
        _holds.Remove(args.Encounter);
        _unloaded.Remove(args.Encounter, out var unloaded);

        // A fight decided with a freighter still flying for the run didn't lose it.
        if (args.Resolution == WFEncounterResolution.Decided
            && hulls?.Any(hull => !TerminatingOrDeleted(hull) && HasComp<WFEncounterGridComponent>(hull)) == true)
            return;

        if (args.Resolution is not (WFEncounterResolution.Destroyed or WFEncounterResolution.Decided)
            || !TryComp<WFEncounterComponent>(args.Encounter, out var encounter)
            || encounter.Manifest is not { } manifestId
            || !_proto.TryIndex(manifestId, out var manifest)
            || encounter.Stops.Count == 0)
            return;

        var goods = manifest.Crates.Select(crate => crate.Id).ToHashSet();
        var unreached = encounter.Stops.Count(stop => reached?.Contains(stop) != true && !TerminatingOrDeleted(stop));
        var crateStock = _market.Profile(null).FreightCrateStock;
        foreach (var stop in encounter.Stops)
        {
            if (reached?.Contains(stop) == true || TerminatingOrDeleted(stop))
                continue;

            // What it was still bringing is gone, and the sector has to make it again.
            foreach (var good in goods)
            {
                var left = Math.Max(0, manifest.Crates.Count(crate => crate.Id == good) - (unloaded?.GetValueOrDefault(good) ?? 0));
                _market.LoseInbound(stop, good, left * crateStock / (float) unreached);
            }

            _shortages.NoteLost(stop);
            if (!_market.Chutes().Any(chute => chute.Grid == stop && goods.Any(chute.Dispenser.Inventory.ContainsKey)))
                continue;

            _shortages.StartLocal(stop, goods, LostFactor, LostDuration, Loc.GetString(manifest.Name));
            _market.Announce(Loc.GetString("wf-trade-freight-lost", ("station", Name(stop)), ("run", Loc.GetString(manifest.Name))), stop);
        }
    }

    /// <summary>Freight runs under way whose radio call has gone out: the ship, its next stop and its cargo.</summary>
    public List<(string Ship, string Station, string Goods)> Inbound()
    {
        var inbound = new List<(string, string, string)>();
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (!encounter.Begun || encounter.Resolution != null || encounter.Announcement != null
                || encounter.Manifest is not { } manifestId || !_proto.TryIndex(manifestId, out var manifest))
                continue;

            var reached = _reached.GetValueOrDefault(uid);
            var next = encounter.Stops.FirstOrDefault(stop => reached?.Contains(stop) != true && !TerminatingOrDeleted(stop));
            var ship = encounter.Ships.TryGetValue(encounter.Announcer, out var announcer) ? announcer : encounter.Ships.Values.FirstOrDefault();
            if (!next.IsValid() || ship == null || TerminatingOrDeleted(ship.Grid))
                continue;

            inbound.Add((Name(ship.Grid), Name(next), Loc.GetString(manifest.Name)));
        }

        return inbound;
    }

    /// <summary>Freight runs are likelier to start their route at a station that is short of something or owed a lot.</summary>
    private void OnStationWeight(ref WFEncounterStationWeightEvent args)
    {
        if (args.Prototype.Manifests.Count == 0)
            return;

        var station = args.Station;
        if (_shortages.Active.Any(shortage => shortage.Grid == station))
            args.Weight *= ShortageDraw;

        args.Weight *= 1f + NeedDraw * _market.Owed(station) / _market.Profile(station).StockTarget;
    }

    /// <summary>Freight runs are likelier to carry what their stops are owed, the more so the more is waiting.</summary>
    private void OnManifestWeight(ref WFEncounterManifestWeightEvent args)
    {
        var goods = args.Manifest.Crates.Select(crate => crate.Id).ToHashSet();
        var owed = 0f;
        foreach (var stop in args.Stops)
        {
            owed += _market.Owed(stop, goods);
        }

        var draw = owed / ManifestDraw;
        args.Weight *= 1f + draw * draw;
    }

    /// <summary>Values a ship's trade cargo at the best base price any chute pays for each crate.</summary>
    private void OnCargoValue(ref WFCargoValueQueryEvent args)
    {
        if (_valuesTick != _timing.CurTick)
        {
            _valuesTick = _timing.CurTick;
            ValueAllCargo();
        }

        args.Value += _values.GetValueOrDefault(args.Grid);
    }

    /// <summary>Values the trade cargo on every grid in one pass, so raiders weighing several ships sweep once.</summary>
    private void ValueAllCargo()
    {
        _values.Clear();
        var chutes = _market.Chutes();
        var prices = new Dictionary<string, int>();
        var query = EntityQueryEnumerator<WFTradeOriginComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out _, out var xform, out var meta))
        {
            if (xform.GridUid is not { } grid || meta.EntityPrototype is not { } proto)
                continue;

            if (!prices.TryGetValue(proto.ID, out var price))
            {
                price = chutes.Select(chute => _market.BasePrice(chute.Dispenser, proto.ID) ?? 0).DefaultIfEmpty(0).Max();
                prices[proto.ID] = price;
            }

            _values[grid] = _values.GetValueOrDefault(grid) + price;
        }
    }

    private void OnExamined(Entity<WFTradeOriginComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Run is { } run)
        {
            var destination = ent.Comp.Destination is { } dest && !TerminatingOrDeleted(dest) ? Name(dest) : Loc.GetString("wf-trade-unknown-station");
            var profile = _market.Profile(null);
            args.PushMarkup(Loc.GetString("wf-trade-seal-examine",
                ("run", run), ("station", destination), ("percent", (int) MathF.Round(profile.HotCargoLawful * 100f))));
        }

        args.PushMarkup(Loc.GetString("wf-trade-thermal-examine"));
    }
}
