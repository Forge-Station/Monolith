using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Turrets;

[Serializable, NetSerializable]
public enum TurretCommandUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum TurretIffSide : byte
{
    Clear = 0,
    Friendly = 1,
    Hostile = 2,
}

/// <summary>
/// Neutral spares everyone except aggressive mobs.
/// Hostile shoots everyone except the friendly list.
/// Friendly spares everyone, including the hostile list, but still shoots aggressive mobs.
/// </summary>
[Serializable, NetSerializable]
public enum TurretDoctrine : byte
{
    Neutral = 0,
    Hostile = 1,
    Friendly = 2,
}

[Serializable, NetSerializable]
public sealed class TurretCommandConsoleState : BoundUserInterfaceState
{
    public bool HasServer;
    public bool HasGrid;
    public NetEntity Grid;
    public TurretDoctrine Doctrine;
    public Color Accent = Color.FromHex("#c87820");
    public List<TurretCommandTurretEntry> Turrets = new();
    public bool PlayersScanned;
    public List<TurretCommandPersonEntry> Present = new();
    public List<TurretCommandPersonEntry> Friendly = new();
    public List<TurretCommandPersonEntry> Hostile = new();
}

[Serializable, NetSerializable]
public sealed class TurretCommandTurretEntry
{
    public NetEntity Entity;
    public string Name = string.Empty;
    public bool Enabled;
    public bool Jammed;
    public bool MagazineLocked;
    public int Ammo;
    public int AmmoCapacity;
    public TurretDoctrine Doctrine;

    /// <summary>
    /// Position on the console's grid. People are not included.
    /// </summary>
    public Vector2 Position;
}

[Serializable, NetSerializable]
public sealed class TurretCommandPersonEntry
{
    public string Key = string.Empty;
    public string Name = string.Empty;
}

[Serializable, NetSerializable]
public sealed class TurretCommandToggleTurretMessage : BoundUserInterfaceMessage
{
    public NetEntity Turret;
    public bool Enabled;
}

[Serializable, NetSerializable]
public sealed class TurretCommandAddNameMessage : BoundUserInterfaceMessage
{
    public string Name = string.Empty;
    public TurretIffSide Side;
}

[Serializable, NetSerializable]
public sealed class TurretCommandClearPersonMessage : BoundUserInterfaceMessage
{
    public string Key = string.Empty;
}

[Serializable, NetSerializable]
public sealed class TurretCommandRefreshMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class TurretCommandSetDoctrineMessage : BoundUserInterfaceMessage
{
    public TurretDoctrine Doctrine;
}

[Serializable, NetSerializable]
public sealed class TurretCommandSetTurretDoctrineMessage : BoundUserInterfaceMessage
{
    public NetEntity Turret;
    public TurretDoctrine Doctrine;
}

[Serializable, NetSerializable]
public sealed class TurretCommandSetMagazineLockMessage : BoundUserInterfaceMessage
{
    public NetEntity Turret;
    public bool Locked;
}
