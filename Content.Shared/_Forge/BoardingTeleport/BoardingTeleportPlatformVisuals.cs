using Robust.Shared.Serialization;

namespace Content.Shared._Forge.BoardingTeleport;

/// <summary>
/// Appearance data key for <see cref="BoardingTeleportPlatformState"/>.
/// </summary>
[Serializable, NetSerializable]
public enum BoardingTeleportPlatformVisuals
{
    State,
}

/// <summary>
/// Visual state: unpowered, idle pulse, or charging for jump.
/// </summary>
[Serializable, NetSerializable]
public enum BoardingTeleportPlatformState : byte
{
    Offline,
    Idle,
    Charging,
}
