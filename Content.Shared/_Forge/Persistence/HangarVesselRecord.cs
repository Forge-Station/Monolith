using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Persistence;

[Serializable, NetSerializable]
public enum HangarVesselState : byte
{
    InHangar,
    Deployed,
}

/// <summary>
/// Database metadata for a shuttle whose serialized grid is stored on disk.
/// </summary>
public sealed record HangarVesselRecord(
    Guid VesselId,
    Guid PlayerUserId,
    int CharacterSlot,
    string? VesselPrototypeId,
    string CustomName,
    string SavePath,
    HangarVesselState State,
    DateTime LastStored);

[Serializable, NetSerializable]
public sealed record HangarVesselCrewRecord(
    Guid VesselId,
    Guid PlayerUserId,
    int CharacterSlot,
    string CharacterName);
