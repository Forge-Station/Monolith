namespace Content.Server._WF.PlanetNpcGear;

/// <summary>On an NPC dressed for the world he stands on, and the tank he breathes from there, if he needs one.</summary>
[RegisterComponent, Access(typeof(WFPlanetNpcGearSystem))]
public sealed partial class WFPlanetNpcGearComponent : Component
{
    /// <summary>The map he was dressed for; on another one he is looked at again.</summary>
    [ViewVariables]
    public EntityUid Map;

    /// <summary>The tank put on him, kept topped up while he wears it.</summary>
    [ViewVariables]
    public EntityUid? Tank;
}
