namespace Content.Shared._WF.Surgery;

/// <summary>Raised on a surgery singleton by the WOLFGATE hook in SharedSurgerySystem.IsSurgeryValid, so a surgery can refuse a surgeon.</summary>
[ByRefEvent]
public record struct SurgeryUserValidEvent(EntityUid User, EntityUid Body, EntityUid Part, bool Cancelled = false);
