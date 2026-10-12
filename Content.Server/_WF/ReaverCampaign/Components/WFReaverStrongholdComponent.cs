using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>A Reaver stronghold hub, on its encounter entity. It grows a level for each reinforcement that docks, and is broken when its lead ship is out of the fight.</summary>
[RegisterComponent]
public sealed partial class WFReaverStrongholdComponent : Component
{
    /// <summary>The cell it holds station in.</summary>
    [ViewVariables]
    public WFSectorCell Cell;

    /// <summary>The campaign tier when it was founded.</summary>
    [ViewVariables]
    public int Tier;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>Ship weapon hits its ships have taken from player ships.</summary>
    [ViewVariables]
    public int Hits;

    /// <summary>Hits each player ship has landed on it, so a fresh hit marks the ship that made it.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, int> AttackerHits = new();

    /// <summary>When its ships last took a hit from a player ship.</summary>
    [ViewVariables]
    public TimeSpan LastHit;

    /// <summary>Whether a patrol has been called back to it since it was first hit.</summary>
    [ViewVariables]
    public bool Recalled;

    /// <summary>When it may next send a sortie pack out against an attacker.</summary>
    [ViewVariables]
    public TimeSpan NextSortie;

    /// <summary>The sortie pack it sent, while it is out; there is one at a time.</summary>
    [ViewVariables]
    public EntityUid? Sortie;

    /// <summary>Seconds each player ship that never hit it has spent near it while it was under attack.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, float> Support = new();

    /// <summary>When it may next send out a patrol.</summary>
    [ViewVariables]
    public TimeSpan NextPatrol;

    /// <summary>How many reinforcements have joined it; zero at its founding.</summary>
    [ViewVariables]
    public int Level;

    /// <summary>When the next reinforcement may set out.</summary>
    [ViewVariables]
    public TimeSpan NextReinforcement;

    /// <summary>The reinforcement encounters that have docked or holding beside it.</summary>
    [ViewVariables]
    public List<EntityUid> Reinforcements = new();

    /// <summary>The reinforcement on its way, if any; there is one at a time.</summary>
    [ViewVariables]
    public EntityUid? Inbound;

    /// <summary>Whether its reinforcements have broken away to fight an attacker and not yet come back.</summary>
    [ViewVariables]
    public bool Scattered;

    /// <summary>The player ship its reinforcements were last sent against.</summary>
    [ViewVariables]
    public EntityUid? Attacker;

    /// <summary>The encounter's name without its level, so the level can be shown on the map.</summary>
    [ViewVariables]
    public string BaseName = string.Empty;
}
