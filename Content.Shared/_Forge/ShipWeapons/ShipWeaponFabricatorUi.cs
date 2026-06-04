using Robust.Shared.Serialization;

namespace Content.Shared._Forge.ShipWeapons;

[Serializable, NetSerializable]
public enum ShipWeaponFabricatorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum ShipWeaponFabricatorVisuals : byte
{
    Fabricating,
}

[Serializable, NetSerializable]
public sealed class ShipWeaponFabricatorStartMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class ShipWeaponFabricatorEjectMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class ShipWeaponFabricatorEjectPartMessage : BoundUserInterfaceMessage
{
    public readonly string PartPrototype;
    public readonly int Amount;

    public ShipWeaponFabricatorEjectPartMessage(string partPrototype, int amount)
    {
        PartPrototype = partPrototype;
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public sealed class ShipWeaponFabricatorLoadedPartEntry
{
    public readonly string PartPrototype;
    public readonly string DisplayName;
    public readonly int Current;
    public readonly int Rating;
    public readonly bool Compatible;

    public ShipWeaponFabricatorLoadedPartEntry(string partPrototype, string displayName, int current, int rating, bool compatible)
    {
        PartPrototype = partPrototype;
        DisplayName = displayName;
        Current = current;
        Rating = rating;
        Compatible = compatible;
    }
}

[Serializable, NetSerializable]
public sealed class ShipWeaponFabricatorLoadedMaterialEntry
{
    public readonly string MaterialId;
    public readonly string DisplayName;
    public readonly int Current;
    public readonly int? Required;
    public readonly bool Compatible;

    public ShipWeaponFabricatorLoadedMaterialEntry(string materialId, string displayName, int current, int? required, bool compatible)
    {
        MaterialId = materialId;
        DisplayName = displayName;
        Current = current;
        Required = required;
        Compatible = compatible;
    }
}

[Serializable, NetSerializable]
public sealed class ShipWeaponFabricatorState : BoundUserInterfaceState
{
    public readonly string? BoardName;
    public readonly string? TargetName;
    public readonly string RequirementsText;
    public readonly string LoadedPartsText;
    public readonly List<ShipWeaponFabricatorLoadedMaterialEntry> LoadedMaterials;
    public readonly List<ShipWeaponFabricatorLoadedPartEntry> LoadedParts;
    public readonly string StatusText;
    public readonly string? TargetPrototypeId;
    public readonly bool CanStart;
    public readonly bool CanEject;
    public readonly bool IsFabricating;

    public ShipWeaponFabricatorState(
        string? boardName,
        string? targetName,
        string requirementsText,
        string loadedPartsText,
        List<ShipWeaponFabricatorLoadedMaterialEntry> loadedMaterials,
        List<ShipWeaponFabricatorLoadedPartEntry> loadedParts,
        string statusText,
        string? targetPrototypeId,
        bool canStart,
        bool canEject,
        bool isFabricating)
    {
        BoardName = boardName;
        TargetName = targetName;
        RequirementsText = requirementsText;
        LoadedPartsText = loadedPartsText;
        LoadedMaterials = loadedMaterials;
        LoadedParts = loadedParts;
        StatusText = statusText;
        TargetPrototypeId = targetPrototypeId;
        CanStart = canStart;
        CanEject = canEject;
        IsFabricating = isFabricating;
    }
}
