using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.ShipShields;

/// <summary>
/// While active, collisions with the given shield bubble are skipped so the projectile can exit the shell after a partial absorb.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShieldBubblePassImmunityComponent : Component
{
    /// <summary>
    /// The ship shield entity to ignore for a few contact evaluations.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid ShieldBubble;

    [DataField, AutoNetworkedField]
    public int TicksRemaining;
}
