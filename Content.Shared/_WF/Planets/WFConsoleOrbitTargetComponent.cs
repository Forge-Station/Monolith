using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>
/// What a shuttle console offers for planet orbit, swept server-side; not in the BUI state, which is pushed too rarely.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFConsoleOrbitTargetComponent : Component
{
    /// <summary>The sector body whose orbit this hull can enter right now, or null when none is in range.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Planet;

    /// <summary>Display name of <see cref="Planet"/>, so the button labels itself without resolving the body.</summary>
    [DataField, AutoNetworkedField]
    public string PlanetName = string.Empty;

    /// <summary>True when <see cref="Planet"/> is a world nobody is cleared to visit; entering its orbit asks first.</summary>
    [DataField, AutoNetworkedField]
    public bool Unsanctioned;

    /// <summary>True when the hull is parked on a planet orbit layer, which is what offers "leave orbit" instead.</summary>
    [DataField, AutoNetworkedField]
    public bool InOrbit;

    /// <summary>True while the hull cannot make the hop at all: already in FTL, in cooldown or riding a transit map.</summary>
    [DataField, AutoNetworkedField]
    public bool Busy;

    /// <summary>True while this hull is grounded on a planet layer; blockers are left out so the server can explain a refusal.</summary>
    [DataField, AutoNetworkedField]
    public bool LiftoffAvailable;

    /// <summary>True while the server is feeding latched upward input to this hull.</summary>
    [DataField, AutoNetworkedField]
    public bool LiftoffActive;

    /// <summary>Prospective atmosphere thrust over hull weight at this world's gravity; 1 is level flight. Only meaningful in orbit.</summary>
    [DataField, AutoNetworkedField]
    public float LiftRatio;

    /// <summary>Prospective atmosphere power demand in watts. Only meaningful while in orbit.</summary>
    [DataField, AutoNetworkedField]
    public float AtmospherePowerDemand;

    /// <summary>True when prospective atmosphere power demand exceeds available power.</summary>
    [DataField, AutoNetworkedField]
    public bool AtmospherePowerDeficit;

    /// <summary>Seconds until this hull's orbit decays into the atmosphere, or -1 while holding station; only meaningful in orbit.</summary>
    [DataField, AutoNetworkedField]
    public float DecaySeconds = -1f;
}
