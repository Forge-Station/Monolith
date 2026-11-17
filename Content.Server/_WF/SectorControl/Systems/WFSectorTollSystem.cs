using Content.Server._Mono.Company;
using Content.Server._NF.Bank;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>
/// A foreign crewed ship that stays in a held hex pays the holding company once. Its own ships pay nothing.
/// The money leaves one crew member's account and lands in the company bank.
/// </summary>
public sealed class WFSectorTollSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private CompanyManager _companies = default!;
    [Dependency] private ServerGameTicker _ticker = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFSectorActivitySystem _activity = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    /// <summary>Spesos taken from the ship and paid to the holder, once per entry.</summary>
    public const int Toll = 500;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private readonly Dictionary<EntityUid, TollState> _ships = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _ships.Clear();
        _nextCheck = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_territory.Enabled || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        Check();
    }

    /// <summary>Charges ships that have sat in someone else's hex long enough, once per stay.</summary>
    public void Check()
    {
        var now = _timing.CurTime;
        var map = _territory.SectorMap;
        var ships = _activity.CrewedShips(map);
        var gone = new List<EntityUid>();
        foreach (var (grid, tracked) in _ships)
        {
            if (ships.Contains(grid))
            {
                tracked.LastSeen = now;
                continue;
            }

            if (TerminatingOrDeleted(grid) || now - tracked.LastSeen > Cooldown)
                gone.Add(grid);
        }

        foreach (var grid in gone)
        {
            _ships.Remove(grid);
        }

        foreach (var grid in ships)
        {
            var cell = _territory.CellOf(_transform.GetMapCoordinates(grid));
            var owner = _territory.Owner(map, cell);
            if (owner is { } holder && _activity.IsNeutralTo(holder, grid))
                owner = null;

            if (!_ships.TryGetValue(grid, out var state))
            {
                state = new TollState { Since = now, LastSeen = now };
                _ships[grid] = state;
            }

            if (owner != state.Current || cell != state.Cell)
            {
                state.Current = owner;
                state.Cell = cell;
                state.Since = now;
                state.Charged = false;
            }

            if (state.Charged || state.Current is not { } faction || now - state.Since < Dwell || now < state.NextAllowed)
                continue;
            if (!_prototypes.TryIndex(faction, out var proto) || proto.Company is not { } company)
                continue;
            if (Owns(grid, company))
            {
                state.Charged = true;
                continue;
            }

            state.Charged = true;
            if (!TryCharge(grid, company, cell))
                continue;

            state.NextAllowed = now + Cooldown;
        }
    }

    private bool Owns(EntityUid grid, ProtoId<CompanyPrototype> company)
    {
        if (TryComp<CompanyComponent>(grid, out var ship) && ship.CompanyName != "None")
            return ship.CompanyName == company;

        var saw = false;
        var foreign = false;
        var query = EntityQueryEnumerator<ActorComponent, CompanyComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var member, out var xform))
        {
            if (xform.GridUid != grid || member.CompanyName == "None")
                continue;

            saw = true;
            if (member.CompanyName != company)
                foreign = true;
        }

        return saw && !foreign;
    }

    private bool TryCharge(EntityUid grid, ProtoId<CompanyPrototype> holder, WFSectorCell cell)
    {
        var payer = Payer(grid, holder);
        if (payer is not { } mob || !_bank.TryBankWithdraw(mob, Toll))
            return false;

        _ = _companies.TryDepositBank(holder, Toll);
        if (TryComp<ActorComponent>(mob, out var actor))
        {
            var name = _prototypes.TryIndex(holder, out var company) ? Loc.GetString(company.Name) : holder.Id;
            _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString("wf-sector-toll-paid",
                ("amount", Toll), ("company", name), ("callsign", WFSectorHex.Callsign(cell))));
        }

        return true;
    }

    private EntityUid? Payer(EntityUid grid, ProtoId<CompanyPrototype> holder)
    {
        var query = EntityQueryEnumerator<ActorComponent, BankAccountComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;
            if (TryComp<CompanyComponent>(uid, out var member) && member.CompanyName == holder)
                continue;

            return uid;
        }

        return null;
    }

    private sealed class TollState
    {
        public ProtoId<WFSectorFactionPrototype>? Current;
        public WFSectorCell Cell;
        public TimeSpan Since;
        public TimeSpan LastSeen;
        public TimeSpan NextAllowed;
        public bool Charged;
    }
}
