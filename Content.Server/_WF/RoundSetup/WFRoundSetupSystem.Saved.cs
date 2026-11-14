using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Roles;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private IResourceManager _resources = default!;
    private readonly Dictionary<NetUserId, WFSavedSetup[]> _savedSetups = new();
    private readonly HashSet<NetUserId> _savedReadFailures = new();

    /// <summary>Versioned account-owned data, stored independently of rounds and connected sessions.</summary>
    private sealed class SavedDocument
    {
        public int Version { get; set; } = 1;
        public WFSavedSetup[] Setups { get; set; } = Array.Empty<WFSavedSetup>();
    }

    private static ResPath SavePath(NetUserId user, string suffix = ".json") => new($"/round-setups/{user}{suffix}");

    /// <summary>Loads bounded private data once per round; damaged files are never silently overwritten.</summary>
    private WFSavedSetup[] GetSavedSetups(NetUserId user)
    {
        if (_savedSetups.TryGetValue(user, out var cached))
            return cached;
        var result = Array.Empty<WFSavedSetup>();
        var path = SavePath(user);
        var backup = SavePath(user, ".bak");
        try
        {
            if (!_resources.UserData.Exists(path) && _resources.UserData.Exists(backup))
                _resources.UserData.Rename(backup, path);
            if (_resources.UserData.Exists(path))
            {
                using var stream = _resources.UserData.OpenRead(path);
                if (stream.Length > 524288)
                    throw new IOException("Saved setup file exceeds its size limit.");
                var document = JsonSerializer.Deserialize<SavedDocument>(stream);
                if (document is not { Version: 1, Setups: not null } || document.Setups.Length > WFRoundSetupPolicy.MaximumSavedSetups ||
                    document.Setups.Any(setup => setup == null || !ValidSavedSetup(setup)) ||
                    document.Setups.Select(setup => setup.Id).Distinct().Count() != document.Setups.Length)
                    throw new IOException("Invalid saved setup document.");
                result = document.Setups;
                foreach (var setup in result.Where(setup => setup.Scenario == WFRoundScenario.FactionShip))
                {
                    setup.JobTitle = string.Empty;
                    setup.Roles = setup.Roles.Select(role => role with { Name = string.Empty }).ToArray();
                    if (setup.ShipStart == WFShipStartLocation.CentralDock)
                        setup.ShipStart = WFShipStartLocation.FactionBase;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _savedReadFailures.Add(user);
            Log.Warning($"Could not read crew setups for {user}: {e.Message}");
        }
        _savedSetups[user] = result;
        return result;
    }

    /// <summary>Writes a complete replacement before rotating the previous account file.</summary>
    private bool StoreSetups(NetUserId user, WFSavedSetup[] setups)
    {
        if (_savedReadFailures.Contains(user))
            return false;
        var data = _resources.UserData;
        var path = SavePath(user);
        var next = SavePath(user, ".next");
        var backup = SavePath(user, ".bak");
        try
        {
            data.CreateDir(new ResPath("/round-setups"));
            if (!data.Exists(path) && data.Exists(backup))
                data.Rename(backup, path);
            using (var stream = data.OpenWrite(next))
                JsonSerializer.Serialize(stream, new SavedDocument { Setups = setups });
            if (data.Exists(backup))
                data.Delete(backup);
            if (data.Exists(path))
                data.Rename(path, backup);
            data.Rename(next, path);
            _savedSetups[user] = setups;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not save crew setups for {user}: {e.Message}");
            return false;
        }
    }

    /// <summary>Stores only reusable choices, excluding people, codes, money and ready state.</summary>
    private bool SaveSetup(Lobby lobby, ICommonSession host, string? input)
    {
        if (!WFRoundSetupPolicy.TryNormalizeTitle(input, out var name) || string.IsNullOrWhiteSpace(name))
            return false;
        var saved = GetSavedSetups(host.UserId);
        var existing = saved.FirstOrDefault(setup => setup.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing == null && saved.Length >= WFRoundSetupPolicy.MaximumSavedSetups)
            return false;
        var member = lobby.Members[host.UserId];
        var title = member.Title;
        if (title.Length == 0 && member.RoleId is { } role && lobby.Roles.TryGetValue(role, out var selected))
            title = selected.Name;
        var snapshot = new WFSavedSetup
        {
            Id = existing?.Id ?? Enumerable.Range(1, WFRoundSetupPolicy.MaximumSavedSetups).First(id => saved.All(entry => entry.Id != id)),
            Name = name, Title = lobby.Title, Description = lobby.Description, Scenario = lobby.Scenario, Faction = lobby.Faction, ShipName = lobby.ShipName, ShipStart = lobby.ShipStart, Vessel = lobby.Vessel,
            Job = member.Job, JobTitle = lobby.Scenario == WFRoundScenario.FactionShip ? string.Empty : title, Access = lobby.Access, AllowLateJoin = lobby.AllowLateJoin,
            Roles = lobby.Roles.Values.Select(entry => new WFSavedRole(entry.Name, entry.Job, entry.Capacity)).ToArray(),
        };
        return ValidSavedSetup(snapshot) && ValidSavedJobs(snapshot) &&
               (lobby.Vessel == null || CanBuyVessel(lobby, host, lobby.Vessel)) && StoreSetups(host.UserId, saved.Where(entry => entry.Id != snapshot.Id).Append(snapshot).ToArray());
    }

    private bool DeleteSetup(NetUserId user, int id)
    {
        var saved = GetSavedSetups(user);
        return saved.Any(entry => entry.Id == id) && StoreSetups(user, saved.Where(entry => entry.Id != id).ToArray());
    }

    /// <summary>Creates a fresh unready crew only after validating the entire stored configuration.</summary>
    private bool LoadSetup(ICommonSession host, int id)
    {
        var saved = GetSavedSetups(host.UserId).FirstOrDefault(entry => entry.Id == id);
        if (saved == null || !ValidSavedSetup(saved) || _lobbies.Count >= 64 ||
            !IsScenarioAvailable(saved.Scenario) ||
            WFRoundSetupPolicy.IsShip(saved.Scenario) && saved.Vessel != null && !_ships.IsVesselAvailable(saved.Vessel, saved.Faction, saved.Job != null && IsJobAllowed(host, saved.Job) ? saved.Job : null) ||
            saved.Scenario == WFRoundScenario.FactionShip && (!CanCreateFaction(host, saved.Faction) || saved.Job != null && !IsJobAllowed(host, saved.Job)))
            return false;
        if (!ValidSavedJobs(saved) || !WFRoundSetupPolicy.IsShipStartAllowed(saved.ShipStart, saved.Faction))
            return false;
        var lobby = new Lobby
        {
            Id = ++_nextId, Host = host.UserId, Title = saved.Title, Description = saved.Description, Scenario = saved.Scenario, Faction = saved.Faction, ShipName = saved.ShipName, ShipStart = saved.ShipStart,
            Vessel = saved.Vessel, Access = WFRoundSetupPolicy.IsCompanyAccess(saved.Access) ? WFLobbyAccess.Requests : saved.Access, AllowLateJoin = saved.AllowLateJoin, Code = NewCode(),
        };
        foreach (var entry in saved.Roles)
        {
            var roleId = ++lobby.NextRoleId;
            lobby.Roles.Add(roleId, new LobbyRole { Id = roleId, Name = entry.Name, Job = entry.Job, Capacity = entry.Capacity });
        }
        _lobbies.Add(lobby.Id, lobby);
        RegisterMember(lobby, host, new Member { Job = saved.Job != null && IsJobAllowed(host, saved.Job) ? saved.Job : null, Title = saved.JobTitle });
        ResetContributions(lobby);
        return true;
    }

    /// <summary>Saving and loading use the same scenario catalog and current job prototypes.</summary>
    private bool ValidSavedJobs(WFSavedSetup saved)
    {
        var jobs = ScenarioJobs(saved.Scenario, saved.Faction);
        return saved.Roles.All(role => jobs.Contains(role.Job) && _prototypes.HasIndex<JobPrototype>(role.Job) &&
                   (saved.Scenario != WFRoundScenario.FactionShip || IsFactionJob(saved.Faction, role.Job) &&
                       WFFactionCrewPolicy.CanRecruit(saved.Faction, saved.Job, role.Job))) &&
               (saved.Job == null || jobs.Contains(saved.Job) && _prototypes.HasIndex<JobPrototype>(saved.Job));
    }

    private static bool ValidSavedSetup(WFSavedSetup saved) =>
        saved.Id is >= 1 and <= WFRoundSetupPolicy.MaximumSavedSetups &&
        WFRoundSetupPolicy.IsCrewScenario(saved.Scenario) &&
        (saved.Scenario == WFRoundScenario.FactionShip ? WFFactionCrewPolicy.All.Contains(saved.Faction) : saved.Faction == WFCrewFaction.None) &&
        saved.ShipName != null && WFRoundSetupPolicy.TryNormalizeTitle(saved.ShipName, out _) && Enum.IsDefined(saved.ShipStart) && Enum.IsDefined(saved.Access) &&
        saved.Name != null && saved.Title != null && saved.JobTitle != null && saved.Description != null &&
        WFRoundSetupPolicy.TryNormalizeDescription(saved.Description, out _) &&
        WFRoundSetupPolicy.TryNormalizeTitle(saved.Name, out var name) && !string.IsNullOrWhiteSpace(name) &&
        WFRoundSetupPolicy.TryNormalizeTitle(saved.Title, out _) && WFRoundSetupPolicy.TryNormalizeRoleName(saved.JobTitle, out _) &&
        saved.Roles != null && saved.Roles.Length <= WFRoundSetupPolicy.MaximumRoles &&
        saved.Roles.All(role => role != null && role.Name != null && role.Capacity is >= 1 and <= WFRoundSetupPolicy.MaximumMembers &&
            WFRoundSetupPolicy.TryNormalizeRoleName(role.Name, out _) && !string.IsNullOrEmpty(role.Job));
}
