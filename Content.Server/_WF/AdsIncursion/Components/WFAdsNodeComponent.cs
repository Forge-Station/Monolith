using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.AdsIncursion.Components;

/// <summary>An ADS node, on its encounter entity: a gun platform holding a cell, gaining sentinels as it stands. It breaks when its lead is out of the fight.</summary>
[RegisterComponent]
public sealed partial class WFAdsNodeComponent : Component
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

    /// <summary>When its ships last took a hit from a player ship.</summary>
    [ViewVariables]
    public TimeSpan LastHit;

    /// <summary>Seconds each player ship that never hit it has spent near it while it was under attack.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, float> Support = new();

    /// <summary>The sentinel encounters that have joined it.</summary>
    [ViewVariables]
    public List<EntityUid> Sentinels = new();

    /// <summary>The sentinel on its way, if any; there is one at a time.</summary>
    [ViewVariables]
    public EntityUid? Inbound;

    /// <summary>When the next sentinel may set out.</summary>
    [ViewVariables]
    public TimeSpan NextSentinel;

    /// <summary>When it may next send out a sweep.</summary>
    [ViewVariables]
    public TimeSpan NextSweep;

    /// <summary>Whether its sentinels have broken away to fight an attacker and not yet come back.</summary>
    [ViewVariables]
    public bool Scattered;

    /// <summary>The player ship its sentinels were last sent against.</summary>
    [ViewVariables]
    public EntityUid? Attacker;

    /// <summary>Whether the node has been left standing while out of power with no attack on it, so it is logged once.</summary>
    [ViewVariables]
    public bool Starved;

    /// <summary>When the node was first found out of power this spell; past the grace it is broken like any other.</summary>
    [ViewVariables]
    public TimeSpan StarvedSince;
}
