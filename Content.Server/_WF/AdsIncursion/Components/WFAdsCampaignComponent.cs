using Content.Shared._WF.AdsIncursion;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.AdsIncursion.Components;

/// <summary>The ADS incursion's state for the round, on the sector map entity: tier, clocks and the round's tallies.</summary>
[RegisterComponent]
public sealed partial class WFAdsCampaignComponent : Component
{
    [ViewVariables]
    public ProtoId<WFAdsCampaignPrototype> Campaign;

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

    /// <summary>When a quiet banner was last sent.</summary>
    [ViewVariables]
    public TimeSpan? LastQuietBanner;

    /// <summary>When the last node was founded; a deployment counts when it arrives.</summary>
    [ViewVariables]
    public TimeSpan? LastFounded;

    /// <summary>Since when no node has stood, once one has been founded.</summary>
    [ViewVariables]
    public TimeSpan? NoNodeSince;

    [ViewVariables]
    public TimeSpan? NextSpread;

    [ViewVariables]
    public TimeSpan? NextChallenge;

    /// <summary>Since when each crewed player ship has been in held space.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> InsideSince = new();

    /// <summary>When each player ship may next be interdicted.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> InterdictCooldowns = new();

    /// <summary>The entity the sector watch speaks on the radio from.</summary>
    [ViewVariables]
    public EntityUid? Watch;

    [ViewVariables]
    public int PeakCells;

    [ViewVariables]
    public int Founded;

    [ViewVariables]
    public int Broken;

    /// <summary>Sweeps and sentinels destroyed this round.</summary>
    [ViewVariables]
    public int SweepsDestroyed;

    [ViewVariables]
    public int DeploymentsStopped;

    /// <summary>Cells challenged this round.</summary>
    [ViewVariables]
    public int Challenges;

    [ViewVariables]
    public int ChallengesWon;

    [ViewVariables]
    public int ChallengesLost;

    /// <summary>Held cells players have retaken this round.</summary>
    [ViewVariables]
    public int Retaken;

    /// <summary>Whether the last node has broken with none founded since: the ADS is dormant for now.</summary>
    [ViewVariables]
    public bool Defeated;

    /// <summary>How many times the ADS went dormant this round.</summary>
    [ViewVariables]
    public int Defeats;

    /// <summary>Since when the campaign has stood at tier 5; null below it.</summary>
    [ViewVariables]
    public TimeSpan? OverrunSince;

    /// <summary>Dreadnoughts activated this round.</summary>
    [ViewVariables]
    public int Dreadnoughts;

    [ViewVariables]
    public int DreadnoughtsDestroyed;

    /// <summary>Reaver strongholds broken with a dreadnought among their attackers.</summary>
    [ViewVariables]
    public int StrongholdsBroken;

    /// <summary>Whether the dreadnought has been destroyed: the ADS founds, launches and sends nothing more this round, and what stands fights on until broken.</summary>
    [ViewVariables]
    public bool Finished;
}
