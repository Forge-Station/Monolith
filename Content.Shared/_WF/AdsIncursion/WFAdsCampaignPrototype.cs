using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.AdsIncursion;

/// <summary>The ADS incursion for one storyteller preset: its pacing, tier ladder, bounties and encounters. Lists by tier run 0 to 5.</summary>
[Prototype("wfAdsCampaign")]
public sealed partial class WFAdsCampaignPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The storyteller preset this campaign runs under.</summary>
    [DataField(required: true)]
    public ProtoId<WFEncounterPresetPrototype> Preset;

    /// <summary>The faction whose cells the campaign claims.</summary>
    [DataField(required: true)]
    public ProtoId<WFSectorFactionPrototype> Faction;

    /// <summary>How long after the stations are generated the first node is founded.</summary>
    [DataField]
    public TimeSpan FirstNodeDelay = TimeSpan.FromMinutes(35);

    /// <summary>How long with no node standing and no deployment out before a fresh node is founded.</summary>
    [DataField]
    public TimeSpan RegroupDelay = TimeSpan.FromMinutes(30);

    /// <summary>How often one quiet frontier cell is claimed.</summary>
    [DataField]
    public TimeSpan SpreadInterval = TimeSpan.FromMinutes(15);

    /// <summary>How long a cell must have had no player traffic to be taken.</summary>
    [DataField]
    public float QuietMinutes = 10f;

    /// <summary>The least time between two foundings; a deployment counts when it arrives.</summary>
    [DataField]
    public TimeSpan DeployInterval = TimeSpan.FromMinutes(70);

    /// <summary>Held cells needed per standing node before another deploys.</summary>
    [DataField]
    public int CellsPerNode = 7;

    [DataField]
    public int MaxNodes = 3;

    /// <summary>How many rings from a node it supports spread cells.</summary>
    [DataField]
    public int SupportRange = 2;

    /// <summary>Sweeps each node keeps out, by tier.</summary>
    [DataField]
    public List<int> Sweeps = new() { 0, 1, 1, 1, 2, 2 };

    /// <summary>How long a node waits before sending out another sweep.</summary>
    [DataField]
    public TimeSpan SweepInterval = TimeSpan.FromMinutes(12);

    /// <summary>How long a crewed player ship stays in held space before a sweep is sent to interdict it.</summary>
    [DataField]
    public TimeSpan InterdictDwell = TimeSpan.FromSeconds(90);

    /// <summary>How long after an interdiction ends before the same ship may be interdicted again.</summary>
    [DataField]
    public TimeSpan InterdictCooldown = TimeSpan.FromMinutes(10);

    /// <summary>Interdictions out at once, campaign-wide, by tier.</summary>
    [DataField]
    public List<int> Interdicts = new() { 0, 1, 1, 1, 2, 2 };

    /// <summary>How long a node stands, from its founding or its last sentinel, before the next sentinel sets out.</summary>
    [DataField]
    public TimeSpan SentinelInterval = TimeSpan.FromMinutes(15);

    /// <summary>The most sentinels a node keeps, by tier.</summary>
    [DataField]
    public List<int> MaxSentinelsByTier = new() { 0, 0, 1, 1, 2, 2 };

    /// <summary>The first tier at which the ADS challenges cells another faction holds.</summary>
    [DataField]
    public int ChallengeTier = 2;

    /// <summary>Time between two challenges.</summary>
    [DataField]
    public TimeSpan ChallengeInterval = TimeSpan.FromMinutes(25);

    /// <summary>How long after its interdictor's reckoned arrival a challenge is decided.</summary>
    [DataField]
    public TimeSpan ChallengeDeadline = TimeSpan.FromMinutes(10);

    /// <summary>Challenges out at once.</summary>
    [DataField]
    public int MaxChallenges = 1;

    /// <summary>The threat score each tier from 1 to 5 starts at.</summary>
    [DataField]
    public List<float> TierThresholds = new() { 10f, 25f, 45f, 70f, 95f };

    /// <summary>Spesos for breaking a node, by tier; each sentinel alive adds half.</summary>
    [DataField]
    public List<int> NodeBounty = new() { 50000, 75000, 100000, 130000, 170000, 220000 };

    /// <summary>Spesos for destroying a sweep or a sentinel.</summary>
    [DataField]
    public int SweepBounty = 18000;

    /// <summary>Spesos for destroying an interdictor or a picket.</summary>
    [DataField]
    public int InterdictorBounty = 30000;

    /// <summary>Spesos for retaking a held cell, by tier, split among the crews of the ships that held it through the countdown.</summary>
    [DataField]
    public List<int> RetakeBounty = new() { 10000, 12000, 14000, 17000, 20000, 25000 };

    /// <summary>The faction credits each navy's members get with a bounty: the stronghold amount for a node, the patrol amount for anything else.</summary>
    [DataField]
    public List<WFReaverCreditGrant> Credits = new();

    /// <summary>A node: the gun platform that holds a cell and its ring.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> Node = "WFAdsNode";

    /// <summary>A later node flying out from a standing one.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> Deployment = "WFAdsDeployment";

    /// <summary>A gunship that joins a node and circles it.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> Sentinel = "WFAdsSentinel";

    /// <summary>A gunship flying a route through held space.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> Sweep = "WFAdsSweep";

    /// <summary>The boat sent to challenge a cell another faction holds.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> Interdictor = "WFAdsInterdictor";

    /// <summary>The tier 5 dreadnought, sent against the Reavers' strongholds; null for none.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype>? Dreadnought;

    /// <summary>How long the campaign must stand at tier 5 before the dreadnought is activated.</summary>
    [DataField]
    public TimeSpan DreadnoughtDelay = TimeSpan.FromMinutes(10);

    /// <summary>Spesos for destroying the dreadnought, which finishes the ADS for the round.</summary>
    [DataField]
    public int DreadnoughtBounty = 400000;

    /// <summary>Played with a rising tier, a founding and a regroup.</summary>
    [DataField]
    public SoundSpecifier RisingSound = new SoundPathSpecifier("/Audio/Misc/redalert.ogg");

    /// <summary>Played with a falling tier and a broken node.</summary>
    [DataField]
    public SoundSpecifier FallingSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    /// <summary>Played on top of the chime for a capital event: the dreadnought activated.</summary>
    [DataField]
    public SoundSpecifier CapitalSound = new SoundPathSpecifier("/Audio/Misc/gamma.ogg");

    [DataField]
    public Color RisingColor = Color.FromHex("#9B4DFF");

    [DataField]
    public Color FallingColor = Color.FromHex("#3FB8AF");
}
