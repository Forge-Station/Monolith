namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>Where a reinforcement is in its life.</summary>
public enum WFReaverReinforcementPhase : byte
{
    /// <summary>Flying to the hub, not yet docked.</summary>
    Inbound,

    /// <summary>Docked to the hub.</summary>
    Docked,

    /// <summary>Holding beside the hub after docking failed.</summary>
    Holding,

    /// <summary>Broken away from the hub to attack.</summary>
    Sortie,

    /// <summary>Flying back to dock with the hub again.</summary>
    Returning,
}

/// <summary>A Reaver reinforcement, on its encounter entity: a ship that flies to a stronghold hub and docks to raise its level.</summary>
[RegisterComponent]
public sealed partial class WFReaverReinforcementComponent : Component
{
    /// <summary>The stronghold it reinforces.</summary>
    [ViewVariables]
    public EntityUid Stronghold;

    /// <summary>The level it brings the stronghold to.</summary>
    [ViewVariables]
    public int Level;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    [ViewVariables]
    public WFReaverReinforcementPhase Phase;

    /// <summary>Whether it has joined the stronghold, raising its level.</summary>
    [ViewVariables]
    public bool Landed;

    [ViewVariables]
    public TimeSpan Launched;

    /// <summary>When it is due at the hub; past that short of the hub it is lost, or ordered to dock again.</summary>
    [ViewVariables]
    public TimeSpan Overdue;

    /// <summary>The soonest it may be ordered to dock again after its crew gave up the flight.</summary>
    [ViewVariables]
    public TimeSpan NextOrders;

    /// <summary>Whether its crew ran out of docking attempts.</summary>
    [ViewVariables]
    public bool DockFailed;

    /// <summary>The player ship it broke away to attack.</summary>
    [ViewVariables]
    public EntityUid? Target;
}
