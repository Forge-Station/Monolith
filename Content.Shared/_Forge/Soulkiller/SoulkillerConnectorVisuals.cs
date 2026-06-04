// Author: @lenta313. Все права не защищены / No rights reserved.
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// Appearance key for the Soulkiller connector capsule sprite.
/// </summary>
[Serializable, NetSerializable]
public enum SoulkillerConnectorVisuals : byte
{
    State,
}

/// <summary>
/// Sprite states for the Soulkiller connector capsule.
/// </summary>
[Serializable, NetSerializable]
public enum SoulkillerConnectorState : byte
{
    /// <summary>Lid open — ready for an operator to lie inside.</summary>
    Open,

    /// <summary>Lid closed, no active connection.</summary>
    Closed,

    /// <summary>Lid closed with an operator connected (animated).</summary>
    Active,
}
