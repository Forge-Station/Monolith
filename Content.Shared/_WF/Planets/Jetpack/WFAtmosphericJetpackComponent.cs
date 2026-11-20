using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Planets.Jetpack;

/// <summary>
/// A jetpack that burns welding fuel to fly in a planet's atmosphere, the ground and air layers where a gas jetpack
/// cannot hold anyone up. It needs air to burn, so it is dead weight in orbit, and it cannot lift against heavy gravity.
/// Movement stays with <see cref="Content.Shared.Movement.Components.JetpackComponent"/>; this only decides where
/// the pack lights, what it burns and how it refuels.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFAtmosphericJetpackComponent : Component
{
    /// <summary>Name of the fuel solution on the pack.</summary>
    [DataField, AutoNetworkedField]
    public string FuelSolutionName = "fuel";

    /// <summary>The only reagent the pack burns; a tank holding anything else is refused.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ReagentPrototype> FuelReagent = "WeldingFuel";

    /// <summary>Fuel burnt per second while hovering in place.</summary>
    [DataField, AutoNetworkedField]
    public float HoverUsage = 1f;

    /// <summary>Fuel burnt per second while moving under power, sideways or between layers.</summary>
    [DataField, AutoNetworkedField]
    public float ThrustUsage = 2f;

    /// <summary>Surface gravity in gees above which the pack cannot lift its wearer at all.</summary>
    [DataField, AutoNetworkedField]
    public float MaxGravity = 1.5f;

    /// <summary>Surface gravity in gees above which the burn is scaled by <see cref="HeavyUsageMultiplier"/>.</summary>
    [DataField, AutoNetworkedField]
    public float HeavyGravity = 1f;

    [DataField, AutoNetworkedField]
    public float HeavyUsageMultiplier = 1.25f;

    /// <summary>Burn is taken out of the tank in steps of this many units, so the solution is not touched every tick.</summary>
    [DataField, AutoNetworkedField]
    public float BurnStep = 0.25f;

    [DataField, AutoNetworkedField]
    public SoundSpecifier RefillSound = new SoundPathSpecifier("/Audio/Effects/refill.ogg");

    /// <summary>Fuel burnt since the tank was last drawn on; server only.</summary>
    [ViewVariables]
    public float Burned;
}
