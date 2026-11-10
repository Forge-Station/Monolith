using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Appearance key for how worn the frame is.</summary>
[Serializable, NetSerializable]
public enum VehicleFrameDamageVisuals : byte
{
    IntegrityFraction,
}

/// <summary>Sprite layer keys of the frame damage visual.</summary>
public static class VehicleFrameDamageLayers
{
    public const string DamagedFrame = "damaged_frame";
}
