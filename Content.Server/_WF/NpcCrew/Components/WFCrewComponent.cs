using Content.Shared._WF.NpcCrew;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>
/// An NPC crewman: works its duty at its post and fights by its engagement rule. Mirrored to the HTN blackboard
/// by <c>WFCrewSystem</c>.
/// </summary>
[RegisterComponent]
public sealed partial class WFCrewComponent : Component
{
    /// <summary>Mission flight limits retained even when the crew has no living pilot.</summary>
    [DataField]
    public WFCrewNavigationSettings Navigation = new();

    /// <summary>Limits local action chatter independently of important radio reports.</summary>
    public TimeSpan NextSpeech;
    public Dictionary<string, TimeSpan> SpokenActions = new();
    public TimeSpan NextReport;
    public TimeSpan NextPatrol;
    public Dictionary<EntityUid, TimeSpan> RadioSightings = new();

    /// <summary>When each attacker last struck this crewman himself; only a fresh blow sends him after someone unseen.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Struck = new();

    /// <summary>Whether this crew puts its troubles on the air. Raiders don't.</summary>
    [DataField]
    public bool CallsForHelp = true;

    [DataField]
    public ProtoId<WFCrewRolePrototype>? Role;

    /// <summary>The crew profile the crewman was spawned with, for the titles it gives its roles.</summary>
    [DataField]
    public ProtoId<WFCrewProfilePrototype>? Profile;

    /// <summary>Duty branch of the crew HTN root worked when not fighting.</summary>
    [DataField]
    public string Duty = WFCrewDuties.Guard;

    [DataField]
    public WFCrewEngagement Engagement = WFCrewEngagement.OnSight;

    /// <summary>Whether on-sight crew share targets with their group aboard this ship.</summary>
    [DataField]
    public bool ShareAlerts = true;

    /// <summary>Keep this crewman's AI running without nearby player bodies.</summary>
    [DataField]
    public bool KeepActive = true;

    /// <summary>Whether this crewman goes looking for hostiles it cannot see. Off, it fights only what is in sight.</summary>
    [DataField]
    public bool Pursues = true;

    /// <summary>Everyone aboard with the same group is one crew.</summary>
    [DataField]
    public string Group = string.Empty;

    /// <summary>Ships whose crews share a battlegroup are allies and answer each other's threats.</summary>
    [DataField]
    public string Battlegroup = string.Empty;

    /// <summary>How well this crewman shoots, lays ship guns and flies.</summary>
    [DataField]
    public WFCrewSkill Skill = WFCrewSkill.Veteran;

    /// <summary>When this crew stops attacking another ship.</summary>
    [DataField]
    public WFCrewDisengage Disengage = WFCrewDisengage.Disable;

    /// <summary>Deter: the distance beyond which an attacker is left alone.</summary>
    [DataField]
    public float DisengageRange = 500f;

    /// <summary>Where the duty is worked and where the crewman returns to. Grid-relative.</summary>
    [DataField]
    public EntityCoordinates? Post;

    /// <summary>How close to the post counts as being at it.</summary>
    [DataField]
    public float PostRange = 1.5f;

    /// <summary>The ship whose allow list holds this crewman's card, so deleting him revokes it.</summary>
    [ViewVariables]
    public EntityUid? AccessShip;

    /// <summary>Whether the role title has been put in front of the name.</summary>
    [ViewVariables]
    public bool Titled;
}
