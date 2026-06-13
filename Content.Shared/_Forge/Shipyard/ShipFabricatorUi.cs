using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Forge.Shipyard;

[Serializable, NetSerializable]
public enum ShipFabricatorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ShipFabricatorBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly bool Fabricating;
    public readonly float Progress;
    public readonly ProtoId<VesselPrototype>? Vessel;
    public readonly string? VesselName;
    public readonly Dictionary<string, int> MaterialRequirements;
    public readonly Dictionary<string, int> MaterialStored;
    public readonly bool HasBlueprint;
    public readonly TimeSpan RemainingTime;

    public ShipFabricatorBoundUserInterfaceState(
        bool fabricating,
        float progress,
        ProtoId<VesselPrototype>? vessel,
        string? vesselName,
        Dictionary<string, int> materialRequirements,
        Dictionary<string, int> materialStored,
        bool hasBlueprint,
        TimeSpan remainingTime)
    {
        Fabricating = fabricating;
        Progress = progress;
        Vessel = vessel;
        VesselName = vesselName;
        MaterialRequirements = materialRequirements;
        MaterialStored = materialStored;
        HasBlueprint = hasBlueprint;
        RemainingTime = remainingTime;
    }
}

[Serializable, NetSerializable]
public sealed class ShipFabricatorStartMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ShipFabricatorEjectBlueprintMessage : BoundUserInterfaceMessage;
