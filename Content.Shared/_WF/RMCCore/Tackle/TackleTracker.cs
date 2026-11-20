using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Tackle;

[Serializable, NetSerializable]
public record struct TackleTracker(int Count, TimeSpan Last);
