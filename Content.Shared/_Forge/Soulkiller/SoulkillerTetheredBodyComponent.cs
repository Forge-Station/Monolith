// Author: @lenta313. Все права не защищены / No rights reserved.
namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// Placed on the operator's real body while their mind inhabits a Soulkiller core. The body is
/// teleported to the core and anchored ("sucked onto" it) — it can't be pulled away. If the body
/// dies or enters crit, the connection breaks. Removed (and the body unanchored) on disconnect.
/// </summary>
[RegisterComponent]
[Access(typeof(SharedSoulkillerSystem))]
public sealed partial class SoulkillerTetheredBodyComponent : Component
{
    /// <summary>
    /// The Soulkiller core this body is tethered to.
    /// </summary>
    [DataField]
    public EntityUid Core;
}
