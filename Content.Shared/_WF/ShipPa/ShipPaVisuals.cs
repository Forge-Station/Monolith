using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipPa;

/// <summary>Appearance keys for a PA speaker.</summary>
[Serializable, NetSerializable]
public enum ShipPaSpeakerVisuals : byte
{
    State,
}

/// <summary>What a speaker is doing, in descending display priority.</summary>
[Serializable, NetSerializable]
public enum ShipPaSpeakerState : byte
{
    Unpowered,
    Idle,
    Broadcasting,
    Damaged,
    Broken,
}
