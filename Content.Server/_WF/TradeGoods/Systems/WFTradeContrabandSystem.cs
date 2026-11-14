using Content.Server._NF.BountyContracts;
using Content.Shared._NF.BountyContracts;
using Content.Shared.GameTicking;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Contraband at open chutes. Every sale pays, but customs flags some sellers: a criminal warrant goes on the bounty
/// board in their name and security hears who sold it where. A flagged seller isn't flagged again for a while.
/// </summary>
public sealed class WFTradeContrabandSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BountyContractSystem _board = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;

    /// <summary>Until when each flagged seller can't be flagged again.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _flagged = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _flagged.Clear());
    }

    private void OnSale(WFTradeSaleEvent args)
    {
        var profile = _market.Profile(args.Grid);
        if (!profile.ContrabandGoods.Contains(args.Good)
            || _market.IsHiddenMarket(args.Grid)
            || !_random.Prob(profile.ContrabandWarrantChance))
            return;

        Flag(args.Seller, args.Grid, args.Ship);
    }

    /// <summary>
    /// Flags a contraband seller, unless they were flagged recently: posts the warrant and tells security. Returns the
    /// warrant, or null if none was posted.
    /// </summary>
    public BountyContract? Flag(EntityUid seller, EntityUid station, EntityUid? ship)
    {
        var now = _timing.CurTime;
        if (_flagged.TryGetValue(seller, out var until) && now < until)
            return null;

        var profile = _market.Profile(station);
        _flagged[seller] = now + profile.ContrabandWarrantCooldown;

        var shipName = ship is { } shipUid && !TerminatingOrDeleted(shipUid) ? Name(shipUid) : null;
        var warrant = _board.TryPostSystemContract(BountyContractSystem.WarrantCollection, BountyContractCategory.Criminal, Name(seller),
            profile.ContrabandWarrant, Loc.GetString("wf-trade-contraband-warrant", ("station", Name(station))),
            shipName, Loc.GetString("wf-trade-contraband-customs"));

        _market.Radio(shipName == null
            ? Loc.GetString("wf-trade-contraband-flagged", ("seller", Name(seller)), ("station", Name(station)))
            : Loc.GetString("wf-trade-contraband-flagged-ship", ("seller", Name(seller)), ("station", Name(station)), ("ship", shipName)),
            profile.ContrabandAlertChannel);

        return warrant;
    }
}
