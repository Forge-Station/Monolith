using Robust.Shared.Prototypes;

namespace Content.Server._WF.CavernEncounters;

/// <summary>A tunnel raider: fights until badly hurt, then runs from whoever hurt him; drops scrap and a little cash when he dies.</summary>
[RegisterComponent, Access(typeof(WFCavernRaiderSystem))]
public sealed partial class WFCavernRaiderComponent : Component
{
    /// <summary>Damage taken, before the wounds share it out, at which he runs.</summary>
    [DataField]
    public float FleeAt = 45f;

    /// <summary>Tiles he runs from whoever last hurt him.</summary>
    [DataField]
    public float FleeDistance = 24f;

    /// <summary>Damage taken so far.</summary>
    [ViewVariables]
    public float Taken;

    /// <summary>Whether he is running.</summary>
    [ViewVariables]
    public bool Fleeing;

    /// <summary>Whoever last hurt him.</summary>
    [ViewVariables]
    public EntityUid? Attacker;

    /// <summary>When he next looks over his shoulder and picks a new spot to run to.</summary>
    [ViewVariables]
    public TimeSpan NextLook;

    /// <summary>What he drops when he dies.</summary>
    [DataField]
    public List<EntProtoId> Loot = new();

    /// <summary>The fewest spesos he drops.</summary>
    [DataField]
    public int CashMin = 20;

    /// <summary>The most spesos he drops.</summary>
    [DataField]
    public int CashMax = 120;

    /// <summary>Whether his drop has been made.</summary>
    [ViewVariables]
    public bool Dropped;
}
