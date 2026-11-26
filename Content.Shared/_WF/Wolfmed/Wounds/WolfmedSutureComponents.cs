using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// M2 (P20): a healing item that closes a wound with a suture. The wounds it treats count as sutured for infection,
/// which reads the profile's Sutured rate instead of the untreated one.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSutureComponent : Component;

/// <summary>
/// Playtest 5: a healing item that is not clean, like the makeshift suture. Every open wound it treats is left
/// contaminated (WolfmedInfectionSystem.ContaminateTreated), so it infects faster and has a reason to under a sealed suit.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedDirtyTreatmentComponent : Component;

/// <summary>
/// M2 (P20): this wound was sutured. Infection treats it as closed at the profile's Sutured rate until the wound grows
/// wolfmed.suture_treatment_lost_severity past where it was sutured.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedSuturedComponent : Component
{
    /// <summary>The wound's severity when the suture went in.</summary>
    [ViewVariables]
    public FixedPoint2 TreatedSeverity;
}
