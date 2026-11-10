using Robust.Shared.Prototypes;

namespace Content.Shared._WF.NpcCrew;

/// <summary>When a crewman leaves its duty to fight.</summary>
public enum WFCrewEngagement : byte
{
    /// <summary>Fights anyone its faction is hostile to, or that a crew alert hands it.</summary>
    OnSight,

    /// <summary>Fights only whoever attacked it, for as long as it remembers the attack.</summary>
    WhenAttacked,

    /// <summary>Never fights; runs for the bridge when the ship is alerted.</summary>
    Never,
}

/// <summary>Duty names. Each is a branch of the crew HTN root, picked by the WFCrewDuty blackboard key.</summary>
public static class WFCrewDuties
{
    public const string Guard = "Guard";
    public const string Pilot = "Pilot";
    public const string Gunnery = "Gunnery";
}

/// <summary>The role prototypes the planner hands out.</summary>
public static class WFCrewRoles
{
    public static readonly ProtoId<WFCrewRolePrototype> Deckhand = "WFCrewDeckhand";
    public static readonly ProtoId<WFCrewRolePrototype> Marine = "WFCrewMarine";
    public static readonly ProtoId<WFCrewRolePrototype> Pilot = "WFCrewPilot";
    public static readonly ProtoId<WFCrewRolePrototype> RadioOperator = "WFCrewRadioOperator";
    public static readonly ProtoId<WFCrewRolePrototype> Captain = "WFCrewCaptain";
    public static readonly ProtoId<WFCrewRolePrototype> Gunner = "WFCrewGunner";
    public static readonly ProtoId<WFCrewRolePrototype> Engineer = "WFCrewEngineer";
}
