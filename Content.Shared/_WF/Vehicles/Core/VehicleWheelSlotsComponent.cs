using System;
using System.Collections.Generic;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Marks a hardpoint as a wheel or tread.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(VehicleWheelSystem))]
public sealed partial class VehicleWheelItemComponent : Component
{
    [DataField]
    public bool AcidResistant;
}

/// <summary>A vehicle's wheel slots and how hard a crash wears them.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(VehicleWheelSystem))]
public sealed partial class VehicleWheelSlotsComponent : Component
{
    public const string WheelComponentId = "VehicleWheelItem";

    public static readonly EntProtoId HardpointTypeId = "HardpointTypeWheel";

    [DataField]
    public int SlotCount = 1;

    [DataField]
    public List<string> Slots = new();

    [DataField]
    public string SlotPrefix = "wheel";

    [DataField]
    public EntityWhitelist WheelWhitelist = new()
    {
        Components = new[] { WheelComponentId },
    };

    [DataField]
    public float CollisionDamagePerSpeed = 0f;

    [DataField]
    public float MinCollisionDamage;
}

/// <summary>Appearance keys for the wheel layer.</summary>
[Serializable, NetSerializable]
public enum VehicleWheelVisuals : byte
{
    HasAllWheels,
    WheelCount,
    WheelFunctionalCount,
    WheelIntegrityFraction,
}

/// <summary>Sprite layer keys of the wheel visual.</summary>
public static class VehicleWheelLayers
{
    public const string Wheels = "rmc-wheels";
}
