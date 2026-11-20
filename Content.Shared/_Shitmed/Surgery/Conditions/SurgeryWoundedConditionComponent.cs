using Content.Shared.Body.Part;
using Content.Shared.Damage.Prototypes; // WOLFGATE(Wolfmed): EXT 1
using Content.Shared.FixedPoint; // WOLFGATE(Wolfmed): EXT 1
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes; // WOLFGATE(Wolfmed): EXT 1

namespace Content.Shared._Shitmed.Medical.Surgery.Conditions;

[RegisterComponent, NetworkedComponent]
// WOLFGATE(Wolfmed) START: EXT 1, the P4-D19 wound-severity window a surgery lists in.
// public sealed partial class SurgeryWoundedConditionComponent : Component;
public sealed partial class SurgeryWoundedConditionComponent : Component
{
    /// <summary>Damage group the wound-severity window is measured over.</summary>
    [DataField] public ProtoId<DamageGroupPrototype> WoundGroup = "Brute"; // WOLFGATE(Wolfmed): EXT 1 - P4-D19

    /// <summary>Lowest wound severity in the group this surgery lists at. Null keeps the pre-Wolfmed behaviour.</summary>
    [DataField] public FixedPoint2? MinWoundSeverity; // WOLFGATE(Wolfmed): EXT 1 - P4-D19

    /// <summary>Highest wound severity in the group this surgery lists at. Null keeps the pre-Wolfmed behaviour.</summary>
    [DataField] public FixedPoint2? MaxWoundSeverity; // WOLFGATE(Wolfmed): EXT 1 - P4-D19
}
// WOLFGATE END
