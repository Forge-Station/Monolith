using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Line;

[Serializable, NetSerializable]
public readonly record struct LineTile(MapCoordinates Coordinates, TimeSpan At);
