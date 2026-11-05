using System.Linq;
using Robust.Shared.Network;

namespace Content.Server._WF.RoundSetup;

/// <summary>What the contract board asks of Crew Setup: who is in a player's crew.</summary>
public sealed partial class WFRoundSetupSystem
{
    /// <summary>Every member of a player's crew, them included; just them outside a crew.</summary>
    public IReadOnlyCollection<NetUserId> CrewMembers(NetUserId user) =>
        FindLobby(user) is { } lobby ? lobby.Members.Keys.ToList() : new List<NetUserId> { user };

    /// <summary>The crew's members who are in the round as its members, with their bodies; empty outside a crew.</summary>
    public List<(NetUserId User, EntityUid Body)> CrewInRound(NetUserId user) =>
        FindLobby(user) is { } lobby
            ? lobby.Members.Where(pair => pair.Value is { Spawned: true, Body: not null } && !TerminatingOrDeleted(pair.Value.Body.Value))
                .Select(pair => (pair.Key, pair.Value.Body!.Value))
                .ToList()
            : new List<(NetUserId, EntityUid)>();

    /// <summary>The crew's name, or null outside a crew.</summary>
    public string? CrewNameOf(NetUserId user) => FindLobby(user) is { } lobby ? CrewName(lobby) : null;
}
