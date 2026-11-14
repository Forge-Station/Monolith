using Robust.Shared.Prototypes;

namespace Content.Shared._WF.NpcCrew;

/// <summary>
/// A crew role: the mob to spawn, the duty it works, how readily it fights, the title put before its name and any
/// components the role adds to the mob.
/// </summary>
[Prototype("wfCrewRole")]
public sealed partial class WFCrewRolePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Mob prototype spawned for this role.</summary>
    [DataField(required: true)]
    public EntProtoId Mob;

    /// <summary>The role's own components as an abstract prototype, put on a crew profile's body in place of <see cref="Mob"/>.</summary>
    [DataField]
    public EntProtoId? Kit;

    /// <summary>Duty worked when nothing is being fought; a branch of the crew HTN root.</summary>
    [DataField]
    public string Duty = WFCrewDuties.Guard;

    [DataField]
    public WFCrewEngagement Engagement = WFCrewEngagement.OnSight;

    /// <summary>Title put before the crewman's name, such as "First Officer".</summary>
    [DataField(required: true)]
    public LocId Title;

    /// <summary>Sort order in rosters and plans; lower first.</summary>
    [DataField]
    public int Order;

    /// <summary>Extra components the spawned mob gets, such as the radio officer's radio duty.</summary>
    [DataField]
    public ComponentRegistry Components = new();
}
