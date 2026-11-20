using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Defcon;

/// <summary>How the sector DEFCON scores threat, moves between levels and announces them.</summary>
[Prototype("wfDefconProfile")]
public sealed partial class WFDefconProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Score needed for DEFCON 4, 3, 2 and 1, in that order.</summary>
    [DataField]
    public List<float> Thresholds = new() { 15f, 35f, 60f, 85f };

    /// <summary>How often the threat is scored.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>How long a higher threat must hold before the level rises.</summary>
    [DataField]
    public TimeSpan RiseConfirm = TimeSpan.FromSeconds(60);

    /// <summary>How long a lower threat must hold before the level falls one step.</summary>
    [DataField]
    public TimeSpan FallDelay = TimeSpan.FromMinutes(10);

    /// <summary>The least time a level stands before it can fall.</summary>
    [DataField]
    public TimeSpan MinDwell = TimeSpan.FromMinutes(5);

    /// <summary>Score for each ADS tier, from Dormant up. Low because a campaign runs most rounds; only the last two tiers weigh.</summary>
    [DataField]
    public List<float> AdsTier = new() { 0f, 1f, 3f, 5f, 25f, 60f };

    /// <summary>Score for each Reaver tier, from the lowest up. Low because a campaign runs most rounds; only the last two tiers weigh.</summary>
    [DataField]
    public List<float> ReaverTier = new() { 0f, 1f, 3f, 5f, 25f, 60f };

    /// <summary>Score while the war level is HOT.</summary>
    [DataField]
    public float WarHot = 10f;

    /// <summary>The calmest level allowed while the war level is HOT.</summary>
    [DataField]
    public int WarFloor = 3;

    /// <summary>Score for each open scheduled threat encounter, and its cap.</summary>
    [DataField]
    public float ThreatEncounter = 6f;

    [DataField]
    public float ThreatEncounterCap = 18f;

    /// <summary>Score for each open distress call, and its cap.</summary>
    [DataField]
    public float Distress = 2f;

    [DataField]
    public float DistressCap = 8f;

    /// <summary>Score for each player death within <see cref="DeathWindow"/>, and its cap.</summary>
    [DataField]
    public float Death = 2f;

    [DataField]
    public float DeathCap = 12f;

    [DataField]
    public TimeSpan DeathWindow = TimeSpan.FromMinutes(10);

    /// <summary>The alarm every change opens with, before the level's callout.</summary>
    [DataField]
    public string? Alarm;

    /// <summary>Volume of the alarm and callouts.</summary>
    [DataField]
    public float Volume = -2f;

    /// <summary>How each level reads and sounds, keyed by its number (5 to 1).</summary>
    [DataField]
    public Dictionary<int, WFDefconLevel> Levels = new();
}

/// <summary>How one DEFCON level reads and sounds.</summary>
[DataDefinition]
public sealed partial class WFDefconLevel
{
    /// <summary>The level's name, such as FADE OUT.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>What the level means for crews, in the banner.</summary>
    [DataField(required: true)]
    public LocId Description;

    [DataField]
    public Color Color = Color.White;

    /// <summary>How many times the alarm sounds before the callout.</summary>
    [DataField]
    public int Alarms = 1;

    /// <summary>The level's callout, played after the alarm.</summary>
    [DataField]
    public string? Callout;

    /// <summary>A second alarm played alongside the first one.</summary>
    [DataField]
    public string? Siren;
}
