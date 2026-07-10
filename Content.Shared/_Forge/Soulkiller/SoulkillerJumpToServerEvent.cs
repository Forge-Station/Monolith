// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared.Actions;

namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// Fired by the action granted to a Soulkiller AI to jump its eye to a linked
/// <see cref="SoulkillerServerComponent"/> (e.g. on a remote shuttle).
/// </summary>
public sealed partial class SoulkillerJumpToServerEvent : InstantActionEvent
{
}
