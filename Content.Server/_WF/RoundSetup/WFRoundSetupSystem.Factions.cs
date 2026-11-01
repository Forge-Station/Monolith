using System.Linq;
using Content.Server._Battlefield14.FactionTracking;
using Content.Server._NF.GameRule;
using Content.Server.GameTicking;
using Content.Shared.Maps;
using Content.Server.Station.Components;
using Content.Shared._WF.RoundSetup;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    private static string[] ScenarioJobs(WFRoundScenario scenario, WFCrewFaction faction) => scenario switch
    {
        WFRoundScenario.Ship or WFRoundScenario.Doomed or WFRoundScenario.Formed => WFShipCrewJobs.Jobs,
        WFRoundScenario.FactionShip => WFFactionCrewPolicy.Jobs(faction),
        _ => WFCrashScenarioSystem.AllowedJobs,
    };

    /// <summary>A crew scenario this server can run right now.</summary>
    private bool IsScenarioAvailable(WFRoundScenario scenario) => WFRoundSetupPolicy.IsCrewScenario(scenario) &&
        (scenario != WFRoundScenario.Crashlanded || _crash.IsAvailable) &&
        (scenario != WFRoundScenario.Doomed || _doomed.IsAvailable);

    /// <summary>Requires a real faction job offered by the current map and allowed for this character.</summary>
    private bool CanCreateFaction(ICommonSession session, WFCrewFaction faction, Dictionary<string, bool>? eligibility = null) =>
        WFFactionCrewPolicy.Jobs(faction).Any(job => IsFactionJob(faction, job) && FactionJobAvailable(job) && IsJobAllowed(session, job, eligibility));

    private bool IsFactionJob(WFCrewFaction faction, string job) =>
        WFFactionCrewPolicy.Rank(faction, job) >= 0 && _prototypes.TryIndex<JobPrototype>(job, out var prototype) &&
        prototype.AssignedCompany.Id == WFFactionCrewPolicy.Company(faction);

    /// <summary>Uses existing map slots instead of creating extra faction command positions.</summary>
    private bool FactionJobAvailable(string job)
    {
        if (_ticker.RunLevel != GameRunLevel.PreRoundLobby)
            return TryFactionStation(job, out _);
        return _gameMaps.GetSelectedMap() is { } map && MapOffersJob(map, job) || PoiOffersJob(job);
    }

    private bool MapOffersJob(GameMapPrototype map, string job)
    {
        foreach (var station in map.Stations.Values)
        {
            if (!station.StationComponentOverrides.TryGetComponent<StationJobsComponent>(_components, out var jobs))
                continue;
            foreach (var (id, slots) in jobs.SetupAvailableJobs)
            {
                if (id.Id == job && slots.Length > 0 && slots[0] != 0)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Before the round, the job slots of the base POIs this preset is sure to spawn: Required ones, and the only
    /// member of its own group at full chance. Camelot (USSP and VG) and the Underground exist only once the round starts.
    /// </summary>
    private bool PoiOffersJob(string job)
    {
        var preset = _ticker.Preset?.ID;
        var candidates = _prototypes.EnumeratePrototypes<PointOfInterestPrototype>()
            .Where(poi => poi.SpawnGamePreset.Length == 0 || preset != null && poi.SpawnGamePreset.Any(p => p.Id == preset))
            .ToList();
        foreach (var poi in candidates)
        {
            var certain = poi.SpawnGroup == "Required" ||
                          poi.SpawnGroup is not ("Optional" or "CargoDepot" or "MarketStation") && poi.SpawnChance >= 1f &&
                          candidates.Count(other => other.SpawnGroup == poi.SpawnGroup) == 1;
            if (certain && _prototypes.TryIndex<GameMapPrototype>(poi.ID, out var map) && MapOffersJob(map, job))
                return true;
        }
        return false;
    }

    /// <summary>Spawning uses the faction station's job ledger and records, with only coordinates redirected to the crew ship.</summary>
    private bool TryFactionStation(string job, out EntityUid station)
    {
        var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
        while (query.MoveNext(out var candidate, out _, out _))
        {
            if (IsStandardStation(candidate) && !IsPlayerShip(candidate) && _jobs.TryGetJobSlot(candidate, job, out var slots) && slots != 0)
            {
                station = candidate;
                return true;
            }
        }
        station = EntityUid.Invalid;
        return false;
    }

    /// <summary>Changing command never makes an occupied higher-rank position available to a lower-rank host.</summary>
    private static bool CanLeadFaction(Lobby lobby, NetUserId host, string? job = null) =>
        lobby.Scenario != WFRoundScenario.FactionShip ||
        lobby.Members.TryGetValue(host, out var member) &&
        WFFactionCrewPolicy.Rank(lobby.Faction, job ?? member.Job) >= 0 &&
        lobby.Members.Where(pair => pair.Key != host && pair.Value.Job != null).All(pair =>
            WFFactionCrewPolicy.CanRecruit(lobby.Faction, job ?? member.Job, pair.Value.Job));

    /// <summary>Retires empty openings above the current host; existing spawned identities are preserved.</summary>
    private void ReconcileFactionRoles(Lobby lobby)
    {
        if (lobby.Scenario != WFRoundScenario.FactionShip)
            return;
        var job = lobby.Members[lobby.Host].Job;
        foreach (var (user, member) in lobby.Members)
        {
            if (user == lobby.Host || member.Spawned || WFFactionCrewPolicy.CanRecruit(lobby.Faction, job, member.Job))
                continue;
            member.Job = null;
            member.RoleId = null;
            member.Station = null;
            UnreadyMember(user, member);
        }
        foreach (var role in lobby.Roles.Values.Where(role => !WFFactionCrewPolicy.CanRecruit(lobby.Faction, job, role.Job)).ToArray())
            RemoveRole(lobby, role.Id);
    }

    /// <summary>Faction counts use the explicit setup job without rewriting saved preferences.</summary>
    public bool TryGetSelectedFactionJob(NetUserId user, out string job)
    {
        var lobby = FindLobby(user);
        job = string.Empty;
        if (lobby?.Scenario != WFRoundScenario.FactionShip || lobby.Members[user].Job is not { } selected || !IsFactionJob(lobby.Faction, selected))
            return false;
        job = selected;
        return true;
    }

    /// <summary>Checks late-join balance before payment; pregame already uses the normal readiness gate.</summary>
    private bool CanDeployFaction(Lobby lobby)
    {
        // Only TSF and PDV are balanced against each other; the other factions sit outside both sides.
        if (lobby.Scenario != WFRoundScenario.FactionShip || _ticker.RunLevel == GameRunLevel.PreRoundLobby ||
            _ticker.CurrentPreset?.AutobalancerEnabled != true || lobby.Faction is not (WFCrewFaction.TSF or WFCrewFaction.PDV))
            return true;
        return !EntityManager.System<FactionTrackingSystem>().IsFactionOverpopulated(
            lobby.Faction == WFCrewFaction.TSF ? "blufor" : "redfor", _ticker);
    }
}
