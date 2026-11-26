using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Animations;

[DataRecord]
[Serializable, NetSerializable]
public readonly partial record struct RMCKeyFrame(string State, float KeyTime);
