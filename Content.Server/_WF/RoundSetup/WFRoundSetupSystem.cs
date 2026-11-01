using System.Linq;
using System.Security.Cryptography;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.ShipAccess;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.RoundSetup;

/// <summary>Owns player scenario lobbies and authenticates their membership changes.</summary>
public sealed partial class WFRoundSetupSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private UserDbDataManager _userDb = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private WFCrashScenarioSystem _crash = default!;
    [Dependency] private WFDoomedScenarioSystem _doomed = default!;
    [Dependency] private WFShipScenarioSystem _ships = default!;

    private readonly Dictionary<int, Lobby> _lobbies = new();
    private readonly HashSet<NetUserId> _watchers = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextRequest = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextCodeAttempt = new();
    private TimeSpan _nextUpdate;
    private TimeSpan _nextBroadcast;
    private bool _broadcastPending;
    private int _nextId;

    /// <summary>One player's choices, retained through a temporary disconnection.</summary>
    private sealed class Member
    {
        public string Name = string.Empty;
        public WFCrewPortrait? Portrait;
        public string Title = string.Empty;
        public string? Job;
        public NetEntity? Station;
        public int? RoleId;
        public string? PurchaseVessel;
        public int? PurchaseCharacterSlot;
        public int Contribution;
        public int? PurchaseRevision;
        public bool Ready;
        public bool Spawned;
        public EntityUid? Body;
        public WFShipAccessKey? AccessKey;
        /// <summary>Joined with a character already in the round instead of spawning through the crew.</summary>
        public bool Live;
        /// <summary>Their company rank in the crew's company, kept for when they disconnect.</summary>
        public int RankId;
        /// <summary>Playing their company rank because the crew flies under their company, so they can't pick another job.</summary>
        public bool RankLocked;
    }

    /// <summary>Round-scoped lobby state; private admission data never enters public snapshots.</summary>
    private sealed class Lobby
    {
        public int Id;
        public NetUserId Host;
        public WFRoundScenario Scenario;
        public WFCrewFaction Faction;
        public string ShipName = string.Empty;
        public WFShipStartLocation ShipStart;
        public WFLobbyAccess Access = WFLobbyAccess.Requests;
        public bool AllowLateJoin = true;
        public bool Started;
        public ShipChange? PendingShip;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Description = string.Empty;
        public string? Vessel;
        public int NextRoleId;
        public int PaymentRevision;
        public bool Recruiting;
        public TimeSpan? NextPosting;
        public int? CompanyId;
        /// <summary>The company hangar licence this crew launches, when it is one.</summary>
        public int? HangarLicence;
        public readonly Dictionary<int, LobbyRole> Roles = new();
        public readonly Dictionary<NetUserId, Member> Members = new();
        public readonly Dictionary<NetUserId, int> Requests = new();
        public readonly HashSet<NetUserId> Invitations = new();
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFRoundSetupRequest>(OnRequest);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnReturnedToLobby);
        InitializeShipAccess();
        InitializeCrewSignals();
        InitializeSpawning();
        InitializeDemo();
        InitializeBroadcast();
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateCrewSignals();
        var now = _timing.CurTime;
        if (now >= _nextUpdate)
        {
            _nextUpdate = now + TimeSpan.FromSeconds(2);
            var pruned = PrunePendingAdmissions();
            UpdateDemo();
            SyncCrewShips();
            var locks = SyncCompanyLocks();
            SyncCompanyShips();
            BroadcastIfChanged(pruned | locks | UpdateCrewStatus());
        }
        if (!_broadcastPending || now < _nextBroadcast)
            return;
        _broadcastPending = false;
        _nextBroadcast = now + TimeSpan.FromMilliseconds(250);
        FlushBroadcast();
    }

    /// <summary>Discards round-scoped membership and pending spawn authorization.</summary>
    private void OnRestart(RoundRestartCleanupEvent args)
    {
        _lobbies.Clear();
        _postingCooldowns.Clear();
        SyncCrewSignals();
        SyncCrewShips();
        _savedSetups.Clear();
        _savedReadFailures.Clear();
        _nextRequest.Clear();
        _nextCodeAttempt.Clear();
        _pendingSpawns.Clear();
        _shipHealth.Clear();
        _knownStatuses.Clear();
        ResetDemo();
        Broadcast();
    }

    /// <summary>Respawning releases the old crew so a returning player can choose a fresh start.</summary>
    private void OnReturnedToLobby(PlayerJoinedLobbyEvent args)
    {
        var session = args.PlayerSession;
        if (FindLobby(session.UserId) is not { } lobby || !lobby.Members[session.UserId].Spawned)
            return;
        RemoveMember(lobby, session.UserId);
        SendState(session);
        Broadcast();
    }

    private Lobby? FindLobby(NetUserId user) => _lobbies.Values.FirstOrDefault(l => l.Members.ContainsKey(user));

    /// <summary>Requires loaded lobby status and the global permission to enter the round.</summary>
    private bool CanEnter(ICommonSession session) =>
        session.Status == SessionStatus.InGame &&
        _ticker.PlayerGameStatuses.TryGetValue(session.UserId, out var status) &&
        status != PlayerGameStatus.JoinedGame && _userDb.IsLoadComplete(session) &&
        _preferences.HavePreferencesLoaded(session) &&
        (_ticker.RunLevel == GameRunLevel.PreRoundLobby ||
         _ticker.RunLevel == GameRunLevel.InRound && !_ticker.DisallowLateJoin);

    /// <summary>Authenticates and rate-limits each membership or setup operation.</summary>
    private void OnRequest(WFRoundSetupRequest msg, EntitySessionEventArgs args)
    {
        // Mapping mode has no rounds to set up.
        if (_demoConfig.GetCVar(WFMappingModeCVars.Enabled))
            return;

        var session = args.SenderSession;
        var user = session.UserId;
        // A keep-alive only extends the viewer's lease, so it skips the rate limit and sends nothing back.
        if (msg.Action == WFRoundSetupAction.Watch)
        {
            if (_watchers.Contains(user))
                Watch(user);
            return;
        }
        if (_nextRequest.TryGetValue(user, out var next) && next > _timing.CurTime)
            return;
        _nextRequest[user] = _timing.CurTime + TimeSpan.FromMilliseconds(150);
        Watch(user);
        if (!Enum.IsDefined(msg.Action) || session.Status != SessionStatus.InGame || !_userDb.IsLoadComplete(session))
            return;
        if (msg.Action == WFRoundSetupAction.Refresh)
        {
            // A refresh follows a change of character, which can change the crew's company and everyone's job.
            if (SyncCompanyLocks())
                Broadcast();
            SendState(session);
            return;
        }
        if (_ticker.RunLevel is not (GameRunLevel.PreRoundLobby or GameRunLevel.InRound))
        {
            SendState(session, "wf-round-setup-error-round-ended");
            return;
        }

        var own = FindLobby(user);
        _lobbies.TryGetValue(msg.LobbyId, out var target);
        var error = "wf-round-setup-error-denied";
        var success = false;
        switch (msg.Action)
        {
            case WFRoundSetupAction.LoadSetup when own == null && CanEnter(session):
                error = "wf-round-setup-saved-invalid";
                success = LoadSetup(session, msg.SavedId);
                break;
            case WFRoundSetupAction.DeleteSetup:
                error = "wf-round-setup-save-failed";
                success = DeleteSetup(user, msg.SavedId);
                break;
            case WFRoundSetupAction.SaveSetup when own != null && own.Id == msg.LobbyId && own.Host == user && !WFRoundSetupPolicy.IsFormed(own.Scenario) && own.HangarLicence == null:
                error = "wf-round-setup-save-failed";
                success = (msg.Title == null || HostRequest(own, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
                    { Title = msg.Title, Description = msg.Description, Access = msg.Access, AllowLateJoin = msg.AllowLateJoin }, session)) && SaveSetup(own, session, msg.SaveName);
                if (success)
                {
                    SendState(session, "wf-round-setup-save-success");
                    Broadcast();
                    return;
                }
                break;
            case WFRoundSetupAction.SetTitle when own != null && own.Id == msg.LobbyId && own.Host == user && !own.Members[user].Spawned:
                error = "wf-round-setup-error-role";
                success = SetPersonalTitle(own, user, msg.RoleName);
                break;
            case WFRoundSetupAction.Create when own == null && msg.Scenario == WFRoundScenario.Formed && CanJoinLive(session):
                if (_lobbies.Count >= 64)
                    break;
                if (!WFRoundSetupPolicy.TryNormalizeTitle(msg.Title, out var formedTitle))
                {
                    error = "wf-round-setup-error-title";
                    break;
                }
                if (!WFRoundSetupPolicy.TryNormalizeDescription(msg.Description, out var formedDescription))
                {
                    error = "wf-round-setup-error-description";
                    break;
                }
                success = CreateFormed(session, formedTitle, formedDescription);
                if (success && FindLobby(user) is { } formed && msg.CompanyId is { } formedCompany && _companies.CanFieldCrew(session, formedCompany))
                    formed.CompanyId = formedCompany;
                break;
            case WFRoundSetupAction.Create when own == null && CanEnter(session):
                if (!IsScenarioAvailable(msg.Scenario) || _lobbies.Count >= 64)
                    break;
                if (!WFRoundSetupPolicy.TryNormalizeTitle(msg.Title, out var title))
                {
                    error = "wf-round-setup-error-title";
                    break;
                }
                if (!WFRoundSetupPolicy.TryNormalizeDescription(msg.Description, out var description))
                {
                    error = "wf-round-setup-error-description";
                    break;
                }
                if (msg.Scenario == WFRoundScenario.FactionShip && !CanCreateFaction(session, msg.Faction))
                {
                    error = "wf-round-setup-faction-unavailable";
                    break;
                }
                var created = new Lobby { Id = ++_nextId, Host = user, Scenario = msg.Scenario,
                    Faction = msg.Scenario == WFRoundScenario.FactionShip ? msg.Faction : WFCrewFaction.None,
                    Code = NewCode(), Title = title, Description = description };
                _lobbies.Add(created.Id, created);
                RegisterMember(created, session, new Member { Name = session.Name });
                if (msg.CompanyId is { } createdCompany && _companies.CanFieldCrew(session, createdCompany))
                    created.CompanyId = createdCompany;
                success = true;
                break;
            case WFRoundSetupAction.Join when own == null && target != null && (CanEnter(session) || CanJoinLive(session)):
                if (_nextCodeAttempt.TryGetValue(user, out var codeNext) && codeNext > _timing.CurTime)
                    break;
                _nextCodeAttempt[user] = _timing.CurTime + TimeSpan.FromSeconds(1);
                var validCode = WFRoundSetupPolicy.TryNormalizeCode(msg.Code, out var code) && code == target.Code;
                if (!CanAdmit(target, session))
                {
                    error = CanEnter(session) ? "wf-round-setup-formed-no-ship" : "wf-round-setup-live-unavailable";
                    break;
                }
                // A company member may join as their company rank instead of taking an opening.
                var asRank = msg.AsRank && JoinsAsRank(target, session) || LockedRank(target, session) != null;
                LobbyRole requestedRole = default!;
                if (!asRank && (CanEnter(session) ? !TryGetRole(target, session, msg.RoleId, out requestedRole) : !TryGetLiveRole(target, msg.RoleId, out requestedRole)))
                {
                    error = "wf-round-setup-error-role";
                    break;
                }
                var decision = Admission(target, false, validCode, InCrewCompany(target, session));
                if (decision == WFLobbyJoinDecision.Join)
                {
                    success = AddMember(target, session, asRank ? RankRole : msg.RoleId);
                }
                else if (decision == WFLobbyJoinDecision.Request && target.Requests.Count < WFRoundSetupPolicy.MaximumMembers)
                {
                    target.Requests[user] = asRank ? RankRole : requestedRole.Id;
                    SendState(session, "wf-round-setup-request-sent");
                    Broadcast();
                    return;
                }
                break;
            case WFRoundSetupAction.AcceptInvite when own == null && target != null && CanAdmit(target, session):
                if (target.Invitations.Contains(user) && Admission(target, true, false) == WFLobbyJoinDecision.Join)
                {
                    success = AddMember(target, session, msg.RoleId);
                }
                break;
            case WFRoundSetupAction.DeclineInvite when target != null:
                success = target.Invitations.Remove(user);
                break;
            case WFRoundSetupAction.Leave when own != null && msg.LobbyId == own.Id:
                RemoveMember(own, user);
                success = true;
                break;
            case WFRoundSetupAction.AssignShip when own != null && msg.LobbyId == own.Id:
                success = RequestCrewShip(own, session, msg.Target, msg.Ship, out error);
                break;
            case WFRoundSetupAction.ApproveShip when own != null && msg.LobbyId == own.Id:
                success = ApproveCrewShip(own, session, msg.Ship, msg.ShipChangeId, out error);
                break;
            case WFRoundSetupAction.CancelShip when own != null && msg.LobbyId == own.Id &&
                own.PendingShip is { } pending && msg.ShipChangeId == pending.Id && !TerminatingOrDeleted(pending.Ship) && msg.Ship == GetNetEntity(pending.Ship) &&
                (own.Host == user || pending.Owner == user):
                own.PendingShip = null;
                success = true;
                break;
            case WFRoundSetupAction.SelectJob when own != null && msg.LobbyId == own.Id && !own.Members[user].Spawned && CanEnter(session):
                error = "wf-round-setup-error-role";
                success = SelectRoleByJob(own, session, msg.Job, msg.Station);
                break;
            case WFRoundSetupAction.SelectRole when own != null && msg.LobbyId == own.Id && !own.Members[user].Spawned && CanEnter(session):
                error = "wf-round-setup-error-role";
                success = SelectRole(own, session, msg.RoleId);
                break;
            case WFRoundSetupAction.SetReady when own != null && msg.LobbyId == own.Id && !own.Members[user].Spawned && CanEnter(session):
                success = (!msg.Ready || ValidatePaymentQuote(own, session, msg.PaymentRevision, out error)) &&
                    SetMemberReady(own, session, msg.Ready, out error);
                break;
            case WFRoundSetupAction.ConfirmJoin when own != null && msg.LobbyId == own.Id && !own.Members[user].Spawned && CanEnter(session):
                if (own.Host == user && msg.Title != null && !HostRequest(own, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
                    { Title = msg.Title, Description = msg.Description, Access = msg.Access, AllowLateJoin = msg.AllowLateJoin }, session))
                    break;
                if (own.Host == user && msg.RoleName != null && !SetPersonalTitle(own, user, msg.RoleName))
                    break;
                success = ValidatePaymentQuote(own, session, msg.PaymentRevision, out error) &&
                    SetMemberReady(own, session, true, out error);
                if (success && _ticker.RunLevel == GameRunLevel.InRound && (own.Started || own.Host == user && AllPaymentsApproved(own)))
                {
                    success = Launch(own, session, out error);
                    if (!success)
                        UnreadyMember(user, own.Members[user]);
                }
                break;
            case WFRoundSetupAction.Launch when own != null && msg.LobbyId == own.Id && CanEnter(session):
                success = Launch(own, session, out error);
                break;
            case WFRoundSetupAction.Post when own != null && msg.LobbyId == own.Id && own.Host == user:
                success = PostCrew(own, session, msg.Posting, out error);
                if (success)
                {
                    SendState(session, "wf-round-setup-posting-sent");
                    Broadcast();
                    return;
                }
                break;
            default:
                if (own != null && own.Host == user && msg.LobbyId == own.Id)
                    success = HostRequest(own, msg, session);
                break;
        }
        if (!success)
            SendState(session, error);
        else
        {
            SyncCompanyLocks();
            SendState(session, joinConfirmed: msg.Action is WFRoundSetupAction.ConfirmJoin or WFRoundSetupAction.Launch);
            Broadcast();
        }
    }

    /// <summary>Validates personal choices and records explicit purchase consent with readiness.</summary>
    private bool SetMemberReady(Lobby lobby, ICommonSession session, bool ready, out string error)
    {
        error = "wf-round-setup-error-job";
        var member = lobby.Members[session.UserId];
        var wasReady = IsReadyForLobby(lobby, session.UserId, member);
        if (!ready)
        {
            UnreadyMember(session.UserId, member);
            return true;
        }
        if (WFRoundSetupPolicy.IsShip(lobby.Scenario) && lobby.Host == session.UserId && lobby.Vessel == null)
        {
            error = "wf-round-setup-error-ship-choice";
            return false;
        }
        if (!CanSpawnRole(lobby, session))
            return false;
        if (WFRoundSetupPolicy.IsShip(lobby.Scenario) && !lobby.Started)
        {
            if (!ValidatePaymentFunds(lobby, session, out error))
                return false;
            member.PurchaseRevision = lobby.PaymentRevision;
            member.PurchaseVessel = lobby.Vessel;
            member.PurchaseCharacterSlot = _preferences.GetPreferences(session.UserId).SelectedCharacterIndex;
        }
        if (_ticker.RunLevel == GameRunLevel.PreRoundLobby)
        {
            _ticker.ToggleReady(session, true);
            if (!IsReady(session.UserId, member))
            {
                UnreadyMember(session.UserId, member);
                error = lobby.Scenario == WFRoundScenario.FactionShip ? "wf-round-setup-faction-balance" : error;
                return false;
            }
        }
        else
            member.Ready = true;
        if (!wasReady && !lobby.Started && WFRoundSetupPolicy.IsShip(lobby.Scenario) && lobby.Host != session.UserId &&
            _ticker.RunLevel == GameRunLevel.InRound && AllPaymentsApproved(lobby))
            NotifyCrew(lobby, Loc.GetString("wf-round-setup-payment-ready"));
        return true;
    }

    private WFLobbyJoinDecision Admission(Lobby lobby, bool invited, bool code, bool companyMember = false) =>
        WFRoundSetupPolicy.EvaluateJoin(lobby.Access, lobby.Started || _ticker.RunLevel == GameRunLevel.InRound,
            lobby.AllowLateJoin || !lobby.Started, lobby.Members.Count >= WFRoundSetupPolicy.MaximumMembers, invited, code, companyMember);

    /// <summary>Restricts admission and ownership changes to the authenticated host.</summary>
    private bool HostRequest(Lobby lobby, WFRoundSetupRequest msg, ICommonSession session)
    {
        switch (msg.Action)
        {
            case WFRoundSetupAction.Configure:
                if (!Enum.IsDefined(msg.Access) || WFRoundSetupPolicy.IsCompanyAccess(msg.Access) && lobby.CompanyId == null ||
                    !WFRoundSetupPolicy.TryNormalizeTitle(msg.Title ?? lobby.Title, out var title) ||
                    !WFRoundSetupPolicy.TryNormalizeDescription(msg.Description ?? lobby.Description, out var description))
                    return false;
                if (lobby.Access != msg.Access)
                {
                    lobby.Code = NewCode();
                    lobby.Requests.Clear();
                }
                lobby.Title = title;
                lobby.Description = description;
                lobby.Access = msg.Access;
                lobby.AllowLateJoin = msg.AllowLateJoin;
                return true;
            case WFRoundSetupAction.RegenerateCode:
                lobby.Code = NewCode();
                return true;
            case WFRoundSetupAction.SetRecruiting:
                lobby.Recruiting = msg.Recruiting;
                return true;
            case WFRoundSetupAction.SetCompany:
                return lobby.HangarLicence == null && SetCrewCompany(lobby, session, msg.CompanyId);
            case WFRoundSetupAction.ChangeScenario:
                if (lobby.Started || lobby.HangarLicence != null || !IsScenarioAvailable(msg.Scenario))
                    return false;
                if (msg.Scenario == WFRoundScenario.FactionShip && !CanCreateFaction(session, msg.Faction))
                    return false;
                var faction = msg.Scenario == WFRoundScenario.FactionShip ? msg.Faction : WFCrewFaction.None;
                if (lobby.Scenario == msg.Scenario && lobby.Faction == faction)
                    return true;
                _crash.RemoveScenario(lobby.Id);
                _doomed.RemoveScenario(lobby.Id);
                _ships.RemoveScenario(lobby.Id);
                lobby.Scenario = msg.Scenario;
                lobby.Faction = faction;
                lobby.Vessel = null;
                lobby.ShipStart = WFShipStartLocation.RandomSpace;
                if (lobby.Scenario == WFRoundScenario.FactionShip)
                {
                    foreach (var entry in lobby.Members.Values)
                        entry.Title = string.Empty;
                    foreach (var entry in lobby.Roles.Values)
                        entry.Name = string.Empty;
                }
                var allowed = ScenarioJobs(lobby.Scenario, lobby.Faction);
                foreach (var affected in lobby.Members.Values.Where(entry => !entry.Spawned && entry.Job != null && !allowed.Contains(entry.Job)))
                {
                    affected.RoleId = null;
                    affected.Job = null;
                    affected.Station = null;
                }
                foreach (var role in lobby.Roles.Values.Where(role => !allowed.Contains(role.Job)).Select(role => role.Id).ToArray())
                    RemoveRole(lobby, role);
                ReconcileFactionRoles(lobby);
                ResetContributions(lobby);
                return true;
            case WFRoundSetupAction.SetShipName:
                if (lobby.Started || !WFRoundSetupPolicy.IsShip(lobby.Scenario) ||
                    !WFRoundSetupPolicy.TryNormalizeTitle(msg.ShipName, out var shipName))
                    return false;
                lobby.ShipName = shipName;
                if (lobby.HangarLicence is { } namedLicence)
                    _companies.RememberShipName(namedLicence, shipName);
                return true;
            case WFRoundSetupAction.SelectShipStart:
                if (lobby.Started || !WFRoundSetupPolicy.IsShip(lobby.Scenario) || !WFRoundSetupPolicy.IsShipStartAllowed(msg.ShipStart, lobby.Faction))
                    return false;
                if (lobby.ShipStart == msg.ShipStart)
                    return true;
                _ships.RemoveScenario(lobby.Id);
                lobby.ShipStart = msg.ShipStart;
                UnreadyMembers(lobby);
                return true;
            case WFRoundSetupAction.SetContribution:
                return SetContribution(lobby, msg);
            case WFRoundSetupAction.SelectVessel:
                if (lobby.Started || lobby.HangarLicence != null || !WFRoundSetupPolicy.IsShip(lobby.Scenario) || msg.Vessel == null || !CanBuyVessel(lobby, session, msg.Vessel))
                    return false;
                // Re-confirming the same ship keeps the payment split but still revokes purchase consent.
                if (lobby.Vessel == msg.Vessel)
                {
                    UnreadyMembers(lobby);
                    return true;
                }
                lobby.Vessel = msg.Vessel;
                ResetContributions(lobby);
                return true;
            case WFRoundSetupAction.ApplyRoster:
                return ApplyRoster(lobby, msg.Roster);
            case WFRoundSetupAction.UpsertRole:
                return UpsertRole(lobby, session, msg);
            case WFRoundSetupAction.RemoveRole:
                return RemoveRole(lobby, msg.RoleId);
            case WFRoundSetupAction.RejectRequest when msg.Target is { } rejected:
                return lobby.Requests.TryGetValue(rejected, out var rejectedRole) &&
                    (msg.RoleId == null || msg.RoleId == rejectedRole) && lobby.Requests.Remove(rejected);
            case WFRoundSetupAction.AcceptRequest when msg.Target is { } accepted:
                if (!lobby.Requests.TryGetValue(accepted, out var requestedRole) ||
                    msg.RoleId != null && msg.RoleId != requestedRole || !WFRoundSetupPolicy.TakesRequests(lobby.Access) ||
                    Admission(lobby, true, false) != WFLobbyJoinDecision.Join ||
                    FindLobby(accepted) != null || !_players.TryGetSessionById(accepted, out var requester) || !CanAdmit(lobby, requester))
                    return false;
                return AddMember(lobby, requester, requestedRole);
            case WFRoundSetupAction.Invite when msg.Target is { } invited:
                if (lobby.Invitations.Count >= WFRoundSetupPolicy.MaximumMembers || FindLobby(invited) != null ||
                    !_players.TryGetSessionById(invited, out var invitee) || !CanAdmit(lobby, invitee) ||
                    Admission(lobby, true, false) != WFLobbyJoinDecision.Join)
                    return false;
                lobby.Invitations.Add(invited);
                _watchers.Add(invited);
                SendState(invitee, "wf-round-setup-invited");
                return true;
            case WFRoundSetupAction.Kick when msg.Target is { } kicked && kicked != lobby.Host:
                if (!lobby.Members.TryGetValue(kicked, out var member) || member.Spawned && !member.Live)
                    return false;
                RemoveMember(lobby, kicked);
                return true;
            case WFRoundSetupAction.TransferHost when msg.Target is { } host:
                if (!lobby.Members.ContainsKey(host) || !_players.TryGetSessionById(host, out _) || !CanLeadFaction(lobby, host))
                    return false;
                lobby.PendingShip = null;
                lobby.Host = host;
                ReconcileFactionRoles(lobby);
                lobby.Code = NewCode();
                RequireHostConsent(lobby);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Grants membership and clears competing requests and invitations.</summary>
    private bool AddMember(Lobby lobby, ICommonSession session, int? roleId)
    {
        if (roleId == RankRole || LockedRank(lobby, session) != null)
            return AddRankMember(lobby, session);
        if (!CanEnter(session))
            return AddLiveMember(lobby, session, roleId);
        if (FindLobby(session.UserId) != null || lobby.Members.Count >= WFRoundSetupPolicy.MaximumMembers ||
            !TryGetRole(lobby, session, roleId, out var role))
            return false;
        RegisterMember(lobby, session, new Member { Name = session.Name, RoleId = role.Id, Job = role.Job, Station = role.Station });
        NotifyCrewJoined(lobby, session.UserId);
        return true;
    }

    /// <summary>Registers validated membership and resets any competing admission or readiness state.</summary>
    private void RegisterMember(Lobby lobby, ICommonSession session, Member member)
    {
        UpdatePortrait(session.UserId, member);
        lobby.Members.Add(session.UserId, member);
        foreach (var other in _lobbies.Values)
        {
            other.Requests.Remove(session.UserId);
            other.Invitations.Remove(session.UserId);
        }
        _watchers.Add(session.UserId);
        UnreadyMember(session.UserId, member);
    }

    /// <summary>Releases membership and preserves host ownership for remaining members.</summary>
    private void RemoveMember(Lobby lobby, NetUserId user)
    {
        if (lobby.PendingShip is { } pending && (pending.Host == user || pending.Owner == user))
            lobby.PendingShip = null;
        var hadContribution = lobby.Members[user].Contribution > 0;
        lobby.Members.Remove(user);
        if (_players.TryGetSessionById(user, out var session) && _ticker.RunLevel == GameRunLevel.PreRoundLobby)
            _ticker.ToggleReady(session, false);
        if (lobby.Members.Count == 0)
        {
            if (lobby.NextPosting != null)
                RaiseNetworkEvent(new WFCrewPostingRevokedEvent(lobby.Id));
            if (lobby.HangarLicence is { } licence && !lobby.Started)
                _companies.ReleaseLicence(licence);
            _lobbies.Remove(lobby.Id);
            _crash.RemoveScenario(lobby.Id);
            _doomed.RemoveScenario(lobby.Id);
            _ships.RemoveScenario(lobby.Id);
        }
        else if (lobby.Host == user)
        {
            lobby.Host = lobby.Members.Keys.OrderByDescending(id => WFFactionCrewPolicy.Rank(lobby.Faction, lobby.Members[id].Job))
                .ThenByDescending(id => _players.TryGetSessionById(id, out _)).First();
            ReconcileFactionRoles(lobby);
            lobby.Code = NewCode();
            RequireHostConsent(lobby);
        }
        if (hadContribution && lobby.Members.Count > 0 && !lobby.Started)
            ResetContributions(lobby);
        SyncCrewShips();
    }

    /// <summary>Generates unpredictable admission codes without ambiguous characters.</summary>
    private static string NewCode()
    {
        var chars = new char[WFRoundSetupPolicy.MaximumCodeLength];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = WFRoundSetupPolicy.CodeAlphabet[RandomNumberGenerator.GetInt32(WFRoundSetupPolicy.CodeAlphabet.Length)];
        return new string(chars);
    }

    private bool IsReady(NetUserId user, Member member) => _ticker.RunLevel == GameRunLevel.PreRoundLobby
        ? _ticker.PlayerGameStatuses.GetValueOrDefault(user) == PlayerGameStatus.ReadyToPlay
        : member.Ready;

    /// <summary>Public listings use the selected character name, never the account identifier.</summary>
    private string CharacterName(NetUserId user) =>
        _preferences.GetPreferencesOrNull(user)?.SelectedCharacter?.Name ?? Loc.GetString("wf-round-setup-unknown-character");

    /// <summary>Validates a crew-local personal title independently of the underlying job.</summary>
    private bool SetPersonalTitle(Lobby lobby, NetUserId user, string? input)
    {
        if (!WFRoundSetupPolicy.TryNormalizeRoleName(input, out var title) ||
            lobby.Scenario == WFRoundScenario.FactionShip && title.Length != 0)
            return false;
        var member = lobby.Members[user];
        if (member.Title == title || member.RankLocked)
            return true;
        member.Title = title;
        UnreadyMember(user, member);
        return true;
    }

    /// <summary>Snapshots visible appearance without sending preferences, traits or account data.</summary>
    private void UpdatePortrait(NetUserId user, Member member)
    {
        if (!member.Spawned && _preferences.GetPreferencesOrNull(user)?.SelectedCharacter is Content.Shared.Preferences.HumanoidCharacterProfile profile)
        {
            member.Portrait = new WFCrewPortrait(profile.Name, profile.Species.Id, profile.Sex, profile.Appearance.Clone());
            member.Name = profile.Name;
        }
        if (member.Portrait == null && !member.Spawned)
            member.Name = Loc.GetString("wf-round-setup-unknown-character");
    }

    /// <summary>Refreshes character snapshots once before delivery to any number of recipients.</summary>
    private void RefreshPortraits()
    {
        foreach (var lobby in _lobbies.Values)
        foreach (var (id, member) in lobby.Members)
            UpdatePortrait(id, member);
    }

    /// <summary>Includes private member and admission details only for authorized recipients.</summary>
    private WFRoundSetupLobby Snapshot(Lobby lobby, NetUserId user, bool canEnter, bool canJoinLive, IReadOnlyCollection<WFRoundSetupJob> jobs)
    {
        var member = lobby.Members.ContainsKey(user);
        var host = lobby.Host == user;
        return new WFRoundSetupLobby
        {
            Id = lobby.Id, Host = lobby.Host, HostName = lobby.Members[lobby.Host].Name, Title = lobby.Title, Description = lobby.Description,
            HostJob = lobby.Members[lobby.Host].Job, Portrait = lobby.Members[lobby.Host].Portrait,
            HostTitle = RosterTitle(lobby, lobby.Host, lobby.Members[lobby.Host]), HostRankId = CrewRankId(lobby, lobby.Host, lobby.Members[lobby.Host]),
            Scenario = lobby.Scenario, Faction = lobby.Faction, ShipName = lobby.ShipName, Access = lobby.Access, AllowLateJoin = lobby.AllowLateJoin,
            CurrentShip = lobby.Started ? CurrentShip(lobby) : null, ShipCondition = ShipCondition(lobby),
            Started = lobby.Started, MemberCount = lobby.Members.Count, Vessel = lobby.Vessel, ShipStart = lobby.ShipStart,
            Roster = lobby.Members.Select(pair => new WFRoundSetupCrewmate(pair.Value.Name, pair.Value.Job,
                RosterTitle(lobby, pair.Key, pair.Value),
                IsReadyForLobby(lobby, pair.Key, pair.Value), pair.Value.Spawned, MemberStatus(pair.Value), CrewRankId(lobby, pair.Key, pair.Value))).ToArray(),
            Roles = SnapshotRoles(lobby, user, canEnter && (!WFRoundSetupPolicy.IsFormed(lobby.Scenario) || _ships.HasAssignedShip(lobby.Id)), canJoinLive && WFRoundSetupPolicy.AllowsLiveMembers(lobby.Scenario, lobby.Started), jobs),
            Recruiting = ShowsRecruiting(lobby),
            LiveJoin = WFRoundSetupPolicy.AllowsLiveMembers(lobby.Scenario, lobby.Started),
            RecruitingOn = host && lobby.Recruiting,
            Hangar = lobby.HangarLicence != null,
            CompanyId = CrewCompany(lobby) != null ? lobby.CompanyId : null,
            CompanyName = CrewCompany(lobby)?.Name ?? string.Empty,
            CompanyTag = CrewCompany(lobby)?.Tag ?? string.Empty,
            CompanyColour = CrewCompany(lobby)?.Colour ?? Color.White,
            NextPosting = host ? lobby.NextPosting : null,
            Code = host && lobby.Access == WFLobbyAccess.Code ? lobby.Code : null,
            Members = member ? lobby.Members.Select(pair => new WFRoundSetupMember(pair.Key, pair.Value.Name,
                pair.Value.Job, pair.Value.Station, IsReadyForLobby(lobby, pair.Key, pair.Value), pair.Value.Spawned,
                _players.TryGetSessionById(pair.Key, out _), pair.Value.RoleId, pair.Value.Title, pair.Value.Contribution, MemberStatus(pair.Value), pair.Value.Live, CrewRankId(lobby, pair.Key, pair.Value), pair.Value.RankLocked)).ToArray() : Array.Empty<WFRoundSetupMember>(),
            Requests = host ? lobby.Requests.Keys.Where(id => _players.TryGetSessionById(id, out _))
                .Select(id => new WFRoundSetupPlayer(id, CharacterName(id), lobby.Requests[id])).ToArray() : Array.Empty<WFRoundSetupPlayer>(),
        };
    }

    /// <summary>Combines public listings with the recipient's permitted choices and invitations.</summary>
    private void SendState(ICommonSession session, string? message = null, bool joinConfirmed = false, bool refreshPortraits = true)
    {
        if (session.Status != SessionStatus.InGame)
            return;
        var own = FindLobby(session.UserId);
        var shipHost = own != null && WFRoundSetupPolicy.IsShip(own.Scenario) && own.Host == session.UserId;
        if (refreshPortraits)
            RefreshPortraits();
        var canEnter = CanEnter(session);
        var canJoinLive = own == null && CanJoinLive(session);
        var eligibility = new Dictionary<string, bool>();
        var scenarioJobs = new Dictionary<WFRoundScenario, List<WFRoundSetupJob>>();
        List<WFRoundSetupJob> JobsFor(Lobby lobby)
        {
            if (lobby.Scenario is WFRoundScenario.Normal or WFRoundScenario.FactionShip)
                return GetJobs(session, lobby, eligibility);
            if (!scenarioJobs.TryGetValue(lobby.Scenario, out var jobs))
                scenarioJobs[lobby.Scenario] = jobs = GetJobs(session, lobby, eligibility);
            return jobs;
        }
        WFRoundSetupLobby ForRecipient(Lobby lobby)
        {
            var snapshot = Snapshot(lobby, session.UserId, canEnter, canJoinLive, canEnter ? JobsFor(lobby) : Array.Empty<WFRoundSetupJob>());
            snapshot.CanJoinAsRank = JoinsAsRank(lobby, session);
            return snapshot;
        }
        var state = new WFRoundSetupState
        {
            InRound = _ticker.RunLevel != GameRunLevel.PreRoundLobby,
            JoinConfirmed = joinConfirmed,
            CanJoin = canEnter, CanJoinLive = canJoinLive, RecruitingStations = RecruitingStations(),
            OwnCompany = _companies.CompanyIdOf(session),
            CompanyRank = _companies.RankFor(session)?.Title,
            FieldCompanies = own == null || own.Host == session.UserId ? FieldCompanies(session) : Array.Empty<WFRoundSetupCompany>(), CrashlandedAvailable = _crash.IsAvailable, DoomedAvailable = _doomed.IsAvailable, OwnLobby = own?.Id,
            // A launched crew whose members are all dead, gone or in cryosleep drops off everyone else's list.
            Lobbies = _lobbies.Values.Where(lobby => lobby == own || !lobby.Started || HasActiveMembers(lobby)).Select(ForRecipient).ToArray(),
            Invitations = _lobbies.Values.Where(l => l.Invitations.Contains(session.UserId))
                .Select(ForRecipient).ToArray(),
            InviteTargets = own?.Host == session.UserId ? _players.Sessions.Where(s => FindLobby(s.UserId) == null && CanAdmit(own, s))
                .Select(s => new WFRoundSetupPlayer(s.UserId, CharacterName(s.UserId))).ToArray() : Array.Empty<WFRoundSetupPlayer>(),
            Jobs = own != null && (canEnter || own.Host == session.UserId) ? JobsFor(own).ToArray() : Array.Empty<WFRoundSetupJob>(),
            SavedSetups = GetSavedSetups(session.UserId).ToArray(),
            RoleJobs = own?.Host == session.UserId ? (WFRoundSetupPolicy.IsShip(own.Scenario) || WFRoundSetupPolicy.IsFormed(own.Scenario) ? GetRoleJobs(session, own) : JobsFor(own)).ToArray() : Array.Empty<WFRoundSetupJob>(),
            Factions = WFFactionCrewPolicy.All.Where(faction => CanCreateFaction(session, faction, eligibility)).ToArray(),
            Vessels = !shipHost ? Array.Empty<WFRoundSetupVessel>() : own!.HangarLicence != null ? HangarVessels(own)
                : _ships.GetVessels(own.Faction, EligiblePurchaseJob(own, session)).ToArray(),
            CrewShips = own is { Started: true } && own.Host == session.UserId ? CrewShipChoices(own) : Array.Empty<WFRoundSetupShipChoice>(),
            PendingShip = own != null ? PendingShipFor(own, session.UserId) : null,
            HostBalance = own != null ? _ships.GetBalance(session) : null,
            Payment = own != null ? PaymentFor(own, session.UserId) : null,
            Message = message == null ? null : Loc.GetString(message),
        };
        RaiseNetworkEvent(state, session.Channel);
    }

    /// <summary>Expired requests and invitations must not consume the bounded admission queues.</summary>
    private bool PrunePendingAdmissions()
    {
        var changed = false;
        foreach (var lobby in _lobbies.Values)
        {
            if (lobby.PendingShip is { } pending && !ValidShipChange(lobby, pending))
            {
                lobby.PendingShip = null;
                changed = true;
            }
            foreach (var user in lobby.Requests.Keys.Where(user => IsUnavailable(lobby, user)).ToArray())
                changed |= lobby.Requests.Remove(user);
            changed |= lobby.Invitations.RemoveWhere(user => IsUnavailable(lobby, user)) > 0;
        }
        return changed;

        bool IsUnavailable(Lobby lobby, NetUserId user) =>
            !_players.TryGetSessionById(user, out var session) || !CanAdmit(lobby, session) || FindLobby(user) != null;
    }

    /// <summary>Coalesces mutations into a bounded stream of snapshots for other viewers.</summary>
    private void Broadcast() => _broadcastPending = true;
}
