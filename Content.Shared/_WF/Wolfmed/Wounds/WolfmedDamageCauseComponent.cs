using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Declares what kind of damage this entity deals when it is the tool of a hit. Merged with the causes
/// derived from the hit itself, so a buckshot pellet only has to declare <c>Fragment</c> and still counts
/// as a projectile. Several flags go in one scalar: <c>cause: "Unarmed, Bite"</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedDamageCauseComponent : Component
{
    [DataField(required: true)]
    public WolfmedWoundCause Cause;
}
