using System.Linq;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Feeds NPC freight into the market. Each run's crates carry a manifest seal naming the run and its destination. A
/// freighter docking at a stop supplies that station, and a run lost to attack leaves its remaining stops short. Sealed
/// crates pay less at lawful chutes, full price at a fence, and a bonus at their own destination.
/// </summary>
public sealed class WFTradeFreightSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeShortageSystem _shortages = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Most pressure one freighter's dock adds to one good.</summary>
    private const float SupplyCap = 0.3f;

    /// <summary>How much a station pays for a lost run's goods, and for how long.</summary>
    private const float LostFactor = 1.5f;

    private static readonly TimeSpan LostDuration = TimeSpan.FromMinutes(30);

    /// <summary>How much likelier a short station is to be a freight run's first stop.</summary>
    private const float ShortageDraw = 4f;

    /// <summary>Stops each run has reached.</summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _reached = new();

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
        SubscribeLocalEvent<WFTradeQuoteEvent>(OnQuote);
        SubscribeLocalEvent<WFTradeOriginComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _reached.Clear();
        _freighters.Clear();
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
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter) || encounter.Manifest is not { } manifestId)
            return;

        if (!_reached.TryGetValue(args.Encounter, out var reached))
            _reached[args.Encounter] = reached = new HashSet<EntityUid>();

        if (!reached.Add(args.Station))
            return;

        // A crewed hull supplies what is still aboard; a simulated one, what its manifest says it carries.
        var counts = new Dictionary<string, int>();
        var query = EntityQueryEnumerator<WFTradeOriginComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out _, out var xform, out var meta))
        {
            if (xform.GridUid == args.Grid && meta.EntityPrototype is { } proto)
                counts[proto.ID] = counts.GetValueOrDefault(proto.ID) + 1;
        }

        if (counts.Count == 0 && _proto.TryIndex(manifestId, out var manifest))
        {
            foreach (var crate in manifest.Crates)
            {
                counts[crate] = counts.GetValueOrDefault(crate) + 1;
            }
        }

        var station = args.Station;
        var bought = _market.Chutes().Where(chute => chute.Grid == station).ToList();
        var supplied = new List<string>();
        var profile = _market.Profile(args.Station);
        foreach (var (good, count) in counts)
        {
            if (!bought.Any(chute => chute.Dispenser.Inventory.ContainsKey(good)))
                continue;

            _market.AddPressure(args.Station, good, Math.Min(SupplyCap, count * profile.PerSaleOverrides.GetValueOrDefault(good, profile.PerSale)), supplied: true);
            supplied.Add(_market.GoodName(good));
        }

        if (supplied.Count > 0)
        {
            _market.Announce(Loc.GetString("wf-trade-freight-supplied",
                ("ship", Name(args.Grid)), ("goods", string.Join(", ", supplied.Take(3))), ("station", Name(args.Station))), args.Station);
        }
    }

    private void OnResolved(ref WFEncounterResolvedEvent args)
    {
        _reached.Remove(args.Encounter, out var reached);
        _freighters.Remove(args.Encounter, out var hulls);

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
        foreach (var stop in encounter.Stops)
        {
            if (reached?.Contains(stop) == true || TerminatingOrDeleted(stop)
                || !_market.Chutes().Any(chute => chute.Grid == stop && goods.Any(chute.Dispenser.Inventory.ContainsKey)))
                continue;

            _shortages.StartLocal(stop, goods, LostFactor, LostDuration, manifest.Name);
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

    /// <summary>Freight runs are likelier to start their route at a station that is short of something.</summary>
    private void OnStationWeight(ref WFEncounterStationWeightEvent args)
    {
        if (args.Prototype.Manifests.Count == 0)
            return;

        var station = args.Station;
        if (_shortages.Active.Any(shortage => shortage.Grid == station))
            args.Weight *= ShortageDraw;
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
