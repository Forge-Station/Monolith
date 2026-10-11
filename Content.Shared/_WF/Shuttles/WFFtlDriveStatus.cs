using Robust.Shared.Serialization;

namespace Content.Shared._WF.Shuttles;

/// <summary>Whether a ship has an FTL drive that can jump, as the helm's FTL page reports it.</summary>
[Serializable, NetSerializable]
public enum WFFtlDriveStatus : byte
{
    /// <summary>The ship has no FTL drive.</summary>
    Missing,

    /// <summary>The ship has an FTL drive, but none of them has power.</summary>
    Unpowered,

    /// <summary>The ship has a powered FTL drive.</summary>
    Ready,
}
