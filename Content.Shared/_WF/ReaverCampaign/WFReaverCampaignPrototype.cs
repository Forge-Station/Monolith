using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ReaverCampaign;

/// <summary>The Reaver campaign for one storyteller preset: its pacing, tier ladder, bounties and encounters. Lists by tier run 0 to 5.</summary>
[Prototype("wfReaverCampaign")]
public sealed partial class WFReaverCampaignPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The storyteller preset this campaign runs under.</summary>
    [DataField(required: true)]
    public ProtoId<WFEncounterPresetPrototype> Preset;

    /// <summary>The faction whose cells the campaign claims.</summary>
    [DataField(required: true)]
    public ProtoId<WFSectorFactionPrototype> Faction;

    /// <summary>How long after the stations are generated the first stronghold is founded.</summary>
    [DataField]
    public TimeSpan FirstStrongholdDelay = TimeSpan.FromMinutes(25);

    /// <summary>How long with no stronghold standing before a fresh one is founded.</summary>
    [DataField]
    public TimeSpan RegroupDelay = TimeSpan.FromMinutes(30);

    /// <summary>How often one frontier cell is claimed or contested.</summary>
    [DataField]
    public TimeSpan SpreadInterval = TimeSpan.FromMinutes(12);

    /// <summary>How long a cell must have had no player traffic to be taken.</summary>
    [DataField]
    public float QuietMinutes = 10f;

    /// <summary>The least time between two foundings.</summary>
    [DataField]
    public TimeSpan FoundInterval = TimeSpan.FromMinutes(60);

    /// <summary>Held cells needed per standing stronghold before another is founded.</summary>
    [DataField]
    public int CellsPerStronghold = 7;

    [DataField]
    public int MaxStrongholds = 4;

    /// <summary>How many rings from a stronghold it supports spread cells.</summary>
    [DataField]
    public int SupportRange = 2;

    /// <summary>Patrols each stronghold keeps out, by tier.</summary>
    [DataField]
    public List<int> Patrols = new() { 0, 1, 1, 2, 2, 2 };

    /// <summary>How long a stronghold waits before sending out another patrol.</summary>
    [DataField]
    public TimeSpan PatrolInterval = TimeSpan.FromMinutes(10);

    /// <summary>How long a crewed player ship stays in held space before a patrol is sent to intercept it.</summary>
    [DataField]
    public TimeSpan InterceptDwell = TimeSpan.FromSeconds(60);

    /// <summary>How long after an intercept ends before the same ship may be intercepted again.</summary>
    [DataField]
    public TimeSpan InterceptCooldown = TimeSpan.FromMinutes(10);

    /// <summary>Intercepts out at once, campaign-wide, by tier.</summary>
    [DataField]
    public List<int> Intercepts = new() { 0, 1, 1, 2, 2, 2 };

    /// <summary>The first tier with raids.</summary>
    [DataField]
    public int RaidTier = 3;

    /// <summary>Time between raids, by tier from <see cref="RaidTier"/> up; the last entry serves any higher tier.</summary>
    [DataField]
    public List<TimeSpan> RaidInterval = new() { TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(26) };

    /// <summary>Multiplies the storyteller's weight for <see cref="ScaledEncounters"/>, by tier.</summary>
    [DataField]
    public List<float> ThreatScale = new() { 1f, 1f, 1.1f, 1.2f, 1.35f, 1.5f };

    /// <summary>The most <see cref="ThreatScale"/> may multiply a weight by.</summary>
    [DataField]
    public float MaxThreatScale = 1.5f;

    /// <summary>The threat score each tier from 1 to 5 starts at.</summary>
    [DataField]
    public List<float> TierThresholds = new() { 12f, 30f, 55f, 80f, 105f };

    /// <summary>Spesos for breaking a stronghold, by tier.</summary>
    [DataField]
    public List<int> StrongholdBounty = new() { 60000, 90000, 120000, 160000, 200000, 250000 };

    [DataField]
    public int PatrolBounty = 20000;

    /// <summary>Spesos for retaking a held cell, by tier, split among the crews of the ships that held it through the countdown.</summary>
    [DataField]
    public List<int> RetakeBounty = new() { 10000, 12000, 14000, 17000, 20000, 25000 };

    /// <summary>The faction credits each navy's members get with a bounty.</summary>
    [DataField]
    public List<WFReaverCreditGrant> Credits = new();

    /// <summary>The stronghold hub: a shielded hull with docking ports and no guards, which reinforcements dock with.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> Stronghold = "WFReaverStronghold";

    /// <summary>The shield generator the campaign anchors on a hub whose hull has none.</summary>
    [DataField]
    public EntProtoId HubShield = "WFReaverHubShieldGenerator";

    /// <summary>How long a stronghold stands, from its founding or its last level, before the next reinforcement sets out.</summary>
    [DataField]
    public TimeSpan ReinforcementInterval = TimeSpan.FromMinutes(12);

    /// <summary>The highest stronghold level reinforcements bring it to, by tier.</summary>
    [DataField]
    public List<int> MaxLevelByTier = new() { 0, 1, 2, 2, 3, 4 };

    /// <summary>The reinforcement convoy for each level, from level 1: the prototype that docks to raise a stronghold to that level.</summary>
    [DataField]
    public List<ProtoId<WFEncounterPrototype>> Reinforcements = new()
    {
        "WFReaverReinforcementGunship",
        "WFReaverReinforcementGunship",
        "WFReaverReinforcementHeavy",
        "WFReaverReinforcementHeavy",
    };

    /// <summary>What each stronghold level adds to its bounty, as a fraction of the base: the bounty is base times one plus this times the level.</summary>
    [DataField]
    public float LevelBountyScale = 0.5f;

    [DataField]
    public ProtoId<WFEncounterPrototype> Patrol = "WFReaverPatrol";

    /// <summary>A raider pack sent at a player ship.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> RaidPack = "WFReaverRaidPack";

    /// <summary>Boarders sent to dock with a player ship.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> RaidBoarders = "WFReaverRaidBoarders";

    /// <summary>A raider pack sent to prowl round a station.</summary>
    [DataField]
    public ProtoId<WFEncounterPrototype> RaidStation = "WFReaverRaidStation";

    /// <summary>The storyteller's own Reaver encounters, weighted up by tier.</summary>
    [DataField]
    public List<ProtoId<WFEncounterPrototype>> ScaledEncounters = new() { "WFEncounterPirateRaiders", "WFEncounterPirateBoarders" };

    /// <summary>Played with a rising tier, a founding and a regrouping.</summary>
    [DataField]
    public SoundSpecifier RisingSound = new SoundPathSpecifier("/Audio/Misc/redalert.ogg");

    /// <summary>Played with a falling tier and a broken stronghold.</summary>
    [DataField]
    public SoundSpecifier FallingSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    [DataField]
    public Color RisingColor = Color.FromHex("#D84A3A");

    [DataField]
    public Color FallingColor = Color.FromHex("#3FB8AF");
}

/// <summary>Faction credits one navy's members get with a bounty.</summary>
[DataDefinition]
public sealed partial class WFReaverCreditGrant
{
    /// <summary>The credit stack to hand out.</summary>
    [DataField(required: true)]
    public EntProtoId Entity;

    /// <summary>The companies whose members get these credits.</summary>
    [DataField]
    public List<string> Companies = new();

    /// <summary>Credits each helper of a listed company gets for breaking a stronghold.</summary>
    [DataField]
    public int Stronghold;

    /// <summary>Credits each helper of a listed company gets for destroying a patrol.</summary>
    [DataField]
    public int Patrol;
}
