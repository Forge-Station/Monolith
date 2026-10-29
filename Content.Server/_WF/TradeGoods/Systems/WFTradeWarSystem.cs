using System.Linq;
using Content.Server._Mono.AlertLevel;
using Content.Server._NF.BountyContracts;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._Mono.Company;
using Content.Shared._NF.BountyContracts;
using Content.Shared._WF.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// War pricing at the navy chutes. While the war is hot a navy pays its own people faction credits on top for munitions
/// and ship parts, and pays the enemy's people more for war goods. A navy that posts warrants puts one on any of its
/// own people who sell to the enemy.
/// </summary>
public sealed class WFTradeWarSystem : EntitySystem
{
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WarLevelSystem _war = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private BountyContractSystem _board = default!;

    /// <summary>Sellers already warranted this round, so a run of sales posts one warrant.</summary>
    private readonly HashSet<EntityUid> _warranted = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTradeQuoteEvent>(OnQuote);
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _warranted.Clear();
    }

    /// <summary>The navy a chute belongs to, if any.</summary>
    private WFTradeNavy? NavyOf(WFTradeMarketPrototype profile, EntityUid chute)
    {
        return MetaData(chute).EntityPrototype is { } proto
            ? profile.Navies.FirstOrDefault(navy => navy.Chutes.Contains(proto.ID))
            : null;
    }

    private ProtoId<CompanyPrototype>? CompanyOf(EntityUid? mob)
    {
        return CompOrNull<CompanyComponent>(mob)?.CompanyName;
    }

    private void OnQuote(WFTradeQuoteEvent args)
    {
        var profile = _market.Profile(args.Grid);
        if (args.Seller is not { } seller
            || !_war.GetWarLevel(args.Chute)
            || NavyOf(profile, args.Chute) is not { } navy
            || CompanyOf(seller) is not { } company
            || !navy.Enemies.Contains(company)
            || !profile.WarGroups.Any(group => _market.InGroup(args.Good, group)))
            return;

        args.Modifiers.Add(new WFTradeModifier(profile.DefectorFactor, "wf-trade-reason-defector"));
    }

    private void OnSale(WFTradeSaleEvent args)
    {
        var profile = _market.Profile(args.Grid);
        if (!_war.GetWarLevel(args.Chute) || NavyOf(profile, args.Chute) is not { } navy || CompanyOf(args.Seller) is not { } company)
            return;

        if (navy.Companies.Contains(company))
        {
            if (navy.CreditEntity is not { } credit
                || profile.WarEffortCredits <= 0
                || !profile.WarEffortGroups.Any(group => _market.InGroup(args.Good, group))
                || !_market.WasHauled(args.Chute, args.Crate))
                return;

            _stack.SpawnMultiple(credit, profile.WarEffortCredits, Transform(args.Chute).Coordinates);
            _popup.PopupEntity(Loc.GetString("wf-trade-war-effort", ("credits", profile.WarEffortCredits), ("navy", Loc.GetString(navy.Name))),
                args.Chute, args.Seller);
            return;
        }

        if (!navy.Enemies.Contains(company)
            || !profile.WarGroups.Any(group => _market.InGroup(args.Good, group))
            || profile.Navies.FirstOrDefault(own => own.Companies.Contains(company)) is not { PostsDefectorWarrants: true } own
            || !_warranted.Add(args.Seller))
            return;

        _board.TryPostSystemContract(BountyContractSystem.WarrantCollection, BountyContractCategory.Criminal, Name(args.Seller),
            profile.DefectorWarrant, Loc.GetString("wf-trade-defector-warrant", ("navy", Loc.GetString(navy.Name))),
            args.Ship is { } ship ? Name(ship) : null, Loc.GetString(own.Name));
    }
}
