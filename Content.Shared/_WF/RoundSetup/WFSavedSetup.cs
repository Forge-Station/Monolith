using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>A reusable setup owned by one account, without members, credentials or purchase consent.</summary>
[Serializable, NetSerializable]
public sealed class WFSavedSetup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WFRoundScenario Scenario { get; set; }
    public WFCrewFaction Faction { get; set; }
    public string ShipName { get; set; } = string.Empty;
    public WFShipStartLocation ShipStart { get; set; }
    public string? Vessel { get; set; }
    public WFLobbyAccess Access { get; set; }
    public bool AllowLateJoin { get; set; }
    public string? Job { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public WFSavedRole[] Roles { get; set; } = Array.Empty<WFSavedRole>();
}

/// <summary>A saved recruitment opening, independent of live reservations.</summary>
[Serializable, NetSerializable]
public sealed record WFSavedRole(string Name, string Job, int Capacity);
