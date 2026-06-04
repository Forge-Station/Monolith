// Author: @lenta313. Все права не защищены / No rights reserved.
namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// Placed on the brain spawned inside a Soulkiller core. Backlinks to the core so the return
/// action and unvisit cleanup can find it.
/// </summary>
[RegisterComponent]
[Access(typeof(SharedSoulkillerSystem))]
public sealed partial class SoulkillerInhabitantComponent : Component
{
    /// <summary>
    /// The Soulkiller core this brain belongs to.
    /// </summary>
    [DataField]
    public EntityUid Core;
}
