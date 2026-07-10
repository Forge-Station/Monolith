// Author: @lenta313. Все права не защищены / No rights reserved.
namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// Shared base for the Soulkiller mind-visit mechanic. Logic lives in the server system;
/// this exists so component <c>[Access]</c> attributes resolve on both sides.
/// </summary>
public abstract class SharedSoulkillerSystem : EntitySystem
{
}
