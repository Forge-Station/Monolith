using Content.Shared._WF.ReaverCampaign;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>The Reaver campaign's state for the round, on the sector map entity: tier, clocks and the round's tallies.</summary>
[RegisterComponent]
public sealed partial class WFReaverCampaignComponent : Component
{
    [ViewVariables]
    public ProtoId<WFReaverCampaignPrototype> Campaign;

    [ViewVariables]
    public int Tier;

    [ViewVariables]
    public float Score;

    /// <summary>Since when the score has been below the current tier's threshold.</summary>
    [ViewVariables]
    public TimeSpan? BelowSince;

    /// <summary>When a tier change was last announced.</summary>
    [ViewVariables]
    public TimeSpan? TierAnnounced;

    /// <summary>When the last stronghold was founded; a convoy counts when it arrives.</summary>
    [ViewVariables]
    public TimeSpan? LastFounded;

    /// <summary>Since when no stronghold has stood, once one has been founded.</summary>
    [ViewVariables]
    public TimeSpan? NoStrongholdSince;

    [ViewVariables]
    public TimeSpan? NextSpread;

    [ViewVariables]
    public TimeSpan? NextRaid;

    /// <summary>Raids launched this round; every third goes for a station.</summary>
    [ViewVariables]
    public int Raids;

    /// <summary>When each player ship was last raided.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Raided = new();

    /// <summary>Since when each crewed player ship has been in held space.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> InsideSince = new();

    /// <summary>When each player ship may next be intercepted.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> InterceptCooldowns = new();

    /// <summary>The entity the sector watch speaks on the radio from.</summary>
    [ViewVariables]
    public EntityUid? Watch;

    [ViewVariables]
    public int PeakCells;

    [ViewVariables]
    public int Founded;

    [ViewVariables]
    public int Broken;

    [ViewVariables]
    public int PatrolsDestroyed;

    [ViewVariables]
    public int ConvoysDestroyed;

    /// <summary>Whether the last stronghold has broken with none founded since: the Reavers are broken for now.</summary>
    [ViewVariables]
    public bool Defeated;

    /// <summary>How many times the Reavers were broken this round.</summary>
    [ViewVariables]
    public int Defeats;

    /// <summary>Held cells players have retaken this round.</summary>
    [ViewVariables]
    public int Retaken;
}
