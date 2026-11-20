using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Animations;

[DataRecord]
[Serializable, NetSerializable]
public readonly partial record struct RMCAnimationTrack(object? LayerKey, List<RMCKeyFrame> KeyFrames);
