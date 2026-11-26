using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// On a body part with an open frostbite wound. Carries the numbness upkeep and, once the freeze is deep
/// enough, the necrosis-risk flag W5's timer reads off the part without walking its wounds.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedFrostbiteComponent : Component
{
    /// <summary>Seconds since the last numbness top-up.</summary>
    [ViewVariables]
    public float Accumulator;

    /// <summary>
    /// How fast tissue in this part is dying, from the worst <c>WolfmedNecrosisRiskBehavior</c> on it.
    /// 0 means frozen but not yet at risk.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float NecrosisRisk;

    /// <summary>How long the part has to stay at risk before W5 should call it necrotic.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan NecrosisOnset;
}
