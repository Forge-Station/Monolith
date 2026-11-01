using System.Linq;
using Content.Server._NF.Station.Components;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Antag;
using Content.Server.Maps;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._WF.RoundSetup;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Preferences;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _components = default!;
    [Dependency] private IBanManager _bans = default!;
    [Dependency] private IGameMapManager _gameMaps = default!;
    [Dependency] private StationJobsSystem _jobs = default!;
    [Dependency] private StationSpawningSystem _spawning = default!;

    private readonly Dictionary<NetUserId, (EntityUid Station, EntityCoordinates? Coordinates)> _pendingSpawns = new();

    /// <summary>Reserves scenario players before ordinary allocation and chooses their spawn coordinates.</summary>
    private void InitializeSpawning()
    {
        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnRoundSpawning, before: new[] { typeof(AntagSelectionSystem) });
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning,
            before: new[] { typeof(ContainerSpawnPointSystem), typeof(SpawnPointSystem) });
    }

    /// <summary>Only the synchronous, validated scenario spawn may enter a private scenario station.</summary>
    public bool CanSpawnOnStation(ICommonSession session, EntityUid station)
    {
        if (_pendingSpawns.TryGetValue(session.UserId, out var pending))
            return pending.Station == station;
        return IsStandardStation(station) && FindLobby(session.UserId) == null;
    }

    /// <summary>Ordinary joining covers every station and player ship except private scenario stations.</summary>
    public bool IsStandardStation(EntityUid station) => !IsScenarioStation(station);

    /// <summary>Player-owned ships recruit through ordinary late join, never through scenario allocation.</summary>
    private bool IsPlayerShip(EntityUid station) => HasComp<ExtraShuttleInformationComponent>(station);

    /// <summary>Private scenario stations never enter the ordinary station picker.</summary>
    private bool IsScenarioStation(EntityUid station) =>
        HasComp<WFCrashScenarioStationComponent>(station) || HasComp<WFShipScenarioStationComponent>(station) ||
        HasComp<WFDoomedScenarioStationComponent>(station);

    /// <summary>Rechecks role bans and character requirements before offering or spawning a job.</summary>
    private bool IsJobAllowed(ICommonSession session, string job, Dictionary<string, bool>? eligibility = null)
    {
        if (eligibility != null)
        {
            if (!eligibility.TryGetValue(job, out var cached))
                eligibility[job] = cached = IsJobAllowed(session, job);
            return cached;
        }
        if (!_prototypes.HasIndex<JobPrototype>(job) || !_userDb.IsLoadComplete(session))
            return false;
        var bans = _bans.GetJobBans(session.UserId);
        if (bans == null || bans.Contains(job))
            return false;
        var allowed = new IsJobAllowedEvent(session, new ProtoId<JobPrototype>(job));
        RaiseLocalEvent(ref allowed);
        return !allowed.Cancelled;
    }

    /// <summary>Offers only eligible jobs from the selected scenario or available stations.</summary>
    private List<WFRoundSetupJob> GetJobs(ICommonSession session, Lobby lobby, Dictionary<string, bool>? eligibility = null)
    {
        var choices = new List<WFRoundSetupJob>();
        if (!_userDb.IsLoadComplete(session) || !_preferences.HavePreferencesLoaded(session))
            return choices;
        if (lobby.Scenario != WFRoundScenario.Normal)
        {
            foreach (var job in ScenarioJobs(lobby.Scenario, lobby.Faction))
            {
                if (lobby.Scenario == WFRoundScenario.FactionShip &&
                    (!IsFactionJob(lobby.Faction, job) || !FactionJobAvailable(job) ||
                     lobby.Host != session.UserId && !WFFactionCrewPolicy.CanRecruit(lobby.Faction, lobby.Members[lobby.Host].Job, job)))
                    continue;
                if (IsJobAllowed(session, job, eligibility))
                    choices.Add(new WFRoundSetupJob(job, null, string.Empty));
            }
            return choices;
        }
        // Before maps load, the selected map still describes its starting jobs.
        if (_ticker.RunLevel == GameRunLevel.PreRoundLobby && _gameMaps.GetSelectedMap() is { } map)
        {
            var ids = new HashSet<string>();
            foreach (var station in map.Stations.Values)
            {
                if (!station.StationComponentOverrides.TryGetComponent<StationJobsComponent>(_components, out var jobs))
                    continue;
                foreach (var (job, slots) in jobs.SetupAvailableJobs)
                {
                    if (slots.Length > 0 && slots[0] != 0 && ids.Add(job.Id) && IsJobAllowed(session, job.Id, eligibility))
                        choices.Add(new WFRoundSetupJob(job.Id, null, map.MapName));
                }
            }
            return choices;
        }
        var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
        while (query.MoveNext(out var station, out _, out _))
        {
            if (!IsStandardStation(station))
                continue;
            foreach (var job in _jobs.GetAvailableJobs(station))
            {
                if (IsJobAllowed(session, job.Id, eligibility))
                    choices.Add(new WFRoundSetupJob(job.Id, GetNetEntity(station), Name(station)));
            }
        }
        return choices;
    }

    /// <summary>Applies explicit Normal choices to allocation copies without changing saved character profiles.</summary>
    public Dictionary<NetUserId, HumanoidCharacterProfile> GetRoundStartProfiles(Dictionary<NetUserId, HumanoidCharacterProfile> profiles)
    {
        var choices = new Dictionary<NetUserId, HumanoidCharacterProfile>(profiles);
        foreach (var (user, profile) in profiles)
        {
            var lobby = FindLobby(user);
            if (lobby?.Scenario != WFRoundScenario.Normal)
                continue;
            var priorities = new Dictionary<ProtoId<JobPrototype>, JobPriority>();
            if (lobby.Members[user].Job is { } job && _players.TryGetSessionById(user, out var player) && CanSpawnRole(lobby, player))
                priorities.Add(job, JobPriority.High);
            choices[user] = profile.WithJobPriorities(priorities).WithPreferenceUnavailable(PreferenceUnavailableMode.StayInLobby);
        }
        return choices;
    }

    /// <summary>Checks the allocator's result and permits a Normal member's single assigned spawn.</summary>
    public bool BeginAssignedSpawn(ICommonSession session, EntityUid station, string job)
    {
        var lobby = FindLobby(session.UserId);
        if (lobby == null)
            return true;
        var member = lobby.Members[session.UserId];
        if (lobby.Scenario != WFRoundScenario.Normal || member.Spawned || member.Job != job || !CanSpawnRole(lobby, session))
            return false;
        _pendingSpawns[session.UserId] = (station, null);
        return true;
    }

    /// <summary>Records a completed Normal start and always releases its temporary spawn permission.</summary>
    public void EndAssignedSpawn(ICommonSession session)
    {
        _pendingSpawns.Remove(session.UserId);
        var lobby = FindLobby(session.UserId);
        if (lobby?.Scenario != WFRoundScenario.Normal || !_ticker.UserHasJoinedGame(session))
            return;
        lobby.Members[session.UserId].Spawned = true;
        CaptureCrewCard(lobby.Members[session.UserId], session);
        lobby.Members[session.UserId].Ready = false;
        lobby.Started = true;
        Broadcast();
    }

    /// <summary>Keeps groups with unready hosts in the lobby and reserves crash starts.</summary>
    private void OnRoundSpawning(RulePlayerSpawningEvent args)
    {
        foreach (var lobby in _lobbies.Values.ToArray())
        {
            var candidates = args.PlayerPool.Where(p => lobby.Members.ContainsKey(p.UserId)).ToArray();
            if (!candidates.Any(p => p.UserId == lobby.Host))
            {
                foreach (var player in candidates)
                {
                    args.PlayerPool.Remove(player);
                    SendState(player, "wf-round-setup-error-host-not-ready");
                }
                continue;
            }
            // Normal groups share the ordinary round-start allocation and shortage access calculation.
            if (lobby.Scenario == WFRoundScenario.Normal)
                continue;
            foreach (var player in candidates)
                args.PlayerPool.Remove(player);
            foreach (var player in candidates.OrderByDescending(p => p.UserId == lobby.Host))
            {
                if (!TrySpawn(lobby, player, false, out var error))
                {
                    SendState(player, error);
                    if (player.UserId == lobby.Host)
                        break;
                }
            }
        }
    }

    /// <summary>Starts a ready group or admits an existing member to its running scenario.</summary>
    private bool Launch(Lobby lobby, ICommonSession actor, out string error)
    {
        error = "wf-round-setup-error-denied";
        if (_ticker.RunLevel != GameRunLevel.InRound || lobby.Members[actor.UserId].Spawned)
            return false;
        // Closing admission does not revoke an existing member's place in the scenario.
        if (lobby.Started)
            return TrySpawn(lobby, actor, true, out error);
        if (actor.UserId != lobby.Host || !lobby.Members[actor.UserId].Ready)
        {
            error = "wf-round-setup-error-host-not-ready";
            return false;
        }
        if (!TrySpawn(lobby, actor, true, out error))
            return false;
        foreach (var (user, member) in lobby.Members.ToArray())
        {
            if (user == actor.UserId || member.Spawned || !member.Ready || !_players.TryGetSessionById(user, out var session))
                continue;
            if (!TrySpawn(lobby, session, true, out var memberError))
                SendState(session, memberError);
        }
        return true;
    }

    /// <summary>Revalidates the member and grants temporary permission for one scenario spawn.</summary>
    private bool TrySpawn(Lobby lobby, ICommonSession session, bool lateJoin, out string error)
    {
        error = "wf-round-setup-error-job";
        var member = lobby.Members[session.UserId];
        if (!CanEnter(session) || member.Spawned || member.Job == null || !CanSpawnRole(lobby, session))
            return false;
        if (!CanDeployFaction(lobby))
        {
            error = "wf-round-setup-faction-balance";
            return false;
        }
        EntityUid station;
        EntityCoordinates? coordinates = null;
        if (lobby.Scenario != WFRoundScenario.Normal)
        {
            var replacingRules = EntityQueryEnumerator<DeathMatchRuleComponent>();
            while (replacingRules.MoveNext(out var rule, out _))
            {
                if (!_ticker.IsGameRuleActive(rule))
                    continue;
                error = "wf-round-setup-error-round-mode";
                return false;
            }
            if (!ScenarioJobs(lobby.Scenario, lobby.Faction).Contains(member.Job))
                return false;
        }
        if (WFRoundSetupPolicy.IsFormed(lobby.Scenario) && !_ships.HasAssignedShip(lobby.Id))
        {
            error = "wf-round-setup-formed-no-ship";
            return false;
        }
        if (_ships.HasAssignedShip(lobby.Id))
        {
            error = "wf-round-setup-ship-gone";
            if (!_ships.TryGetAssignedSpawn(lobby.Id, out station, out var spawn))
                return false;
            coordinates = spawn;
            if (lobby.Scenario == WFRoundScenario.FactionShip && !TryFactionStation(member.Job, out station))
            {
                error = "wf-round-setup-faction-slot-unavailable";
                return false;
            }
        }
        else if (lobby.Scenario == WFRoundScenario.Crashlanded)
        {
            if (!_crash.TryPrepare(lobby.Id, out station, out error))
                return false;
            if (!_crash.TryGetSpawn(lobby.Id, out var spawn))
            {
                error = "wf-round-setup-error-wreck";
                return false;
            }
            coordinates = spawn;
        }
        else if (lobby.Scenario == WFRoundScenario.Doomed)
        {
            if (!_doomed.TryPrepare(lobby.Id, out station, out error))
                return false;
            if (!_doomed.TryGetSpawn(lobby.Id, out var spawn))
            {
                error = "wf-round-setup-doomed-gone";
                return false;
            }
            coordinates = spawn;
        }
        else if (WFRoundSetupPolicy.IsShip(lobby.Scenario))
        {
            if (!lobby.Started && (lobby.Host != session.UserId || !AllPaymentsApproved(lobby)))
            {
                error = "wf-round-setup-error-ship-consent";
                return false;
            }
            if (!lobby.Started && lobby.HangarLicence == null && !CanBuyVessel(lobby, session, lobby.Vessel))
            {
                error = "wf-round-setup-ship-invalid";
                return false;
            }
            // Recheck every share before loading a hull, then check again immediately before payment.
            if (!lobby.Started && !CanAffordCrew(lobby, out error))
                return false;
            if (!_ships.TryPrepare(lobby.Id, lobby.Vessel, out station, out error, lobby.ShipStart, lobby.Faction, lobby.Members[lobby.Host].Job,
                    lobby.HangarLicence != null))
                return false;
            if (!_ships.TryGetSpawn(lobby.Id, out var spawn))
            {
                error = "wf-round-setup-ship-gone";
                return false;
            }
            coordinates = spawn;
            if (lobby.Scenario == WFRoundScenario.FactionShip && !TryFactionStation(member.Job, out station))
            {
                error = "wf-round-setup-faction-slot-unavailable";
                if (!lobby.Started)
                    RollbackShip(lobby);
                return false;
            }
            if (!lobby.Started && !ChargeCrew(lobby, session, out error))
            {
                RollbackShip(lobby);
                return false;
            }
        }
        else if (!TryNormalStation(member, out station))
            return false;

        var buyingShip = WFRoundSetupPolicy.IsShip(lobby.Scenario) && !lobby.Started;
        var completed = false;
        error = buyingShip ? "wf-round-setup-ship-failed" : "wf-round-setup-error-job";
        _pendingSpawns[session.UserId] = (station, coordinates);
        try
        {
            if (!_ticker.WFTryRoundSetupSpawn(session, station, member.Job, lateJoin))
                return false;
            if (buyingShip && !_ships.CompletePurchase(lobby.Id, session, lobby.ShipName))
            {
                _ticker.Respawn(session);
                return false;
            }
            completed = true;
            if (buyingShip && lobby.HangarLicence != null)
                MarkHangarShip(lobby);
            ApplyCrewTitle(lobby, member, session);
            UpdatePortrait(session.UserId, member);
            member.Spawned = true;
            member.Ready = false;
            lobby.Started = true;
            CaptureCrewCard(member, session);
            if (lobby.Scenario == WFRoundScenario.Crashlanded && !_ships.HasAssignedShip(lobby.Id))
            {
                if (session.AttachedEntity is { } survivor)
                    _crash.GiveEmergencyEquipment(survivor);
                _crash.NotifyArrival(lobby.Id, session);
            }
            else if (lobby.Scenario == WFRoundScenario.Doomed && !_ships.HasAssignedShip(lobby.Id))
                _doomed.NotifyArrival(lobby.Id, session);
            else if (WFRoundSetupPolicy.IsShip(lobby.Scenario) || _ships.HasAssignedShip(lobby.Id))
                _ships.NotifyArrival(lobby.Id, session);
            SyncCrewShips();
            return true;
        }
        finally
        {
            _pendingSpawns.Remove(session.UserId);
            if (buyingShip && !completed)
                RollbackShip(lobby);
        }
    }

    /// <summary>Resolves an ordinary station with an available slot for the selected job.</summary>
    private bool TryNormalStation(Member member, out EntityUid station)
    {
        station = default;
        if (member.Job == null)
            return false;
        if (member.Station is { } net)
        {
            if (!TryGetEntity(net, out var selected) || selected == null ||
                !HasComp<StationSpawningComponent>(selected.Value) || !IsStandardStation(selected.Value) ||
                !_jobs.TryGetJobSlot(selected.Value, member.Job, out var slots) || slots == 0)
                return false;
            station = selected.Value;
            return true;
        }
        var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
        while (query.MoveNext(out var candidate, out _, out _))
        {
            if (IsStandardStation(candidate) && !IsPlayerShip(candidate) &&
                _jobs.TryGetJobSlot(candidate, member.Job, out var slots) && slots != 0)
            {
                station = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>Places an authorized crew member on their scenario's surviving deck.</summary>
    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null || args.Session == null ||
            !_pendingSpawns.TryGetValue(args.Session.UserId, out var pending) ||
            pending.Station != args.Station || pending.Coordinates is not { } coordinates)
            return;
        args.SpawnResult = _spawning.SpawnPlayerMob(coordinates, args.Job, args.HumanoidCharacterProfile,
            args.Station, session: args.Session);
    }
}
