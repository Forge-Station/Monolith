// Author: @lenta313. Все права не защищены / No rights reserved.
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// A relay server. When linked to a Soulkiller core with a multitool (device-link port
/// <c>SoulkillerLink</c>), the inhabiting AI gets an action to jump its eye to the server's location —
/// letting it view and interact around the server (e.g. on a remote shuttle).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SoulkillerServerComponent : Component
{
}
