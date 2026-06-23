using System.Numerics; //
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.FireControl;

// Forge-Change-Start: per-console weapon preset lists persisted on the map entity.
/// <summary>
/// Weapon selection preset stored on a gunnery console entity.
/// </summary>
[DataDefinition]
public sealed partial class GunneryWeaponPresetData
{
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public List<GunneryWeaponPresetWeaponData> Weapons = new();

    /// <summary>
    /// Legacy name-only presets. Migrated to <see cref="Weapons"/> on load when empty.
    /// </summary>
    [DataField]
    public List<string> WeaponNames = new();
}

[DataDefinition]
public sealed partial class GunneryWeaponPresetWeaponData
{
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public NetEntity WeaponEntity;

    [DataField]
    public bool HasWeaponEntity;

    [DataField]
    public Vector2 GridPosition;

    [DataField]
    public bool HasGridPosition;
}

[Serializable, NetSerializable]
public struct GunneryWeaponPresetWeaponState
{
    public string Name;
    public NetEntity WeaponEntity;
    public bool HasWeaponEntity;
    public Vector2 GridPosition;
    public bool HasGridPosition;

    public GunneryWeaponPresetWeaponState(
        string name,
        NetEntity weaponEntity,
        bool hasWeaponEntity,
        Vector2 gridPosition,
        bool hasGridPosition)
    {
        Name = name;
        WeaponEntity = weaponEntity;
        HasWeaponEntity = hasWeaponEntity;
        GridPosition = gridPosition;
        HasGridPosition = hasGridPosition;
    }
}

[Serializable, NetSerializable]
public struct GunneryWeaponPresetState
{
    public string Name;
    public GunneryWeaponPresetWeaponState[] Weapons;

    public GunneryWeaponPresetState(string name, GunneryWeaponPresetWeaponState[] weapons)
    {
        Name = name;
        Weapons = weapons;
    }
}
// Forge-Change-End
