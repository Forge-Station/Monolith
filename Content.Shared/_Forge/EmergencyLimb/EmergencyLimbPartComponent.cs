// Author: @lenta313. Все права не защищены / No rights reserved.
namespace Content.Shared._Forge.EmergencyLimb;

/// <summary>
/// Marks a body part as an emergency limb (custom metal on-body sprite via baseLayerId). Used to
/// reset the body's custom base layer back to default when the limb is detached, so a replacement
/// limb doesn't keep showing the metal texture.
/// </summary>
[RegisterComponent]
public sealed partial class EmergencyLimbPartComponent : Component
{
}
