using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.BountyContracts;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Preferences.Managers;
using Content.Shared._Mono.CCVar;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.BountyContracts;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Posts an encounter's warrant on the sector bounty board and pays it out. A warrant is for one crewman or for a whole
/// crew; it is claimed when they are dead or gone, and shared equally by the players who brought them down: those
/// whose ships hit the encounter's ships and those who hurt a target in person, whether or not they lived, and the
/// living aboard or beside it when it fell. An encounter that ends any other way takes its warrant off the board
/// and pays nobody. With no board up the warrant stands unposted: it still grows and still pays.
/// </summary>
public sealed partial class WFEncounterBountySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BountyContractSystem _board = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly Color BroadcastColor = Color.FromHex("#d7a64a");

    /// <summary>How near a target a player must be when it falls to share the warrant.</summary>
    public const float PayRange = 200f;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextPoll;
    private readonly List<EntityUid> _taken = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFEncounterComponent, WFEncounterStartedEvent>(OnStarted);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnResolved);
        SubscribeLocalEvent<WFEncounterShipSpawnedEvent>(OnShipSpawned);
        SubscribeLocalEvent<WFEncounterBountyComponent, ComponentShutdown>(OnShutdown);
        // Before the damage lands, while the damage still names who dealt it.
        SubscribeLocalEvent<WFEncounterBountyTargetComponent, BeforeDamageChangedEvent>(OnTargetDamaged,
            before: [typeof(Content.Shared._Onyx.Wounds.WoundDamageRoutingSystem)]);
    }

    /// <summary>What a live encounter's warrant pays now, if it has one still open.</summary>
    public int? Pool(EntityUid encounter)
    {
        return TryComp<WFEncounterBountyComponent>(encounter, out var bounty) && bounty.State == WFBountyState.Open
            ? bounty.Pool
            : null;
    }

    /// <summary>
    /// Adds spesos to the prize on a live encounter's warrant and updates its board entry. False if it has no open
    /// warrant or the amount is not positive.
    /// </summary>
    public bool AddToBounty(EntityUid encounter, int spesos)
    {
        if (spesos <= 0 || !TryComp<WFEncounterBountyComponent>(encounter, out var bounty) || bounty.State != WFBountyState.Open)
            return false;

        bounty.Pool = (int) Math.Min((long) bounty.Pool + spesos, int.MaxValue);
        if (bounty.Contract is { } id)
            _board.TrySetContractReward(id, bounty.Pool);

        Log.Info($"Warrant {bounty.Contract} on {ToPrettyString(encounter)} grew by {spesos} to {bounty.Pool}.");
        return true;
    }

    /// <summary>
    /// Takes a crewman off a live warrant on a crew: he is no longer one of those who must fall for it to be paid.
    /// For hands a ship has left behind for good.
    /// </summary>
    public void Release(EntityUid encounter, EntityUid crewman)
    {
        if (!TryComp<WFEncounterBountyComponent>(encounter, out var bounty) || bounty.Target != WFEncounterBountyTarget.Crew
            || !bounty.Crew.Remove(crewman))
            return;

        RemComp<WFEncounterBountyTargetComponent>(crewman);
    }

    private void OnStarted(Entity<WFEncounterComponent> ent, ref WFEncounterStartedEvent args)
    {
        if (!_prototypes.TryIndex(ent.Comp.Prototype, out var prototype) || prototype.Bounty is not { } warrant)
            return;

        if (!ent.Comp.Ships.TryGetValue(warrant.Ship, out var ship) || TerminatingOrDeleted(ship.Grid))
        {
            Log.Error($"Encounter {prototype.ID} has a warrant on ship '{warrant.Ship}', which it does not have.");
            return;
        }

        var bounty = AddComp<WFEncounterBountyComponent>(ent);
        bounty.Ship = warrant.Ship;
        bounty.Target = warrant.Target;
        bounty.Title = Loc.GetString(warrant.Title);
        bounty.Base = bounty.Pool = warrant.Spesos;
        MarkTargets(ent, bounty, ship);
        var shipName = MetaData(ship.Grid).EntityName;
        bounty.Name = warrant.Target == WFEncounterBountyTarget.Captain && bounty.Fugitive is { } fugitive
            ? MetaData(fugitive).EntityName
            : shipName;
        // A simulated ship's crew are marked when its hull is spawned.
        if (bounty.Crew.Count == 0 && !ship.Virtual)
        {
            Log.Error($"Encounter {prototype.ID} has nobody aboard '{warrant.Ship}' to put a warrant on.");
            RemComp<WFEncounterBountyComponent>(ent);
            return;
        }

        var contract = _board.TryPostSystemContract(BountyContractSystem.WarrantCollection, BountyContractCategory.Criminal,
            bounty.Name, bounty.Pool, Loc.GetString(warrant.Description, ("name", bounty.Name), ("ship", shipName)), shipName,
            Loc.GetString("wf-encounter-bounty-author"));
        // Off the board the warrant still stands, so what is added to it is not lost and whoever takes the target is paid.
        if (contract == null)
            Log.Warning($"Encounter {prototype.ID} could not post its warrant: the bounty board is not up. It stands unposted.");

        bounty.Contract = contract?.ContractId;
        NoteLocation(bounty, ship);
        if (warrant.Broadcast is { } broadcast)
        {
            _chatSystem.DispatchGlobalAnnouncement(
                Loc.GetString(broadcast, ("name", bounty.Name), ("ship", shipName), ("reward", bounty.Pool)),
                Loc.GetString("wf-encounter-bounty-author"), colorOverride: BroadcastColor);
        }
    }

    /// <summary>A wanted crew's ship spawned from simulation: its new crew are the ones the warrant is for.</summary>
    private void OnShipSpawned(ref WFEncounterShipSpawnedEvent ev)
    {
        if (!TryComp<WFEncounterBountyComponent>(ev.Encounter, out var bounty) || bounty.State != WFBountyState.Open
            || bounty.Ship != ev.Key || !TryComp<WFEncounterComponent>(ev.Encounter, out var encounter)
            || !encounter.Ships.TryGetValue(ev.Key, out var ship))
            return;

        bounty.Crew.RemoveAll(uid => TerminatingOrDeleted(uid));
        MarkTargets((ev.Encounter, encounter), bounty, ship);
    }

    /// <summary>Marks the crewmen a warrant is for: the ship's captain, else its pilot, else any hand; or every hand.</summary>
    private void MarkTargets(Entity<WFEncounterComponent> ent, WFEncounterBountyComponent bounty, WFEncounterShipState ship)
    {
        EntityUid? wanted = null;
        var rank = 0;
        var crew = EntityQueryEnumerator<WFCrewComponent>();
        while (crew.MoveNext(out var uid, out var member))
        {
            if (member.Group != ship.Group)
                continue;

            if (bounty.Target == WFEncounterBountyTarget.Crew)
            {
                Mark(ent, bounty, uid);
                continue;
            }

            var standing = member.Role == WFCrewRoles.Captain ? 3 : member.Role == WFCrewRoles.Pilot ? 2 : 1;
            if (standing <= rank)
                continue;

            rank = standing;
            wanted = uid;
        }

        if (wanted is { } fugitive)
        {
            Mark(ent, bounty, fugitive);
            bounty.Fugitive = fugitive;
        }
    }

    private void Mark(EntityUid encounter, WFEncounterBountyComponent bounty, EntityUid uid)
    {
        AddComp<WFEncounterBountyTargetComponent>(uid).Encounter = encounter;
        bounty.Crew.Add(uid);
    }

    /// <summary>A player who hurts a target in person has a hand in taking them down.</summary>
    private void OnTargetDamaged(Entity<WFEncounterBountyTargetComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } source)
            return;

        // A beam names the gun as its origin; the attacker is whoever holds it.
        var origin = _crew.Wielder(source);
        if (origin == ent.Owner || TerminatingOrDeleted(origin) || !TryComp<ActorComponent>(origin, out var actor)
            || !HasComp<BankAccountComponent>(origin)
            || !TryComp<WFEncounterBountyComponent>(ent.Comp.Encounter, out var bounty))
            return;

        bounty.Fighters.Add(actor.PlayerSession.UserId);
    }

    /// <summary>An encounter that ran out, was flown to its end or was ended by an admin lets its target go.</summary>
    private void OnResolved(ref WFEncounterResolvedEvent args)
    {
        if (args.Resolution is WFEncounterResolution.Expired or WFEncounterResolution.Completed or WFEncounterResolution.Ended
            && TryComp<WFEncounterBountyComponent>(args.Encounter, out var bounty) && bounty.State == WFBountyState.Open)
            Withdraw(args.Encounter, bounty);
    }

    private void OnShutdown(Entity<WFEncounterBountyComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.State == WFBountyState.Open)
            Withdraw(ent, ent.Comp);
    }

    /// <summary>Takes an unclaimed warrant off the board, paying nobody.</summary>
    private void Withdraw(EntityUid uid, WFEncounterBountyComponent bounty)
    {
        bounty.State = WFBountyState.Void;
        RemoveContract(bounty);
        Log.Info($"Warrant on {ToPrettyString(uid)} withdrawn unclaimed.");
    }

    private void RemoveContract(WFEncounterBountyComponent bounty)
    {
        if (bounty.Contract is { } id)
            _board.TryRemoveSystemContract(id);

        bounty.Contract = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextPoll)
            return;

        _nextPoll = _timing.CurTime + PollInterval;
        _taken.Clear();
        var query = EntityQueryEnumerator<WFEncounterBountyComponent, WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var bounty, out var encounter))
        {
            if (bounty.State != WFBountyState.Open || !encounter.Ships.TryGetValue(bounty.Ship, out var ship))
                continue;

            // Swept up by cleanup, not destroyed: nobody took them.
            if (ship.Removed)
            {
                Withdraw(uid, bounty);
                continue;
            }

            NoteLocation(bounty, ship);
            if (Taken(bounty, ship))
                _taken.Add(uid);
        }

        foreach (var uid in _taken)
        {
            Claim(uid, Comp<WFEncounterBountyComponent>(uid), Comp<WFEncounterComponent>(uid));
        }

        _taken.Clear();
    }

    private void NoteLocation(WFEncounterBountyComponent bounty, WFEncounterShipState ship)
    {
        if (TerminatingOrDeleted(ship.Grid))
            return;

        var at = _transform.GetMapCoordinates(ship.Grid);
        bounty.LastSeen = at.Position;
        bounty.LastMap = at.MapId;
    }

    /// <summary>Whether the target is dead (not merely down) or destroyed; a crew's ship being gone counts too.</summary>
    private bool Taken(WFEncounterBountyComponent bounty, WFEncounterShipState ship)
    {
        // Simulated or still being built, nobody can have taken them: the crew are marked once the hull is crewed. Beaten in
        // a simulated fight, the ship is removed and the warrant withdrawn.
        if (ship.Virtual || ship.Building)
            return false;

        if (bounty.Target == WFEncounterBountyTarget.Captain)
            return bounty.Fugitive is not { } fugitive || Down(fugitive);

        // A crew warrant with nobody marked yet is not taken by its hands all being down.
        return TerminatingOrDeleted(ship.Grid) || bounty.Crew.Count > 0 && bounty.Crew.All(Down);
    }

    private bool Down(EntityUid uid)
    {
        return TerminatingOrDeleted(uid) || _mobs.IsDead(uid);
    }

    /// <summary>Pays the warrant to those who took the target down, takes it off the board and ends the encounter.</summary>
    private void Claim(EntityUid uid, WFEncounterBountyComponent bounty, WFEncounterComponent encounter)
    {
        bounty.State = WFBountyState.Claimed;
        var contract = bounty.Contract;
        RemoveContract(bounty);

        var helpers = Helpers(bounty, encounter);
        Pay(uid, bounty, encounter, contract, helpers);
        _encounters.Resolve((uid, encounter), WFEncounterResolution.Destroyed);
    }

    /// <summary>
    /// The players who helped. Those whose ships hit the encounter's ships at least as often as a reward counts and
    /// those who hurt a target in person have earned it whether or not they lived. Those aboard the target's ship
    /// or within <see cref="PayRange"/> of it count only if alive.
    /// </summary>
    public List<ICommonSession> Helpers(WFEncounterBountyComponent bounty, WFEncounterComponent encounter)
    {
        var found = new Dictionary<NetUserId, ICommonSession>();
        foreach (var user in bounty.Fighters)
        {
            if (_players.TryGetSessionById(user, out var session))
                found[user] = session;
        }

        foreach (var (user, bySide) in encounter.Hits)
        {
            if (bySide.Values.Sum() >= WFEncounterRewardSystem.MinimumHits && _players.TryGetSessionById(user, out var session))
                found[user] = session;
        }

        var grid = encounter.Ships.TryGetValue(bounty.Ship, out var ship) && !TerminatingOrDeleted(ship.Grid) ? ship.Grid : (EntityUid?) null;
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (HasComp<GhostComponent>(uid) || _mobs.IsDead(uid))
                continue;

            var aboard = grid != null && xform.GridUid == grid;
            var near = xform.MapID == bounty.LastMap
                       && (_transform.GetWorldPosition(xform) - bounty.LastSeen).LengthSquared() <= PayRange * PayRange;
            if (aboard || near)
                found[actor.PlayerSession.UserId] = actor.PlayerSession;
        }

        return found.Values.ToList();
    }

    /// <summary>Whether a helper has an account to be paid into: their body's, or as a ghost their character's.</summary>
    private bool Payable(ICommonSession session)
    {
        if (session.AttachedEntity is { } mob && !HasComp<GhostComponent>(mob))
            return HasComp<BankAccountComponent>(mob);

        return _prefs.TryGetCachedPreferences(session.UserId, out var prefs) && prefs.SelectedCharacter is HumanoidCharacterProfile;
    }

    private bool Deposit(ICommonSession session, int amount)
    {
        if (session.AttachedEntity is { } mob && HasComp<BankAccountComponent>(mob))
            return _bank.TryBankDeposit(mob, amount, false);

        // A ghost has no account of its own: the money goes to the player's character.
        return _config.GetCVar(MonoCVars.DepositEnabled)
               && _prefs.TryGetCachedPreferences(session.UserId, out var prefs)
               && prefs.SelectedCharacter is HumanoidCharacterProfile profile
               && _bank.TryBankDeposit(session, prefs, profile, amount, out _);
    }

    /// <summary>
    /// Shares the prize equally among helpers with a bank account, into those accounts. The board promised the whole
    /// number, so the hourly cap on encounter rewards does not apply, and a warrant does not count toward it. What an
    /// equal split leaves over, and any share that will not go in, rides on the next share paid.
    /// </summary>
    private void Pay(EntityUid uid, WFEncounterBountyComponent bounty, WFEncounterComponent encounter, uint? contract,
        List<ICommonSession> helpers)
    {
        helpers.RemoveAll(helper => !Payable(helper));
        if (helpers.Count == 0 || bounty.Pool <= 0)
        {
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"Warrant {contract} ({bounty.Title}) on encounter {encounter.Prototype} ({ToPrettyString(uid):entity}) was taken with nobody to pay");
            return;
        }

        var share = bounty.Pool / helpers.Count;
        var carry = bounty.Pool - share * helpers.Count;
        foreach (var session in helpers)
        {
            var amount = share + carry;
            if (amount <= 0)
                continue;

            if (!Deposit(session, amount))
            {
                carry = amount;
                continue;
            }

            carry = 0;
            _chat.DispatchServerMessage(session, Loc.GetString("wf-encounter-bounty-paid",
                ("title", bounty.Title), ("name", bounty.Name), ("amount", amount)));
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{session.Name} was paid {amount} spesos for warrant {contract} ({bounty.Title}) on encounter {encounter.Prototype} ({ToPrettyString(uid):entity})");
        }

        if (carry > 0)
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{carry} spesos of warrant {contract} ({bounty.Title}) on encounter {encounter.Prototype} ({ToPrettyString(uid):entity}) could not be paid to anyone");
        }
    }
}
