using Content.Server._WF.NpcCrew.Systems;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>
/// A crewman who reports what happens to his ship on the radio: docking, jumps, attacks, boarders, the captain or
/// pilot going down and the all-clear. Worked by <c>WFRadioOperatorSystem</c>; no HTN of its own.
/// </summary>
[RegisterComponent]
public sealed partial class WFRadioOperatorComponent : Component
{
    /// <summary>Channel for routine traffic: docking, jumps, orders.</summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> LocalChannel = "Traffic";

    /// <summary>Channel for alerts: mayday, boarders, crew down, all clear.</summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> AlertChannel = "Common";

    /// <summary>Alerts also go here when set.</summary>
    [DataField]
    public ProtoId<RadioChannelPrototype>? FactionChannel;

    /// <summary>Whether the ship calls a mayday when attacked. Raiders don't.</summary>
    [DataField]
    public bool CallsForHelp = true;

    /// <summary>What the ship is called on the air. Null uses the grid's name.</summary>
    [DataField]
    public string? Callsign;

    /// <summary>How soon the same line may be said again.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    /// <summary>How long after the last hostile activity an alert ends with the all-clear.</summary>
    [DataField]
    public TimeSpan AllClearDelay = TimeSpan.FromSeconds(120);

    /// <summary>Whether each line is also said aloud, so the bridge hears it.</summary>
    [DataField]
    public bool SpeakAloud = true;

    /// <summary>Whether an attack episode is running; it ends with the all-clear.</summary>
    [ViewVariables]
    public bool Alerted;

    [ViewVariables]
    public TimeSpan LastHostileActivity;

    /// <summary>Whether this episode's mayday went out.</summary>
    [ViewVariables]
    public bool MaydaySent;

    /// <summary>Whether this episode's boarding call went out.</summary>
    [ViewVariables]
    public bool BoardedSent;

    /// <summary>Whether the jump the drive is spooling for has been announced.</summary>
    [ViewVariables]
    public bool JumpAnnounced;

    /// <summary>Crewmen whose going down has been reported.</summary>
    [ViewVariables]
    public HashSet<EntityUid> DownReported = new();

    /// <summary>Visitors already warned over the radio, and until when: each is told once, not at every turn.</summary>
    public Dictionary<EntityUid, TimeSpan> Warned = new();

    /// <summary>When each line was last said.</summary>
    [ViewVariables]
    public Dictionary<WFRadioLine, TimeSpan> LastSent = new();

    /// <summary>The last transmissions, oldest first.</summary>
    [ViewVariables]
    public List<WFRadioTransmission> Sent = new();
}
