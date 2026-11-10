using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Key of the weapons window.</summary>
[Serializable, NetSerializable]
public enum VehicleWeaponsUiKey : byte
{
    Key,
}

/// <summary>One mounted slot as the weapons window shows it.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class VehicleWeaponsUiEntry
{
    [DataField]
    public string SlotId;

    [DataField]
    public string HardpointType;

    [DataField]
    public NetEntity? MountedEntity;

    [DataField]
    public string? InstalledName;

    [DataField]
    public NetEntity? InstalledEntity;

    [DataField]
    public bool HasItem;

    [DataField]
    public bool Selectable;

    [DataField]
    public bool Selected;

    [DataField]
    public int AmmoCount;

    [DataField]
    public int AmmoCapacity;

    [DataField]
    public bool HasAmmo;

    [DataField]
    public int MagazineSize;

    [DataField]
    public int StoredMagazines;

    [DataField]
    public int MaxStoredMagazines;

    [DataField]
    public bool HasMagazineData;

    [DataField]
    public string? OperatorName;

    [DataField]
    public bool OperatorIsSelf;

    [DataField]
    public float Integrity;

    [DataField]
    public float MaxIntegrity;

    [DataField]
    public bool HasIntegrity;

    [DataField]
    public float CooldownRemaining;

    [DataField]
    public float CooldownTotal;

    [DataField]
    public bool IsOnCooldown;

    public VehicleWeaponsUiEntry()
    {
        SlotId = string.Empty;
        HardpointType = string.Empty;
    }

    public VehicleWeaponsUiEntry(
        string slotId,
        string hardpointType,
        NetEntity? mountedEntity,
        string? installedName,
        NetEntity? installedEntity,
        bool hasItem,
        bool selectable,
        bool selected,
        int ammoCount,
        int ammoCapacity,
        bool hasAmmo,
        int magazineSize,
        int storedMagazines,
        int maxStoredMagazines,
        bool hasMagazineData,
        string? operatorName,
        bool operatorIsSelf,
        float integrity,
        float maxIntegrity,
        bool hasIntegrity,
        float cooldownRemaining,
        float cooldownTotal,
        bool isOnCooldown)
    {
        SlotId = slotId;
        HardpointType = hardpointType;
        MountedEntity = mountedEntity;
        InstalledName = installedName;
        InstalledEntity = installedEntity;
        HasItem = hasItem;
        Selectable = selectable;
        Selected = selected;
        AmmoCount = ammoCount;
        AmmoCapacity = ammoCapacity;
        HasAmmo = hasAmmo;
        MagazineSize = magazineSize;
        StoredMagazines = storedMagazines;
        MaxStoredMagazines = maxStoredMagazines;
        HasMagazineData = hasMagazineData;
        OperatorName = operatorName;
        OperatorIsSelf = operatorIsSelf;
        Integrity = integrity;
        MaxIntegrity = maxIntegrity;
        HasIntegrity = hasIntegrity;
        CooldownRemaining = cooldownRemaining;
        CooldownTotal = cooldownTotal;
        IsOnCooldown = isOnCooldown;
    }
}

/// <summary>What the weapons window shows: every mounted slot and whether stabilisation can be switched.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class VehicleWeaponsUiState
{
    [DataField]
    public NetEntity Vehicle;

    [DataField]
    public List<VehicleWeaponsUiEntry> Hardpoints;

    [DataField]
    public bool CanToggleStabilization;

    [DataField]
    public bool StabilizationEnabled;

    public VehicleWeaponsUiState()
    {
        Hardpoints = new List<VehicleWeaponsUiEntry>();
    }

    public VehicleWeaponsUiState(
        NetEntity vehicle,
        List<VehicleWeaponsUiEntry> hardpoints,
        bool canToggleStabilization,
        bool stabilizationEnabled)
    {
        Vehicle = vehicle;
        Hardpoints = hardpoints;
        CanToggleStabilization = canToggleStabilization;
        StabilizationEnabled = stabilizationEnabled;
    }
}

/// <summary>The window's request to pick a hardpoint, or drop the pick.</summary>
[Serializable, NetSerializable]
public sealed class VehicleWeaponsSelectMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity? MountedEntity;

    public VehicleWeaponsSelectMessage(NetEntity? mountedEntity)
    {
        MountedEntity = mountedEntity;
    }
}

/// <summary>The window's request to switch turret stabilisation.</summary>
[Serializable, NetSerializable]
public sealed class VehicleWeaponsStabilizationMessage : BoundUserInterfaceMessage
{
    public readonly bool Enabled;

    public VehicleWeaponsStabilizationMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

/// <summary>Tells the window how long a refused pick has to wait.</summary>
[Serializable, NetSerializable]
public sealed class VehicleWeaponsCooldownFeedbackMessage : BoundUserInterfaceMessage
{
    public readonly float RemainingSeconds;

    public VehicleWeaponsCooldownFeedbackMessage(float remainingSeconds)
    {
        RemainingSeconds = remainingSeconds;
    }
}
