using System.Linq;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.CartridgeLoader;
using Content.Shared._WF.Contracts;
using Content.Shared.CartridgeLoader;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._WF.Contracts;

/// <summary>The board's window and the Contracts PDA app: what each viewer is shown, and their buttons.</summary>
public sealed partial class WFContractSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;

    public static readonly Robust.Shared.Prototypes.EntProtoId ContractsCartridge = "WFContractsCartridge";

    private void InitializeUi()
    {
        SubscribeLocalEvent<WFContractBoardComponent, BoundUIOpenedEvent>(OnBoardOpened);
        SubscribeLocalEvent<WFContractBoardComponent, WFContractRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<WFContractBoardComponent, WFContractAcceptMessage>(OnAccept);
        SubscribeLocalEvent<WFContractBoardComponent, WFContractAbandonMessage>(OnBoardAbandon);
        SubscribeLocalEvent<WFContractCartridgeComponent, CartridgeUiReadyEvent>(OnCartridgeReady);
        SubscribeLocalEvent<WFContractCartridgeComponent, CartridgeMessageEvent>(OnCartridgeMessage);
        SubscribeLocalEvent<WFPdaProgramsEvent>(OnPdaPrograms);
    }

    private void OnPdaPrograms(ref WFPdaProgramsEvent ev)
    {
        ev.Programs.Add(ContractsCartridge);
    }

    private void OnBoardOpened(Entity<WFContractBoardComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (HolderOf(args.Actor) is { } holder)
            PayOwed(holder);

        SendBoard(ent, args.Actor);
    }

    private void OnRefresh(Entity<WFContractBoardComponent> ent, ref WFContractRefreshMessage args)
    {
        SendBoard(ent, args.Actor);
    }

    private void OnAccept(Entity<WFContractBoardComponent> ent, ref WFContractAcceptMessage args)
    {
        if (TryAccept(args.Actor, args.Id, ent) is { } error)
            _popup.PopupEntity(error, ent, args.Actor);

        SendBoard(ent, args.Actor);
    }

    private void OnBoardAbandon(Entity<WFContractBoardComponent> ent, ref WFContractAbandonMessage args)
    {
        if (HolderOf(args.Actor) is { } holder)
            TryAbandon(holder, args.Id);

        SendBoard(ent, args.Actor);
    }

    private void OnCartridgeReady(Entity<WFContractCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        var holder = Transform(args.Loader).ParentUid;
        if (HolderOf(holder) is { } who)
            PayOwed(who);

        _cartridgeLoader.UpdateCartridgeUiState(args.Loader, new WFContractCartridgeUiState(State(holder)));
    }

    private void OnCartridgeMessage(Entity<WFContractCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not WFContractCartridgeAbandonMessage abandon)
            return;

        var loader = GetEntity(args.LoaderUid);
        // Only the PDA's carrier may abandon from it.
        if (Transform(loader).ParentUid != args.Actor || HolderOf(args.Actor) is not { } holder)
            return;

        TryAbandon(holder, abandon.Id);
        _cartridgeLoader.UpdateCartridgeUiState(loader, new WFContractCartridgeUiState(State(args.Actor)));
    }

    private void SendBoard(EntityUid board, EntityUid actor, Dictionary<string, EntityUid>? homes = null)
    {
        var tag = CompOrNull<WFContractBoardComponent>(board)?.Board ?? WFContractBoards.Central;
        _ui.ServerSendUiMessage(board, WFContractBoardUiKey.Key, new WFContractBoardStateMessage(State(actor, tag, homes)), actor);
    }

    /// <summary>Sends every open board and Contracts app its viewer's state.</summary>
    private void PushAll()
    {
        Dictionary<string, EntityUid>? homes = null;
        var boards = EntityQueryEnumerator<WFContractBoardComponent>();
        while (boards.MoveNext(out var board, out _))
        {
            foreach (var actor in _ui.GetActors(board, WFContractBoardUiKey.Key).ToList())
            {
                SendBoard(board, actor, homes ??= ContractorHomes());
            }
        }

        var cartridges = EntityQueryEnumerator<WFContractCartridgeComponent, CartridgeComponent>();
        while (cartridges.MoveNext(out var uid, out _, out var cartridge))
        {
            if (cartridge.LoaderUid is not { } loader || !TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
                || loaderComp.ActiveProgram != uid || !_ui.IsUiOpen(loader, loaderComp.UiKey))
                continue;

            _cartridgeLoader.UpdateCartridgeUiState(loader,
                new WFContractCartridgeUiState(State(Transform(loader).ParentUid, homes: homes ??= ContractorHomes())), loader: loaderComp);
        }
    }

    /// <summary>A viewer's board state: offers (one board's, or all), crew contracts, history and standing.</summary>
    public WFContractBoardState State(EntityUid viewer, string? board = null, Dictionary<string, EntityUid>? homes = null)
    {
        var holder = HolderOf(viewer);
        var state = new WFContractBoardState { CanHold = holder != null };
        var crew = holder is { } h ? CrewSlots(h.User) : ((int, int, int)?) null;

        foreach (var contract in _contracts.Values.Where(c => c.Status == WFContractStatus.Offered).OrderBy(c => c.Id))
        {
            if (board != null && _proto.Index(_proto.Index(contract.Prototype).Contractor).Board != board
                || !OfferedTo(contract, holder?.User))
                continue;

            var view = View(contract);
            view.Locked = Locked(holder, contract, viewer, crew);
            view.Share = Share(contract, crew?.Item1 ?? 1);
            state.Offers.Add(view);
        }

        if (holder is not { } who)
        {
            state.MaxActive = SlotsFor(1);
            state.NextSlotAt = NextSlotAt(1);
            return state;
        }

        state.CrewSize = CrewSize(who.User);
        state.MaxActive = SlotsFor(state.CrewSize);
        state.NextSlotAt = NextSlotAt(state.CrewSize);
        foreach (var contract in ActiveForCrew(who.User).OrderBy(c => c.Deadline))
        {
            var view = View(contract);
            view.Share = Share(contract, state.CrewSize);
            view.Mine = contract.User == who.User && contract.Character == who.Character;
            if (!view.Mine)
                view.Holder = contract.Character;
            state.Active.Add(view);
        }
        if (_history.TryGetValue(who, out var history))
            state.History.AddRange(history.Select(View));

        state.Owed = _owed.GetValueOrDefault(who);
        homes ??= ContractorHomes();
        foreach (var contractor in _proto.EnumeratePrototypes<WFContractorPrototype>().OrderBy(c => c.Order))
        {
            var points = Standing(who, contractor.ID);
            var rank = RankOf(points);
            state.Standing.Add(new WFContractStandingView(Loc.GetString(contractor.Name), Loc.GetString(contractor.Description),
                contractor.Color, Loc.GetString(RankName(rank)), points,
                rank + 1 < RankThresholds.Length ? RankThresholds[rank + 1] : null, homes.ContainsKey(contractor.ID)));
        }

        return state;
    }

    /// <summary>A contract as the UI shows it.</summary>
    public WFContractView View(WFContract contract)
    {
        var proto = _proto.Index(contract.Prototype);
        var contractor = _proto.Index(proto.Contractor);
        var view = new WFContractView
        {
            Id = contract.Id,
            Title = Title(contract),
            Briefing = Loc.GetString(proto.Briefing, Args(contract)),
            Contractor = Loc.GetString(contractor.Name),
            ContractorColor = contractor.Color,
            Category = proto.Category,
            Tier = proto.Tier,
            Reward = contract.Reward,
            Deposit = contract.Deposit,
            Status = contract.Status,
            Duration = contract.Duration,
            Deadline = contract.Status is WFContractStatus.Offered or WFContractStatus.Active ? contract.Deadline : null,
            MinCrew = proto.MinCrew,
            Employer = Loc.GetString("wf-contract-employer", ("contractor", Loc.GetString(contractor.Name)),
                ("employer", Loc.GetString(contractor.Employer))),
            Requires = proto.Requires.Select(r => Loc.GetString(r.Name)).ToList(),
        };

        if (proto.CrewBonus > 0)
            view.CrewBonus = Loc.GetString("wf-contract-crew-bonus", ("percent", (int) Math.Round(proto.CrewBonus * 100)),
                ("past", Math.Max(1, proto.MinCrew)), ("most", MaxBonusCrew));

        foreach (var (rival, cost) in proto.Harms)
        {
            if (_proto.TryIndex(rival, out var rivalProto))
                view.Harms.Add(Loc.GetString("wf-contract-harms", ("contractor", Loc.GetString(rivalProto.Name)), ("points", cost)));
        }

        if (proto.Credits > 0 && proto.CreditEntity is { } creditEntity && _proto.TryIndex(creditEntity, out var credit))
            view.Credits = Loc.GetString("wf-contract-credits", ("amount", proto.Credits), ("credit", credit.Name));

        var active = contract.Status == WFContractStatus.Active;
        switch (proto.Objective)
        {
            case WFContractCourier:
                view.Site = WFContractSite.Station;
                view.Location = Loc.GetString("wf-contract-location-courier",
                    ("origin", contract.Origin is { } origin ? StationName(origin) : Loc.GetString("wf-contract-board-name")),
                    ("destination", StationName(contract.Destination)), ("km", Km(contract.Distance)));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-courier",
                    ("done", contract.Delivered), ("count", contract.Count), ("destination", StationName(contract.Destination))),
                    contract.Delivered >= contract.Count));
                if (active && contract.Origin is { } pickup
                    && contract.Items.FirstOrDefault(i => !TerminatingOrDeleted(i) && OnStation(i, pickup)) is { Valid: true } waiting)
                    view.Coordinates = _transform.GetWorldPosition(waiting);
                break;
            case WFContractSupply supply:
            {
                view.Site = WFContractSite.Station;
                view.Location = Loc.GetString("wf-contract-location-supply", ("destination", StationName(contract.Destination)),
                    ("km", Km(contract.Distance)));
                var crate = contract.Items.FirstOrDefault();
                var have = active && crate.Valid && !TerminatingOrDeleted(crate) ? Packed(contract, crate) : null;
                for (var i = 0; i < contract.Goods.Count; i++)
                {
                    var (line, count) = contract.Goods[i];
                    var packed = have?[i] ?? 0;
                    view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-goods",
                        ("name", GoodsName(supply.Goods[line])), ("have", Math.Min(packed, count)), ("count", count),
                        ("active", active)), packed >= count));
                }

                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-crate",
                    ("destination", StationName(contract.Destination))), false));
                break;
            }
            case WFContractBounty:
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-bounty", ("home", StationName(contract.Home)),
                    ("km", Km(contract.Distance)), ("active", active));
                if (active && TargetPosition(contract) is { } target)
                    view.Coordinates = target;
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-bounty",
                    ("target", TargetName(contract))), false));
                break;
            case WFContractRecovery recovery:
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-recovery", ("home", StationName(contract.Home)),
                    ("km", Km(contract.Distance)), ("destination", StationName(contract.Destination)), ("active", active));
                if (active && contract.Site is { } site)
                    view.Coordinates = site.Position;
                var item = _proto.Index(recovery.Item).Name;
                var carried = active && contract.Items.FirstOrDefault() is { Valid: true } recovered && !TerminatingOrDeleted(recovered)
                              && contract.WreckGrid is { } wreck && Transform(recovered).GridUid != wreck;
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-recover", ("item", item)), carried));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-return", ("item", item),
                    ("destination", StationName(contract.Destination))), false));
                break;
            case WFContractTow tow:
            {
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-tow", ("home", StationName(contract.Home)),
                    ("km", Km(contract.Distance)), ("range", (int) tow.Range), ("active", active));
                if (active && contract.WreckGrid is { } hull && !TerminatingOrDeleted(hull))
                    view.Coordinates = _transform.GetWorldPosition(hull);
                else if (active && contract.Site is { } towSite)
                    view.Coordinates = towSite.Position;
                var gap = active ? TowGap(contract) : null;
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-tow",
                    ("destination", StationName(contract.Destination)), ("range", (int) tow.Range),
                    ("gap", gap is { } metres ? (int) metres : -1)), gap is { } close && close <= tow.Range));
                break;
            }
            case WFContractSurvey:
            {
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-survey", ("home", StationName(contract.Home)),
                    ("km", Km(contract.Distance)), ("active", active));
                if (active && contract.Site is { } surveySite)
                    view.Coordinates = surveySite.Position;
                var scanned = contract.Scanned.Count;
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-survey",
                    ("done", scanned), ("count", contract.Count)), active && scanned >= contract.Count));
                break;
            }
            case WFContractEscort:
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-escort", ("home", StationName(contract.Home)),
                    ("destination", StationName(contract.Destination)), ("km", Km(contract.Distance)), ("active", active));
                if (active && TargetPosition(contract) is { } escorted)
                    view.Coordinates = escorted;
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-escort-meet",
                    ("target", TargetName(contract))), active && EscortBegun(contract)));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-escort",
                    ("target", TargetName(contract)), ("destination", StationName(contract.Destination))), false));
                break;
            default:
                ViewOperation(contract, proto.Objective, view, active);
                break;
        }

        return view;
    }

    /// <summary>What a crew of a size would be paid for a contract and each one's share, or null for a lone contractor on solo work.</summary>
    private string? Share(WFContract contract, int crewSize)
    {
        var proto = _proto.Index(contract.Prototype);
        if (crewSize <= 1 && proto.MinCrew <= 1)
            return null;

        var size = Math.Max(crewSize, proto.MinCrew);
        var pool = CrewPool(contract.Reward, proto, size);
        return Loc.GetString("wf-contract-share", ("size", size), ("pool", pool), ("each", pool / size));
    }

    public string Title(WFContract contract)
    {
        return Loc.GetString(_proto.Index(contract.Prototype).Title, Args(contract));
    }

    /// <summary>The arguments every title and briefing takes.</summary>
    private (string, object)[] Args(WFContract contract)
    {
        var proto = _proto.Index(contract.Prototype);
        var goods = proto.Objective is WFContractSupply supply
            ? string.Join(", ", contract.Goods.Select(g => Loc.GetString("wf-contract-goods-entry", ("count", g.Count),
                ("name", GoodsName(supply.Goods[g.Line])))))
            : string.Empty;
        return
        [
            ("contractor", Loc.GetString(_proto.Index(proto.Contractor).Name)),
            ("home", StationName(contract.Home)),
            ("origin", contract.Origin is { } origin ? StationName(origin) : Loc.GetString("wf-contract-board-name")),
            ("destination", StationName(contract.Destination)),
            ("count", contract.Count),
            ("goods", goods),
            ("target", TargetName(contract)),
            ("km", Km(contract.Distance)),
        ];
    }

    private static string Km(float metres) => (metres / 1000f).ToString("0.0");

    /// <summary>The bounty's target: the live encounter's name, else the encounter's own with its designation left blank.</summary>
    private string TargetName(WFContract contract)
    {
        if (contract.EncounterEntity is { } encounter && !TerminatingOrDeleted(encounter))
            return Name(encounter);

        return contract.Encounter is { } id && _proto.TryIndex(id, out var proto)
            ? Loc.GetString(proto.Name, ("designation", Loc.GetString("wf-contract-designation-unknown")))
            : string.Empty;
    }

    /// <summary>Whether an escort's ships have met their escort and set off.</summary>
    private bool EscortBegun(WFContract contract)
    {
        return contract.EncounterEntity is { } encounter
               && TryComp<Encounters.Components.WFEncounterComponent>(encounter, out var comp) && comp.Begun;
    }

    /// <summary>Where a bounty's first ship is now, else where it was placed.</summary>
    private System.Numerics.Vector2? TargetPosition(WFContract contract)
    {
        if (contract.EncounterEntity is { } encounter
            && TryComp<Encounters.Components.WFEncounterComponent>(encounter, out var comp))
        {
            foreach (var ship in comp.Ships.Values)
            {
                if (!TerminatingOrDeleted(ship.Grid))
                    return _transform.GetWorldPosition(ship.Grid);
            }
        }

        return contract.Site?.Position;
    }
}
