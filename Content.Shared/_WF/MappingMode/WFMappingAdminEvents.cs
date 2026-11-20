using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>An admin leaves the mapping lobby as an admin observer on the private admin map.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingAdminObserveRequest : EntityEventArgs;

/// <summary>An admin observer goes back to the mapping lobby.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingAdminLeaveRequest : EntityEventArgs;
