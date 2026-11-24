using Robust.Shared.GameStates;

namespace Content.Shared._WF.Synthetics;

/// <summary>
/// A synthetic-biotype body's chassis state: whether its bay is open and whether its servos are overclocked. Put on
/// every body the Synthetic biotype applies to; the chassis panel reads and writes it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFChassisComponent : Component
{
    /// <summary>The chassis bay is open: the components inside can be pulled and installed by hand.</summary>
    [DataField, AutoNetworkedField]
    public bool BayOpen;

    /// <summary>Servos run past their rating: faster, at the cost of charge and heat.</summary>
    [DataField, AutoNetworkedField]
    public bool Overclocked;

    /// <summary>Speed multiplier while overclocked.</summary>
    [DataField]
    public float OverclockSpeed = 1.25f;

    /// <summary>Speed multiplier while the bay is open and the plating is loose.</summary>
    [DataField]
    public float BayOpenSpeed = 0.8f;

    /// <summary>When the panel will next allow a reboot.</summary>
    [DataField]
    public TimeSpan NextReboot;
}

/// <summary>
/// A chassis part the panel has isolated: its servos are unpowered, so the limb does nothing, and its damage sensors
/// are off, so its wounds put no pain on the body. Removed when the panel restores it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFIsolatedPartComponent : Component
{
    /// <summary>What the part's CanEnable was before isolation, given back on restore.</summary>
    [DataField]
    public bool CouldEnable = true;
}

/// <summary>
/// A limb put back on a chassis by hand rather than by a mechanic: it works, badly, until its coupling is repaired.
/// Marker for the examine line and the panel; the penalty itself is the WFWolfmedLooseCouplingWound on the part.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFLooseLimbComponent : Component;
