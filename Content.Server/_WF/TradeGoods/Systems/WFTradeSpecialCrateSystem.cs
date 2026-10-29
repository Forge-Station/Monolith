using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.TradeGoods.Components;
using Content.Server.GameTicking;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Priority consignments. A consignment appears at one station bound for another, pays well if delivered on time, less
/// if late, and is announced when a ship takes it. One that is never delivered is recalled a window after its deadline.
/// </summary>
public sealed class WFTradeSpecialCrateSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _nextCheck;
    private TimeSpan? _nextConsignment;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFConsignmentComponent, ExaminedEvent>(OnConsignmentExamined);
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _nextConsignment = null;
    }

    private WFTradeMarketPrototype Profile => _proto.Index(WFTradeMarketSystem.DefaultProfile);

    /// <summary>Whether a chute should price a crate here instead of from its own table.</summary>
    public bool IsSpecial(EntityUid crate)
    {
        return HasComp<WFConsignmentComponent>(crate);
    }

    /// <summary>What a chute pays for a special crate now, or null if it won't take it.</summary>
    public int? Price(EntityUid chute, EntityUid crate)
    {
        if (!TryComp<WFConsignmentComponent>(crate, out var consignment))
            return null;

        return consignment.Destination == Transform(chute).GridUid ? ConsignmentPayout(consignment) : consignment.Elsewhere;
    }

    /// <summary>What a consignment pays at its destination now: on-time bonus before the deadline, late penalty after.</summary>
    public int ConsignmentPayout(WFConsignmentComponent consignment)
    {
        return Math.Max(0, consignment.Value
                           + (_timing.CurTime <= consignment.Deadline ? consignment.OnTimeBonus : -consignment.LatePenalty));
    }

    private void OnSale(WFTradeSaleEvent args)
    {
        if (!TryComp<WFConsignmentComponent>(args.Crate, out var consignment)
            || consignment.Destination != args.Grid
            || _timing.CurTime > consignment.Deadline)
            return;

        var ship = args.Ship is { } shipUid && !TerminatingOrDeleted(shipUid) ? Name(shipUid) : Name(args.Seller);
        _market.Announce(Loc.GetString("wf-trade-consignment-delivered", ("ship", ship), ("station", Name(args.Grid))), args.Grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        UpdateConsignments();
    }

    private void UpdateConsignments()
    {
        var now = _timing.CurTime;
        var profile = Profile;
        var query = EntityQueryEnumerator<WFConsignmentComponent, WFTradeOriginComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var consignment, out var origin, out var xform))
        {
            if (consignment.Announced is { } announced && TerminatingOrDeleted(announced))
                consignment.Announced = null;

            if (consignment.Destination is not { } destination || TerminatingOrDeleted(destination))
            {
                Recall(uid, xform, consignment, Loc.GetString("wf-trade-consignment-cancelled"));
                continue;
            }

            // Never picked up: withdrawn when its time runs out. Carried off: recalled a window after it.
            if (xform.GridUid == consignment.Origin && now > consignment.Deadline
                || now > consignment.Deadline + consignment.Window)
            {
                Recall(uid, xform, consignment, Loc.GetString("wf-trade-consignment-lapsed", ("station", Name(destination))));
                continue;
            }

            if (origin.Carrier is { } carrier && carrier != consignment.Announced && xform.GridUid == carrier)
            {
                consignment.Announced = carrier;
                _market.Announce(Loc.GetString("wf-trade-consignment-taken", ("ship", Name(carrier)), ("station", Name(destination))),
                    destination, consignment.Origin);
            }
        }

        _nextConsignment ??= now + TimeSpan.FromMinutes(_random.Next(profile.ConsignmentMinutes.X, Math.Max(profile.ConsignmentMinutes.X, profile.ConsignmentMinutes.Y) + 1));
        if (now < _nextConsignment)
            return;

        _nextConsignment = now + TimeSpan.FromMinutes(_random.Next(profile.ConsignmentMinutes.X, Math.Max(profile.ConsignmentMinutes.X, profile.ConsignmentMinutes.Y) + 1));
        SpawnConsignment();
    }

    /// <summary>Deletes a consignment, telling the crew of the ship it was aboard, if any.</summary>
    private void Recall(EntityUid uid, TransformComponent xform, WFConsignmentComponent consignment, string message)
    {
        if (xform.GridUid is { } grid && grid != consignment.Origin)
            _market.TellAboard([grid], message);

        QueueDel(uid);
    }

    /// <summary>
    /// Puts a priority consignment at one station's chute for another station at least the haul distance away. Wandering
    /// merchants are never an end, since they leave, and nor are hidden markets: a public crate or alert would name them,
    /// and the fence takes only sealed freight.
    /// </summary>
    public EntityUid? SpawnConsignment()
    {
        var profile = Profile;
        if (profile.Consignments.Count == 0)
            return null;

        var blockaded = _sector.Blockaded().ToHashSet();
        var all = _market.Chutes();
        var hidden = all.Where(chute => _market.IsHiddenChute(chute.Chute)).Select(chute => chute.Grid).ToHashSet();
        var chutes = all
            .Where(chute => !blockaded.Contains(chute.Grid)
                            && !hidden.Contains(chute.Grid)
                            && !HasComp<WFEncounterGridComponent>(chute.Grid))
            .ToList();
        var taken = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WFConsignmentComponent>();
        while (query.MoveNext(out var existing))
        {
            if (existing.Destination is { } dest)
                taken.Add(dest);
        }

        var destinations = chutes.Select(chute => chute.Grid).Distinct().Where(grid => !taken.Contains(grid)).ToList();
        _random.Shuffle(destinations);
        foreach (var destination in destinations)
        {
            var haul = _market.Profile(destination).MinHaulDistance;
            var ends = chutes.Where(chute => chute.Grid == destination).Select(chute => _transform.GetWorldPosition(chute.Chute)).ToList();
            var origins = chutes
                .Where(chute => chute.Grid != destination
                                && ends.All(end => (_transform.GetWorldPosition(chute.Chute) - end).Length() >= haul))
                .ToList();
            if (origins.Count == 0)
                continue;

            var (chute, _, originGrid) = _random.Pick(origins);
            return Post(profile, chute, originGrid, destination);
        }

        return null;
    }

    private EntityUid Post(WFTradeMarketPrototype profile, EntityUid chute, EntityUid originGrid, EntityUid destination)
    {
        var total = profile.Consignments.Values.Sum();
        var roll = _random.NextFloat() * total;
        var picked = profile.Consignments.Keys.Last();
        foreach (var (proto, weight) in profile.Consignments)
        {
            roll -= weight;
            if (roll <= 0f)
            {
                picked = proto;
                break;
            }
        }

        var crate = Spawn(picked, Transform(chute).Coordinates);
        var consignment = EnsureComp<WFConsignmentComponent>(crate);
        consignment.Origin = originGrid;
        consignment.Destination = destination;
        consignment.Deadline = _timing.CurTime + consignment.Window;

        _market.Announce(Loc.GetString("wf-trade-consignment-start",
            ("station", Name(destination)), ("origin", Name(originGrid)),
            ("value", consignment.Value + consignment.OnTimeBonus), ("minutes", (int) consignment.Window.TotalMinutes)),
            destination, originGrid);

        return crate;
    }

    private void OnConsignmentExamined(Entity<WFConsignmentComponent> ent, ref ExaminedEvent args)
    {
        var minutes = Math.Max(0, (int) Math.Ceiling((ent.Comp.Deadline - _timing.CurTime).TotalMinutes));
        args.PushMarkup(Loc.GetString("wf-trade-consignment-examine",
            ("station", StationLabel(ent.Comp.Destination)),
            ("value", ent.Comp.Value + ent.Comp.OnTimeBonus),
            ("minutes", minutes),
            ("elsewhere", ent.Comp.Elsewhere)));
    }

    /// <summary>A station's name, or a placeholder once it is gone.</summary>
    private string StationLabel(EntityUid? grid)
    {
        return grid is { } uid && !TerminatingOrDeleted(uid) ? Name(uid) : Loc.GetString("wf-trade-unknown-station");
    }

    /// <summary>Open consignments for the Market app: where they go, where they wait, who carries them and what they pay.</summary>
    public IEnumerable<(EntityUid Destination, string Origin, string? Carrier, TimeSpan Deadline, int Value)> Consignments()
    {
        var query = EntityQueryEnumerator<WFConsignmentComponent>();
        while (query.MoveNext(out var consignment))
        {
            if (consignment.Destination is { } dest && !TerminatingOrDeleted(dest))
            {
                yield return (dest, StationLabel(consignment.Origin),
                    consignment.Announced is { } ship && !TerminatingOrDeleted(ship) ? Name(ship) : null,
                    consignment.Deadline, consignment.Value + consignment.OnTimeBonus);
            }
        }
    }
}
