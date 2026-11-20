using System.Linq;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private SharedMapSystem _statusMap = default!;
    [Dependency] private Content.Server._NF.CryoSleep.CryoSleepSystem _cryoSleep = default!;

    /// <summary>The largest deck seen since a crew's current ship was assigned.</summary>
    private sealed class ShipHealth
    {
        public readonly HashSet<EntityUid> Grids = new();
        public int Baseline;
        public WFCrewShipCondition Condition;
    }

    private readonly Dictionary<int, ShipHealth> _shipHealth = new();
    private readonly Dictionary<NetUserId, WFCrewMemberStatus> _knownStatuses = new();
    private readonly HashSet<int> _activeCrews = new();

    /// <summary>Reads a member's condition from their spawned body.</summary>
    private WFCrewMemberStatus MemberStatus(Member member)
    {
        if (!member.Spawned)
            return WFCrewMemberStatus.Waiting;
        if (member.Body is not { } body || TerminatingOrDeleted(body))
            return WFCrewMemberStatus.Missing;
        if (!TryComp<MobStateComponent>(body, out var state))
            return WFCrewMemberStatus.Alive;
        return state.CurrentState switch
        {
            MobState.Dead => WFCrewMemberStatus.Dead,
            MobState.Critical => WFCrewMemberStatus.Critical,
            _ => WFCrewMemberStatus.Alive,
        };
    }

    /// <summary>Whether a launched crew still has someone in it: alive and out of cryosleep, or connected and yet to spawn.</summary>
    private bool HasActiveMembers(Lobby lobby) => lobby.Members.Any(pair => MemberStatus(pair.Value) switch
    {
        WFCrewMemberStatus.Alive or WFCrewMemberStatus.Critical => !_cryoSleep.HasCryosleepingBody(pair.Key),
        WFCrewMemberStatus.Waiting => _players.TryGetSessionById(pair.Key, out _),
        _ => false,
    });

    private WFCrewShipCondition ShipCondition(Lobby lobby) =>
        _shipHealth.TryGetValue(lobby.Id, out var health) ? health.Condition : WFCrewShipCondition.None;

    /// <summary>Samples member and ship conditions; true when any public status changed.</summary>
    private bool UpdateCrewStatus()
    {
        var changed = false;
        var seen = new HashSet<NetUserId>();
        foreach (var lobby in _lobbies.Values)
        foreach (var (user, member) in lobby.Members)
        {
            seen.Add(user);
            var status = MemberStatus(member);
            if (!_knownStatuses.TryGetValue(user, out var known) || known != status)
                changed = true;
            _knownStatuses[user] = status;
        }
        foreach (var user in _knownStatuses.Keys.Where(user => !seen.Contains(user)).ToArray())
            _knownStatuses.Remove(user);
        // Cryosleep leaves the body alive, so it's watched apart from status: it decides whether a crew is still listed.
        var active = _lobbies.Values.Where(lobby => lobby.Started && HasActiveMembers(lobby)).Select(lobby => lobby.Id).ToHashSet();
        if (!active.SetEquals(_activeCrews))
        {
            changed = true;
            _activeCrews.Clear();
            _activeCrews.UnionWith(active);
        }

        foreach (var id in _shipHealth.Keys.Where(id => !_lobbies.TryGetValue(id, out var lobby) || !lobby.Started).ToArray())
            _shipHealth.Remove(id);
        foreach (var lobby in _lobbies.Values.Where(lobby => lobby.Started))
        {
            var grids = CrewGrids(lobby).Where(grid => !TerminatingOrDeleted(grid)).ToArray();
            if (!_shipHealth.TryGetValue(lobby.Id, out var health))
                _shipHealth[lobby.Id] = health = new ShipHealth();
            // A replacement ship shares no grid with the previous one, so its own deck becomes the baseline.
            if (grids.Length > 0 && !grids.Any(health.Grids.Contains))
            {
                health.Grids.Clear();
                health.Baseline = 0;
            }
            health.Grids.UnionWith(grids);
            var largest = 0;
            foreach (var grid in grids)
            {
                if (TryComp<MapGridComponent>(grid, out var comp))
                    largest = Math.Max(largest, _statusMap.GetFilledTileCount((grid, comp)));
            }
            health.Baseline = Math.Max(health.Baseline, largest);
            // A crew that never had a ship has no condition; a ship seen once and then lost is gone.
            var condition = health.Grids.Count == 0 ? WFCrewShipCondition.None : WFCrewStatusPolicy.Condition(largest, health.Baseline);
            changed |= condition != health.Condition;
            health.Condition = condition;
        }
        return changed;
    }
}
