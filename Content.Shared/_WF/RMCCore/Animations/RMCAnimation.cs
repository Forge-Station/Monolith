using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Animations;

[DataRecord]
[Serializable, NetSerializable]
public readonly partial record struct RMCAnimation(TimeSpan Length, List<RMCAnimationTrack> AnimationTracks);
