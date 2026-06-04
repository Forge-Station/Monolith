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
    public List<string> WeaponNames = new();
}

[Serializable, NetSerializable]
public struct GunneryWeaponPresetState
{
    public string Name;
    public string[] WeaponNames;

    public GunneryWeaponPresetState(string name, string[] weaponNames)
    {
        Name = name;
        WeaponNames = weaponNames;
    }
}
// Forge-Change-End
