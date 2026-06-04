// Author: @lenta313. Все права не защищены / No rights reserved.
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// Wall-mounted "КПБ connector". When used, transfers the user's mind into a linked
/// <see cref="SoulkillerComponent"/> shell (via the engine's mind-visit mechanic), turning the
/// user into a remotely-controlled AI body. The user's real body stays put and can be returned to.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedSoulkillerSystem))]
public sealed partial class SoulkillerConnectorComponent : Component
{
    /// <summary>
    /// Explicitly linked shell. If null, the system tries to find the nearest free
    /// <see cref="SoulkillerComponent"/> within <see cref="LinkRange"/>.
    /// </summary>
    [DataField]
    public EntityUid? LinkedSoulkiller;

    /// <summary>
    /// Auto-link search radius (in tiles) when <see cref="LinkedSoulkiller"/> is not set.
    /// </summary>
    [DataField]
    public float LinkRange = 30f;
}
