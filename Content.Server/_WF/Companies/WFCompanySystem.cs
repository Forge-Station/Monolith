using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.Companies;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Companies;

/// <summary>Keeps every player-run company in memory, writes each change through to the database and answers the Company window.</summary>
public sealed partial class WFCompanySystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PlayTimeTrackingManager _playTime = default!;
    [Dependency] private IConfigurationManager _config = default!;

    private ISawmill _log = default!;

    private readonly Dictionary<int, Company> _companies = new();
    /// <summary>Company id by member profile id.</summary>
    private readonly Dictionary<int, int> _memberCompany = new();
    /// <summary>Profile id by slot, per player.</summary>
    private readonly Dictionary<NetUserId, Dictionary<int, int>> _profileIds = new();
    /// <summary>The slot each player spawned this round, so the window acts as the character being played.</summary>
    private readonly Dictionary<NetUserId, int> _spawnedSlots = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextRequest = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextProfileRefresh = new();
    private readonly List<Task> _pendingWrites = new();
    private Task? _loading;

    /// <summary>Whether the companies have been read from the database.</summary>
    public bool Loaded { get; private set; }

    private sealed class Company
    {
        public int Id;
        public string Name = string.Empty;
        public string Tag = string.Empty;
        public string Description = string.Empty;
        public Color Colour = Color.White;
        public int Owner;
        public long Balance;
        public int Cut;
        public bool Recruiting;
        public bool Frozen;
        public bool ArtLocked;
        public byte[]? Logo;
        public int LogoVersion;
        public int LaunchFee;
        /// <summary>The rank anyone may join at without applying.</summary>
        public int? OpenRank;
        public bool Private;
        /// <summary>The owner's own title, base job and icon; its id is the company id negated.</summary>
        public Rank OwnerRank = new();
        public readonly List<Licence> Licences = new();
        /// <summary>Highest first.</summary>
        public readonly List<Rank> Ranks = new();
        public readonly Dictionary<int, Member> Members = new();
        /// <summary>Newest first, capped at <see cref="WFStoredCompany.LedgerCache"/>.</summary>
        public readonly List<WolfgateCompanyLedger> Ledger = new();
        public readonly HashSet<int> Invitations = new();
        public readonly Dictionary<int, Applicant> Applications = new();
    }

    private sealed class Rank
    {
        public int Id;
        public string Title = string.Empty;
        public WFCompanyPermissions Permissions;
        public int Wage;
        public int WithdrawCap;
        public string BaseJob = string.Empty;
        public byte[]? Icon;
        public int IconVersion;
        /// <summary>Loadout prototype ids bought for the member at each spawn.</summary>
        public List<string> Loadout = new();
    }

    private sealed class Member
    {
        public int Profile;
        public NetUserId User;
        public string Name = string.Empty;
        public int Slot;
        public int? Rank;
        public int UnpaidSeconds;
        public int WithdrawnToday;
        public DateTime WithdrawDay;
    }

    private sealed record Applicant(NetUserId User, string Name, int Slot, int? RankId = null, string Message = "");

    /// <summary>The character a session acts as: the one being played, or the one selected in the lobby.</summary>
    private readonly record struct Character(NetUserId User, int Slot, HumanoidCharacterProfile Profile, int ProfileId);

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("wf.companies");
        SubscribeNetworkEvent<WFCompanyRequest>(OnRequest);
        // After the record and any custom job title exist, so the rank title lands on top of them.
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawned,
            after: new[] { typeof(Content.Server._WF.Roles.CustomJobTitleSystem), typeof(Content.Server.StationRecords.Systems.StationRecordsSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
        _players.PlayerStatusChanged += OnPlayerStatus;
        InitializeIdentity();
        InitializeReport();
        InitializeAccess();
        _loading = Reload();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatus;
    }

    /// <summary>Waits for loading and every pending write; for tests and admin tools.</summary>
    public async Task Flush()
    {
        if (_loading != null)
            await _loading;
        while (_pendingWrites.Count > 0)
        {
            var pending = _pendingWrites.ToArray();
            await Task.WhenAll(pending);
            _pendingWrites.RemoveAll(task => pending.Contains(task));
        }
    }

    /// <summary>Rereads everything, picking up characters deleted since, once pending writes have landed.</summary>
    public async Task Reload()
    {
        Loaded = false;
        await Task.WhenAll(_pendingWrites.ToArray());
        _pendingWrites.Clear();
        List<WFStoredCompany> stored;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                stored = await _db.LoadCompaniesAsync();
                break;
            }
            // A synchronous database refuses overlapping requests, such as another system's write still finishing; retry.
            catch (InvalidOperationException) when (attempt < 60)
            {
                await Task.Delay(50);
            }
            catch (Exception e)
            {
                _log.Error($"Could not load companies: {e}");
                return;
            }
        }
        _companies.Clear();
        _memberCompany.Clear();
        foreach (var entry in stored)
        {
            var row = entry.Company;
            var company = new Company
            {
                Id = row.Id, Name = row.Name, Tag = row.Tag, Description = row.Description,
                Colour = Color.TryFromHex(row.Colour, out var colour) ? colour : Color.White, Owner = row.OwnerProfileId, Balance = row.Balance,
                Cut = row.Cut, Recruiting = row.Recruiting, Frozen = row.Frozen, ArtLocked = row.ArtLocked,
                Logo = row.Logo?.Length == WFCompanyPolicy.LogoBytes ? row.Logo : null, LogoVersion = row.LogoVersion, LaunchFee = row.LaunchFee,
                OpenRank = row.OpenRankId, Private = row.Private,
            };
            company.OwnerRank = new Rank
            {
                Id = -row.Id, Title = row.OwnerTitle.Length > 0 ? row.OwnerTitle : Loc.GetString("wf-company-owner"),
                BaseJob = row.OwnerJob.Length > 0 ? row.OwnerJob : "Contractor", Permissions = WFCompanyPermissions.All,
                Icon = row.OwnerIcon?.Length == WFCompanyPolicy.IconBytes ? row.OwnerIcon : null, IconVersion = row.OwnerIconVersion,
                Loadout = SplitLoadout(row.OwnerLoadout),
            };
            foreach (var rank in row.Ranks.OrderBy(rank => rank.Order))
            {
                company.Ranks.Add(new Rank { Id = rank.Id, Title = rank.Title, Permissions = (WFCompanyPermissions) rank.Permissions,
                    Wage = rank.Wage, WithdrawCap = rank.WithdrawCap, BaseJob = rank.BaseJob,
                    Icon = rank.Icon?.Length == WFCompanyPolicy.IconBytes ? rank.Icon : null, IconVersion = rank.IconVersion,
                    Loadout = SplitLoadout(rank.Loadout) });
            }
            foreach (var member in entry.Members)
            {
                company.Members[member.ProfileId] = new Member
                {
                    Profile = member.ProfileId, User = new NetUserId(member.UserId), Name = member.CharacterName, Slot = member.Slot,
                    Rank = member.RankId, UnpaidSeconds = member.UnpaidSeconds, WithdrawnToday = member.WithdrawnToday,
                    WithdrawDay = member.WithdrawDay,
                };
                _memberCompany[member.ProfileId] = company.Id;
            }
            company.Ledger.AddRange(entry.Ledger);
            foreach (var licence in entry.Licences)
                company.Licences.Add(new Licence { Id = licence.Id, Vessel = licence.Vessel, ShipName = licence.ShipName });
            foreach (var application in entry.Applications)
            {
                company.Applications[application.ProfileId] = new Applicant(new NetUserId(application.UserId), application.CharacterName,
                    application.Slot, application.RankId, application.Message);
            }
            _companies[company.Id] = company;
            RepairOwnership(company);
        }
        Loaded = true;
        foreach (var session in _players.Sessions)
            SendState(session);
    }

    /// <summary>A deleted owner passes the company to the highest-ranked member; an empty company is disbanded.</summary>
    private void RepairOwnership(Company company)
    {
        if (company.Members.TryGetValue(company.Owner, out var owner))
        {
            if (owner.Rank != null)
            {
                owner.Rank = null;
                PersistMember(owner);
            }
            return;
        }
        if (company.Members.Count == 0)
        {
            RemoveCompany(company);
            return;
        }
        var heir = company.Members.Values.OrderBy(member => RankOrder(company, member) ?? -1).ThenBy(member => member.Profile).First();
        company.Owner = heir.Profile;
        heir.Rank = null;
        PersistCompany(company);
        PersistMember(heir);
        _log.Info($"Company {company.Name} ({company.Id}) passed to {heir.Name} after its owner was deleted.");
    }

    private void OnRestart(RoundRestartCleanupEvent args)
    {
        SavePlayedTime();
        _spawnedSlots.Clear();
        _rankJoins.Clear();
        _newRecords.Clear();
        _companyBodies.Clear();
        _roundStats.Clear();
        _memberKeys.Clear();
        _launched.Clear();
        _reserved.Clear();
        _companyPaidLoadouts.Clear();
        foreach (var company in _companies.Values)
            company.Invitations.Clear();
        _loading = Reload();
    }

    private void OnSpawned(PlayerSpawnCompleteEvent args)
    {
        var records = _newRecords.Where(entry => ReferenceEquals(entry.Profile, args.Profile)).Select(entry => (entry.Key, entry.Record)).ToArray();
        _newRecords.Clear();
        if (_preferences.GetPreferencesOrNull(args.Player.UserId) is not { } prefs || SpawnedSlot(prefs, args.Profile) is not { } slot)
        {
            _rankJoins.Remove(args.Player.UserId);
            return;
        }
        _spawnedSlots[args.Player.UserId] = slot;
        EnsureComp<WFCompanyRankHudComponent>(args.Mob);
        ApplyMemberIdentity(args.Player, args.Mob, slot, args.JobId, records);
        IssueRankLoadout(args.Player, args.Mob, slot);
    }

    private void OnPlayerStatus(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
            Track(RefreshProfileIds(args.Session.UserId));
        else if (args.NewStatus == SessionStatus.Disconnected)
        {
            foreach (var member in _unsavedTime.Where(member => member.User == args.Session.UserId).ToArray())
            {
                PersistMember(member);
                _unsavedTime.Remove(member);
            }
            _profileIds.Remove(args.Session.UserId);
            _nextRequest.Remove(args.Session.UserId);
            _nextProfileRefresh.Remove(args.Session.UserId);
        }
    }

    private async Task RefreshProfileIds(NetUserId user)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                _profileIds[user] = await _db.GetProfileIdsAsync(user.UserId);
                return;
            }
            catch (InvalidOperationException) when (attempt < 60)
            {
                await Task.Delay(50);
            }
            catch (Exception e)
            {
                _log.Error($"Could not read profile ids for {user}: {e}");
                return;
            }
        }
    }

    /// <summary>Rereads a player's profile ids in the background, at most every ten seconds, for lists of other players.</summary>
    private void RefreshProfileIdsLater(NetUserId user)
    {
        if (_nextProfileRefresh.TryGetValue(user, out var next) && next > _timing.CurTime)
            return;
        _nextProfileRefresh[user] = _timing.CurTime + TimeSpan.FromSeconds(10);
        Track(RefreshProfileIds(user));
    }

    /// <summary>The acting character, rereading profile ids once when a new slot isn't known yet.</summary>
    private async Task<Character?> GetCharacter(ICommonSession session)
    {
        if (CurrentCharacter(session) is { } known)
            return known;
        if (_preferences.GetPreferencesOrNull(session.UserId) == null)
            return null;
        await RefreshProfileIds(session.UserId);
        return CurrentCharacter(session);
    }

    private Character? CurrentCharacter(ICommonSession session)
    {
        if (_preferences.GetPreferencesOrNull(session.UserId) is not { } prefs)
            return null;
        var slot = prefs.SelectedCharacterIndex;
        if (_spawnedSlots.TryGetValue(session.UserId, out var spawned) && session.AttachedEntity is { } body &&
            !HasComp<GhostComponent>(body) && (!TryComp<MobStateComponent>(body, out var state) || state.CurrentState != MobState.Dead))
            slot = spawned;
        if (!prefs.Characters.TryGetValue(slot, out var profile) || profile is not HumanoidCharacterProfile humanoid ||
            !_profileIds.TryGetValue(session.UserId, out var ids) || !ids.TryGetValue(slot, out var id))
            return null;
        return new Character(session.UserId, slot, humanoid, id);
    }

    /// <summary>Ironman characters can't share in company money.</summary>
    private static bool IsIronman(HumanoidCharacterProfile profile) => profile.TraitPreferences.Any(trait => trait.Id == "Ironman");

    private Company? CompanyOf(int profile) =>
        _memberCompany.TryGetValue(profile, out var id) && _companies.TryGetValue(id, out var company) ? company : null;

    private static Rank? RankOf(Company company, Member member) =>
        member.Rank is { } id ? company.Ranks.FirstOrDefault(rank => rank.Id == id) : null;

    /// <summary>Null for the owner; otherwise the rank's place, highest first.</summary>
    private static int? RankOrder(Company company, Member member) =>
        member.Profile == company.Owner ? null : company.Ranks.FindIndex(rank => rank.Id == member.Rank) is >= 0 and var index ? index : int.MaxValue;

    /// <summary>A member's title: their rank's, or the owner rank's for the owner.</summary>
    private static string TitleOf(Company company, Member member) =>
        member.Profile == company.Owner ? company.OwnerRank.Title : RankOf(company, member)?.Title ?? string.Empty;

    /// <summary>The rank whose icon and job a member uses; the owner has their own.</summary>
    private static Rank? IconRankOf(Company company, Member member) =>
        member.Profile == company.Owner ? company.OwnerRank : RankOf(company, member);

    private void PersistOwnerRank(Company company) => Track(_db.SetCompanyOwnerRankAsync(company.Id, company.OwnerRank.Title,
        company.OwnerRank.BaseJob, company.OwnerRank.Icon, company.OwnerRank.IconVersion, string.Join(',', company.OwnerRank.Loadout)));

    private static List<string> SplitLoadout(string stored) =>
        stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static WFCompanyPermissions Permissions(Company company, Member member) =>
        member.Profile == company.Owner ? WFCompanyPermissions.All : RankOf(company, member)?.Permissions ?? WFCompanyPermissions.None;

    private static bool Has(Company company, Member member, WFCompanyPermissions permission) =>
        (Permissions(company, member) & permission) == permission;

    private void Track(Task task)
    {
        _pendingWrites.RemoveAll(pending => pending.IsCompleted);
        _pendingWrites.Add(task.ContinueWith(done =>
        {
            if (done.Exception != null)
                _log.Error($"Company write failed: {done.Exception}");
        }, TaskScheduler.Default));
    }

    private void PersistCompany(Company company) => Track(_db.UpdateCompanyAsync(company.Id, company.Name, company.Tag,
        company.Description, company.Colour.ToHex(), company.Owner, company.Cut, company.Recruiting, company.Frozen));

    private void PersistMember(Member member) => Track(_db.UpdateCompanyMemberAsync(member.Profile, member.Rank,
        member.UnpaidSeconds, member.WithdrawnToday, member.WithdrawDay));

    private void PersistRanks(Company company)
    {
        for (var order = 0; order < company.Ranks.Count; order++)
        {
            var rank = company.Ranks[order];
            Track(_db.SaveCompanyRankAsync(new WolfgateCompanyRank
            {
                Id = rank.Id, CompanyId = company.Id, Order = order, Title = rank.Title, Permissions = (int) rank.Permissions,
                Wage = rank.Wage, WithdrawCap = rank.WithdrawCap, BaseJob = rank.BaseJob, Icon = rank.Icon, IconVersion = rank.IconVersion,
                Loadout = string.Join(',', rank.Loadout),
            }));
        }
    }

    /// <summary>Moves credits in or out of the account and records why; false if it would overdraw or the account is frozen.</summary>
    private bool Transact(Company company, WFCompanyLedgerKind kind, long amount, int? actor, string actorName, string note = "",
        bool ignoreFrozen = false)
    {
        if (company.Frozen && !ignoreFrozen || company.Balance + amount < 0)
            return false;
        CountTransaction(company, kind, amount);
        company.Balance += amount;
        var entry = new WolfgateCompanyLedger
        {
            CompanyId = company.Id, Time = DateTime.UtcNow, Kind = (int) kind, Amount = amount, BalanceAfter = company.Balance,
            ActorProfileId = actor, ActorName = actorName.Length > 64 ? actorName[..64] : actorName,
            Note = note.Length > 128 ? note[..128] : note,
        };
        company.Ledger.Insert(0, entry);
        if (company.Ledger.Count > WFStoredCompany.LedgerCache)
            company.Ledger.RemoveAt(company.Ledger.Count - 1);
        Track(_db.RecordCompanyTransactionAsync(entry));
        _adminLog.Add(Shared.Database.LogType.Action, Shared.Database.LogImpact.Low,
            $"Company {company.Name} ({company.Id}) {kind} {amount:+#;-#;0} by {actorName}, balance {company.Balance}{(note.Length > 0 ? $": {note}" : "")}");
        return true;
    }

    private void RemoveCompany(Company company)
    {
        _companies.Remove(company.Id);
        foreach (var profile in company.Members.Keys)
            _memberCompany.Remove(profile);
        Track(_db.DeleteCompanyAsync(company.Id));
    }

    private bool IsOnline(NetUserId user) => _players.TryGetSessionById(user, out var session) && session.Status == SessionStatus.InGame;

    /// <summary>Sends fresh snapshots to everyone a company change concerns.</summary>
    private void Touch(Company? company, params NetUserId[] extra)
    {
        var users = new HashSet<NetUserId>(extra);
        if (company != null)
        {
            users.UnionWith(company.Members.Values.Select(member => member.User));
            users.UnionWith(company.Applications.Values.Select(applicant => applicant.User));
        }
        foreach (var user in users)
        {
            if (_players.TryGetSessionById(user, out var session))
                SendState(session);
        }
    }

    private WFCompanyListing Listing(Company company) => new(company.Id, company.Name, company.Tag, company.Description,
        company.Colour, company.Members.TryGetValue(company.Owner, out var owner) ? owner.Name : string.Empty,
        company.Members.Count, company.Recruiting, company.Logo == null ? 0 : company.LogoVersion, CompanyValue(company),
        company.Ranks.Select(rank => new WFCompanyOpenRank(rank.Id, rank.Title, rank.BaseJob)).ToArray(), OpenRankOf(company), company.Private);

    /// <summary>The open-access rank, if it still exists.</summary>
    private static int? OpenRankOf(Company company) =>
        company.OpenRank is { } open && company.Ranks.Any(rank => rank.Id == open) ? open : null;

    /// <summary>Whole hours the player has played in total, or null until their playtime has loaded.</summary>
    private int? PlayedHours(ICommonSession session)
    {
        if (!_playTime.TryGetTrackerTimes(session, out var times))
            return null;
        _playTime.FlushTracker(session);
        return (int) times.GetValueOrDefault(PlayTimeTrackingShared.TrackerOverall).TotalHours;
    }

    /// <summary>Builds the snapshot for the session's acting character.</summary>
    private void SendState(ICommonSession session, string? message = null)
    {
        if (session.Status != SessionStatus.InGame)
            return;
        var state = new WFCompanyState { Loaded = Loaded, Message = message == null ? null : Loc.GetString(message) };
        if (Loaded && _profileIds.TryGetValue(session.UserId, out var slots))
        {
            state.Characters = slots.Where(pair => CompanyOf(pair.Value) != null).Select(pair =>
            {
                var company = CompanyOf(pair.Value)!;
                var member = company.Members[pair.Value];
                var rank = TitleOf(company, member);
                var iconRank = IconRankOf(company, member);
                return new WFCompanyCharacter(pair.Key, company.Name, company.Tag, rank, company.Id,
                    company.Logo == null ? 0 : company.LogoVersion, iconRank?.Id ?? 0, iconRank?.Icon == null ? 0 : iconRank.IconVersion);
            }).ToArray();
        }
        if (Loaded && CurrentCharacter(session) is { } character)
        {
            var own = CompanyOf(character.ProfileId);
            state.CharacterName = character.Profile.Name;
            state.CharacterBalance = character.Profile.BankBalance;
            state.CreateFee = _config.GetCVar(WFCompanyCVars.CreateFee);
            state.CreateHours = _config.GetCVar(WFCompanyCVars.CreateHours);
            state.PlayedHours = PlayedHours(session);
            state.CanJoin = own == null && !IsIronman(character.Profile);
            state.Directory = _companies.Values.OrderByDescending(company => company.Members.Count)
                .ThenByDescending(CompanyValue).Select(Listing).ToArray();
            state.Invitations = _companies.Values.Where(company => company.Invitations.Contains(character.ProfileId)).Select(Listing).ToArray();
            state.Applications = _companies.Values.Where(company => company.Applications.ContainsKey(character.ProfileId))
                .Select(company => company.Id).ToArray();
            if (own != null)
            {
                state.CanJoinRound = _ticker.RunLevel == Content.Server.GameTicking.GameRunLevel.InRound && !_ticker.DisallowLateJoin &&
                    !_ticker.UserHasJoinedGame(session) && _preferences.GetPreferencesOrNull(session.UserId)?.SelectedCharacterIndex == character.Slot;
                state.Own = Detail(own, own.Members[character.ProfileId]);
                if (Has(own, own.Members[character.ProfileId], WFCompanyPermissions.Invite))
                    state.InviteTargets = InviteTargets(own);
            }
        }
        RaiseNetworkEvent(state, session.Channel);
    }

    private WFCompanyDetail Detail(Company company, Member you)
    {
        var permissions = Permissions(company, you);
        var seesLedger = (permissions & (WFCompanyPermissions.Withdraw | WFCompanyPermissions.EditRanks)) != 0;
        foreach (var member in company.Members.Values.Where(member => IsOnline(member.User)))
        {
            if (_preferences.GetPreferencesOrNull(member.User)?.Characters.GetValueOrDefault(member.Slot) is HumanoidCharacterProfile profile)
                member.Name = profile.Name;
        }
        return new WFCompanyDetail
        {
            Id = company.Id, Name = company.Name, Tag = company.Tag, Description = company.Description, Colour = company.Colour,
            OwnerProfile = company.Owner, OwnerName = company.Members.TryGetValue(company.Owner, out var owner) ? owner.Name : string.Empty,
            Balance = company.Balance, Cut = company.Cut, Recruiting = company.Recruiting, Frozen = company.Frozen,
            ArtLocked = company.ArtLocked, LogoVersion = company.Logo == null ? 0 : company.LogoVersion,
            LaunchFee = company.LaunchFee, OpenRankId = OpenRankOf(company), Private = company.Private, LaunchedThisRound = LaunchedCount(company), Licences = SnapshotLicences(company),
            LicenceOffers = you.Profile == company.Owner ? LicenceOffers(company) : Array.Empty<WFCompanyLicenceOffer>(),
            Ranks = company.Ranks.Select((rank, order) => new WFCompanyRank(rank.Id, order, rank.Title, rank.Permissions, rank.Wage,
                rank.WithdrawCap, rank.BaseJob, rank.Icon == null ? 0 : rank.IconVersion, rank.Loadout.ToArray())).ToArray(),
            OwnerRank = new WFCompanyRank(company.OwnerRank.Id, -1, company.OwnerRank.Title, WFCompanyPermissions.All, 0, 0,
                company.OwnerRank.BaseJob, company.OwnerRank.Icon == null ? 0 : company.OwnerRank.IconVersion, company.OwnerRank.Loadout.ToArray()),
            Members = company.Members.Values.OrderBy(member => RankOrder(company, member) ?? -1).ThenBy(member => member.Name)
                .Select(member => new WFCompanyMember(member.Profile, member.Name, member.Rank,
                    IsOnline(member.User) && CurrentCharacterProfile(member.User) == member.Profile)).ToArray(),
            Applications = (permissions & WFCompanyPermissions.Invite) != 0
                ? company.Applications.Select(pair => new WFCompanyApplicant(pair.Key, pair.Value.Name, pair.Value.RankId, pair.Value.Message)).ToArray()
                : Array.Empty<WFCompanyApplicant>(),
            YourProfile = you.Profile, YourRank = you.Rank, YourPermissions = permissions,
            WithdrawnToday = you.WithdrawDay == DateTime.UtcNow.Date ? you.WithdrawnToday : 0,
            UnpaidSeconds = you.UnpaidSeconds,
            Ledger = company.Ledger.Where(entry => seesLedger || entry.ActorProfileId == you.Profile).Take(WFCompanyPolicy.LedgerPage)
                .Select(entry => new WFCompanyLedgerEntry(entry.Time, (WFCompanyLedgerKind) entry.Kind, entry.Amount, entry.BalanceAfter,
                    entry.ActorName, entry.Note)).ToArray(),
        };
    }

    private int? CurrentCharacterProfile(NetUserId user) =>
        _players.TryGetSessionById(user, out var session) ? CurrentCharacter(session)?.ProfileId : null;

    /// <summary>Online players whose current character could be invited.</summary>
    private WFCompanyPlayer[] InviteTargets(Company company)
    {
        var targets = new List<WFCompanyPlayer>();
        foreach (var session in _players.Sessions)
        {
            if (session.Status == SessionStatus.InGame && CurrentCharacter(session) == null)
                RefreshProfileIdsLater(session.UserId);
            if (session.Status != SessionStatus.InGame || CurrentCharacter(session) is not { } character ||
                CompanyOf(character.ProfileId) != null || IsIronman(character.Profile) || company.Invitations.Contains(character.ProfileId) ||
                HasPlayer(company, session.UserId))
                continue;
            targets.Add(new WFCompanyPlayer(session.UserId, character.Profile.Name));
        }
        return targets.OrderBy(target => target.Name).ToArray();
    }
}
