using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Contracts;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Storage.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Contracts;

/// <summary>Posts contract offers, runs accepted contracts and pays them out; see the module README.</summary>
public sealed partial class WFContractSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;

    /// <summary>Standing points needed for each rank, lowest first: Hostile, Distrusted, Applicant and up.</summary>
    public static readonly int[] RankThresholds = [int.MinValue, -5, 0, 1, 3, 6, 10];

    private static readonly string[] RankNames =
    [
        "wf-contract-rank-hostile", "wf-contract-rank-distrusted", "wf-contract-rank-applicant", "wf-contract-rank-junior",
        "wf-contract-rank-member", "wf-contract-rank-experienced", "wf-contract-rank-senior",
    ];

    /// <summary>Standing never goes beyond these.</summary>
    public const int MinStanding = -20;
    public const int MaxStanding = 40;

    /// <summary>How long a contractor waits to post again after an offer leaves the board.</summary>
    private static readonly TimeSpan RefillDelay = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan OfferLifetimeMin = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan OfferLifetimeMax = TimeSpan.FromMinutes(35);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OfferInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UiInterval = TimeSpan.FromSeconds(5);

    /// <summary>Ended contracts kept for each holder's history.</summary>
    private const int HistoryLength = 12;

    private readonly Dictionary<int, WFContract> _contracts = new();
    private readonly Dictionary<WFContractHolder, Dictionary<string, int>> _standing = new();
    private readonly Dictionary<WFContractHolder, int> _owed = new();
    private readonly Dictionary<WFContractHolder, List<WFContract>> _history = new();
    private readonly Dictionary<string, TimeSpan> _nextOffer = new();
    private int _nextId = 1;
    private TimeSpan _nextCheck;
    private TimeSpan _nextOffers;
    private TimeSpan _nextUi;
    private bool _dirty;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        InitializeObjectives();
        InitializeUi();
        InitializePersistence();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _contracts.Clear();
        _standing.Clear();
        _loaded.Clear();
        _generation++;
        _owed.Clear();
        _history.Clear();
        _nextOffer.Clear();
        _departures.Clear();
        _nextId = 1;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now >= _nextCheck)
        {
            _nextCheck = now + CheckInterval;
            CheckActive(now);
        }

        if (now >= _nextOffers)
        {
            _nextOffers = now + OfferInterval;
            RefreshOffers(now);
        }

        if (_dirty || now >= _nextUi)
        {
            _nextUi = now + UiInterval;
            _dirty = false;
            UpdateMarkers();
            PushAll();
        }
    }

    /// <summary>Every contract this round, offered, live or ended.</summary>
    public IEnumerable<WFContract> All => _contracts.Values;

    public WFContract? Get(int id) => _contracts.GetValueOrDefault(id);

    /// <summary>The character a mob is played as, if it has a player and a name. Never a ghost.</summary>
    public WFContractHolder? HolderOf(EntityUid mob)
    {
        if (HasComp<Content.Shared.Ghost.GhostComponent>(mob)
            || !_mind.TryGetMind(mob, out _, out var mind) || mind.UserId is not { } user || mind.CharacterName is not { } name)
            return null;

        return new WFContractHolder(user, name);
    }

    /// <summary>The mob a holder plays now, if they are in the round as that character.</summary>
    public EntityUid? MobOf(WFContractHolder holder)
    {
        if (!_mind.TryGetMind(holder.User, out var mind) || mind.Value.Comp.CharacterName != holder.Character)
            return null;

        return mind.Value.Comp.OwnedEntity;
    }

    public IEnumerable<WFContract> ActiveFor(WFContractHolder holder)
    {
        return _contracts.Values.Where(c => c.Status == WFContractStatus.Active && c.User == holder.User && c.Character == holder.Character);
    }

    /// <summary>Pay earned by a holder that has not reached their bank yet.</summary>
    public int Owed(WFContractHolder holder) => _owed.GetValueOrDefault(holder);

    public int Standing(WFContractHolder holder, string contractor)
    {
        return _standing.TryGetValue(holder, out var standing) ? standing.GetValueOrDefault(contractor) : 0;
    }

    public static int RankOf(int points)
    {
        var rank = 0;
        for (var i = 0; i < RankThresholds.Length; i++)
        {
            if (points >= RankThresholds[i])
                rank = i;
        }

        return rank;
    }

    /// <summary>The rank a tier needs: Distrusted for tier 1, Applicant for 2, one more per tier.</summary>
    public static int RankForTier(int tier) => Math.Clamp(tier, 1, RankThresholds.Length - 2);

    public static string RankName(int rank) => RankNames[Math.Clamp(rank, 0, RankNames.Length - 1)];

    /// <summary>Moves a character's standing with a contractor and saves it.</summary>
    private void AddStanding(WFContractHolder holder, string contractor, int points)
    {
        if (points == 0)
            return;

        var standing = _standing.GetOrNew(holder);
        standing[contractor] = Math.Clamp(standing.GetValueOrDefault(contractor) + points, MinStanding, MaxStanding);
        if (_loaded.Contains(holder))
            SaveStanding(holder, contractor);
        else
            _ = LoadStanding(holder); // A failed or missed load: merge this change with the stored row and save it.
    }

    /// <summary>Why a character cannot take an offer, or null if they can. The mob is checked for company membership.</summary>
    public string? Locked(WFContractHolder? holder, WFContract contract, EntityUid? mob = null, (int Size, int Slots, int Held)? crew = null)
    {
        if (holder is not { } who)
            return Loc.GetString("wf-contract-locked-no-character");

        var proto = _proto.Index(contract.Prototype);
        var contractor = _proto.Index(proto.Contractor);
        if (!OfferedTo(contract, who.User))
            return Loc.GetString("wf-contract-error-gone");

        if (contractor.Companies.Count > 0
            && (mob is not { } body || CompOrNull<CompanyComponent>(body)?.CompanyName.Id is not { } company
                || !contractor.Companies.Any(c => c.Id == company)))
            return Loc.GetString("wf-contract-locked-company", ("contractor", Loc.GetString(contractor.Name)));

        var rank = RankOf(Standing(who, proto.Contractor));
        if (rank == 0)
            return Loc.GetString("wf-contract-locked-hostile", ("contractor", Loc.GetString(contractor.Name)));

        var needed = RankForTier(proto.Tier);
        if (rank < needed)
            return Loc.GetString("wf-contract-locked-rank", ("rank", Loc.GetString(RankName(needed))));

        var (size, slots, held) = crew ?? CrewSlots(who.User);
        if (size < proto.MinCrew)
            return Loc.GetString("wf-contract-locked-crew", ("needed", proto.MinCrew), ("size", size));

        if (proto.Requires.Count > 0 && (mob is { } carrier ? Missing(carrier, proto) : proto.Requires) is { Count: > 0 } missing)
            return Loc.GetString("wf-contract-locked-requires", ("items", string.Join(", ", missing.Select(r => Loc.GetString(r.Name)))));

        if (held >= slots)
            return Loc.GetString("wf-contract-locked-full", ("max", slots));

        return null;
    }

    /// <summary>Accepts an offer for the character a mob plays, at a board. Returns why not, or null once accepted.</summary>
    public string? TryAccept(EntityUid mob, int id, EntityUid board)
    {
        if (HolderOf(mob) is not { } holder)
            return Loc.GetString("wf-contract-locked-no-character");

        // Only at a board its contractor posts on, since cargo spawns beside the board.
        if (!_contracts.TryGetValue(id, out var contract) || contract.Status != WFContractStatus.Offered
            || _proto.Index(_proto.Index(contract.Prototype).Contractor).Board
               != (CompOrNull<WFContractBoardComponent>(board)?.Board ?? WFContractBoards.Central))
            return Loc.GetString("wf-contract-error-gone");

        if (Locked(holder, contract, mob) is { } locked)
            return locked;

        if (contract.Deposit > 0 && !_bank.TryBankWithdraw(mob, contract.Deposit))
            return Loc.GetString("wf-contract-error-deposit", ("deposit", contract.Deposit));

        return Begin(mob, holder, contract, board);
    }

    /// <summary>Gives an offer to the character a mob plays with no checks and no deposit, for admins and tests.</summary>
    public bool ForceAccept(EntityUid mob, int id, EntityUid board)
    {
        if (HolderOf(mob) is not { } holder || !_contracts.TryGetValue(id, out var contract)
            || contract.Status != WFContractStatus.Offered)
            return false;

        contract.Deposit = 0;
        return Begin(mob, holder, contract, board) == null;
    }

    /// <summary>Makes an offer live for its holder and puts its objective in the world. Returns why not, or null.</summary>
    private string? Begin(EntityUid mob, WFContractHolder holder, WFContract contract, EntityUid board)
    {
        var now = _timing.CurTime;
        contract.Status = WFContractStatus.Active;
        contract.User = holder.User;
        contract.Character = holder.Character;
        contract.Company = CompOrNull<CompanyComponent>(mob)?.CompanyName.Id;
        contract.Accepted = now;
        contract.Deadline = now + contract.Duration;
        if (!TryStart(contract, board))
        {
            if (contract.Deposit > 0)
                _bank.TryBankDeposit(mob, contract.Deposit);

            // A courier run can fail after spawning some of its packages.
            Cleanup(contract);
            contract.Status = WFContractStatus.Offered;
            contract.User = null;
            contract.Character = string.Empty;
            RemoveOffer(contract);
            return Loc.GetString("wf-contract-error-start");
        }

        _nextOffer[_proto.Index(contract.Prototype).Contractor] = now + RefillDelay;
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(mob):player} accepted contract #{contract.Id} {contract.Prototype} for {contract.Reward}, deposit {contract.Deposit}");
        Tell(holder, Loc.GetString("wf-contract-accepted", ("title", Title(contract)), ("deadline", (int) contract.Duration.TotalMinutes)));
        _dirty = true;
        return null;
    }

    /// <summary>Gives up a live contract. The deposit is lost and standing falls.</summary>
    public bool TryAbandon(WFContractHolder holder, int id)
    {
        if (!_contracts.TryGetValue(id, out var contract) || contract.Status != WFContractStatus.Active
            || contract.User != holder.User || contract.Character != holder.Character)
            return false;

        End(contract, WFContractStatus.Abandoned);
        Tell(holder, Loc.GetString("wf-contract-abandoned", ("title", Title(contract))));
        return true;
    }

    /// <summary>Splits the pay, grown by any crew bonus, among the crew; returns the deposit and credits to the holder; moves standing.</summary>
    private void Complete(WFContract contract)
    {
        if (contract.Status != WFContractStatus.Active || contract.User is not { } user)
            return;

        var holder = new WFContractHolder(user, contract.Character);
        var proto = _proto.Index(contract.Prototype);
        // Read before End clears the contract's items.
        contract.Reward += ArtifactBonus(contract);
        End(contract, WFContractStatus.Completed);

        var crew = PaidCrew(holder);
        var pool = CrewPool(contract.Reward, proto, crew.Count);
        var share = pool / crew.Count;
        var remainder = pool - share * crew.Count;
        foreach (var member in crew)
        {
            var pay = share + (member == holder ? remainder + contract.Deposit : 0);
            var paid = Pay(member, pay);
            AddStanding(member, proto.Contractor, proto.Tier >= 3 ? 2 : 1);
            foreach (var (rival, cost) in proto.Harms)
            {
                AddStanding(member, rival, -cost);
            }

            Tell(member, Loc.GetString(paid ? "wf-contract-completed" : "wf-contract-completed-owed",
                ("title", Title(contract)), ("pay", pay), ("crew", crew.Count)));
        }

        if (proto.Credits > 0 && proto.CreditEntity is { } credits && MobOf(holder) is { } mob)
            GiveCredits(mob, credits, proto.Credits);

        PostFollowUp(holder, proto);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Contract #{contract.Id} {contract.Prototype} completed by {holder.Character} ({user}): {pool} split {crew.Count} ways, deposit {contract.Deposit} returned");
    }

    /// <summary>Ends a live contract that failed: the deposit is lost and standing falls.</summary>
    private void Fail(WFContract contract, string reason)
    {
        if (contract.Status != WFContractStatus.Active || contract.User is not { } user)
            return;

        var holder = new WFContractHolder(user, contract.Character);
        End(contract, WFContractStatus.Failed);
        Tell(holder, Loc.GetString("wf-contract-failed", ("title", Title(contract)), ("reason", Loc.GetString(reason))));
    }

    /// <summary>Calls off a live contract through no fault of its holder and returns the deposit.</summary>
    private void Void(WFContract contract, string reason)
    {
        if (contract.Status != WFContractStatus.Active || contract.User is not { } user)
            return;

        var holder = new WFContractHolder(user, contract.Character);
        End(contract, WFContractStatus.Void);
        if (contract.Deposit > 0)
            Pay(holder, contract.Deposit);

        Tell(holder, Loc.GetString("wf-contract-void", ("title", Title(contract)), ("reason", Loc.GetString(reason)),
            ("deposit", contract.Deposit)));
    }

    /// <summary>Closes a live contract, clears what it put in the world and files it in its holder's history.</summary>
    private void End(WFContract contract, WFContractStatus status)
    {
        contract.Status = status;
        contract.Ended = _timing.CurTime;
        if (contract.User is { } user)
        {
            var holder = new WFContractHolder(user, contract.Character);
            if (status is WFContractStatus.Failed or WFContractStatus.Abandoned)
                AddStanding(holder, _proto.Index(contract.Prototype).Contractor, -1);

            var history = _history.GetOrNew(holder);
            history.Insert(0, contract);
            if (history.Count > HistoryLength)
                history.RemoveAt(history.Count - 1);
        }

        Cleanup(contract);
        _contracts.Remove(contract.Id);
        _dirty = true;
    }

    /// <summary>Pays a holder into their bank, or owes it to them until they can be paid.</summary>
    private bool Pay(WFContractHolder holder, int amount)
    {
        if (amount <= 0)
            return true;

        if (MobOf(holder) is { } mob && _bank.TryBankDeposit(mob, amount))
            return true;

        _owed[holder] = _owed.GetValueOrDefault(holder) + amount;
        return false;
    }

    /// <summary>Pays what a holder is owed, if they can be paid now.</summary>
    private void PayOwed(WFContractHolder holder)
    {
        if (!_owed.TryGetValue(holder, out var owed) || MobOf(holder) is not { } mob || !_bank.TryBankDeposit(mob, owed))
            return;

        _owed.Remove(holder);
        Tell(holder, Loc.GetString("wf-contract-owed-paid", ("pay", owed)));
    }

    /// <summary>A server message to the holder, if they are playing that character now.</summary>
    private void Tell(WFContractHolder holder, string message)
    {
        if (MobOf(holder) is null || !_players.TryGetSessionById(holder.User, out var session))
            return;

        var wrapped = Loc.GetString("wf-contract-message-wrap", ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, default, false, session.Channel, colorOverride: Color.FromHex("#9fd3e0"));
    }

    private void CheckActive(TimeSpan now)
    {
        foreach (var contract in _contracts.Values.ToList())
        {
            if (contract.Status != WFContractStatus.Active)
                continue;

            CheckObjective(contract);
            if (contract.Status == WFContractStatus.Active && now >= contract.Deadline)
                Fail(contract, "wf-contract-reason-expired");
        }

        UpdateDepartures(now);
    }

    private void RefreshOffers(TimeSpan now)
    {
        foreach (var contract in _contracts.Values.ToList())
        {
            if (contract.Status != WFContractStatus.Offered || now < contract.Deadline && !TerminatingOrDeleted(contract.Home))
                continue;

            // A follow-up never held a board slot, so its expiry doesn't hold the contractor back.
            if (contract.ReservedFor != null)
                _contracts.Remove(contract.Id);
            else
                RemoveOffer(contract);
        }

        if (BoardStation() is not { } board)
            return;

        var homes = ContractorHomes();
        var players = _players.PlayerCount;
        foreach (var contractor in _proto.EnumeratePrototypes<WFContractorPrototype>())
        {
            if (!homes.ContainsKey(contractor.ID) || now < _nextOffer.GetValueOrDefault(contractor.ID))
                continue;

            var posted = _contracts.Values.Count(c => c.Status == WFContractStatus.Offered && c.ReservedFor == null
                                                      && _proto.Index(c.Prototype).Contractor == contractor.ID);
            if (posted >= contractor.Offers)
                continue;

            if (RollOffer(contractor, homes, board, players, now) is { } offer)
            {
                Log.Debug($"Posted contract #{offer.Id} {offer.Prototype} for {offer.Reward}.");
                _contracts[offer.Id] = offer;
                _dirty = true;
            }
        }
    }

    private void RemoveOffer(WFContract contract)
    {
        _contracts.Remove(contract.Id);
        _nextOffer[_proto.Index(contract.Prototype).Contractor] = _timing.CurTime + RefillDelay;
        _dirty = true;
    }

    /// <summary>Rolls one offer for a contractor from its templates that fit the round, or null if none do.</summary>
    private WFContract? RollOffer(WFContractorPrototype contractor, Dictionary<string, EntityUid> homes, EntityUid board, int players,
        TimeSpan now)
    {
        var posted = _contracts.Values.Where(c => c.Status == WFContractStatus.Offered).Select(c => c.Prototype.Id).ToHashSet();
        var templates = _proto.EnumeratePrototypes<WFContractPrototype>()
            // Weight 0 marks a chain link, posted only as a follow-up.
            .Where(t => t.Contractor == contractor.ID && t.Weight > 0 && t.MinPlayers <= players && PresetAllows(t))
            .ToList();
        // Repeat a template only when nothing else fits.
        var fresh = templates.Where(t => !posted.Contains(t.ID)).ToList();
        if (fresh.Count > 0)
            templates = fresh;

        for (var attempt = 0; attempt < 4 && templates.Count > 0; attempt++)
        {
            var template = PickWeighted(templates);
            templates.Remove(template);
            if (Build(template, homes, board, now) is { } contract)
                return contract;
        }

        return null;
    }

    /// <summary>Posts an offer from a template now, for admins and tests; null if the round can't give it.</summary>
    public WFContract? PostOffer(ProtoId<WFContractPrototype> id)
    {
        if (!_proto.TryIndex(id, out var template) || BoardStation() is not { } board
            || Build(template, ContractorHomes(), board, _timing.CurTime) is not { } contract)
            return null;

        _contracts[contract.Id] = contract;
        _dirty = true;
        return contract;
    }

    /// <summary>An offer from a template with its ends, counts and pay fixed, or null if the round cannot give it.</summary>
    private WFContract? Build(WFContractPrototype template, Dictionary<string, EntityUid> homes, EntityUid board, TimeSpan now)
    {
        if (!homes.TryGetValue(template.Contractor, out var home))
            return null;

        var contract = new WFContract
        {
            Id = _nextId,
            Prototype = template.ID,
            Home = home,
            Reward = _random.Next(template.Reward.X, Math.Max(template.Reward.X, template.Reward.Y) + 1),
            Deposit = template.Deposit,
            Duration = TimeSpan.FromMinutes(template.Minutes),
            Deadline = now + OfferLifetimeMin + (OfferLifetimeMax - OfferLifetimeMin) * _random.NextDouble(),
        };

        if (!TryRoll(contract, template, homes, board))
            return null;

        contract.Reward = (int) Math.Round(contract.Reward / 50.0) * 50;
        _nextId++;
        return contract;
    }

    private WFContractPrototype PickWeighted(List<WFContractPrototype> templates)
    {
        var total = templates.Sum(t => Math.Max(0f, t.Weight));
        var roll = _random.NextFloat() * total;
        foreach (var template in templates)
        {
            roll -= Math.Max(0f, template.Weight);
            if (roll <= 0f)
                return template;
        }

        return templates[^1];
    }
}
