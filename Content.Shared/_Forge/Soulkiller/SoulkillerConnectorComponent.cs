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
    /// The core this capsule is wired to (set via multitool device-link). Connection is only
    /// possible through this explicit link.
    /// </summary>
    [DataField]
    public EntityUid? LinkedSoulkiller;

    /// <summary>
    /// Time it takes to forcibly crack an occupied capsule open and rip the operator out.
    /// </summary>
    [DataField]
    public TimeSpan ExtractTime = TimeSpan.FromSeconds(30);
}
