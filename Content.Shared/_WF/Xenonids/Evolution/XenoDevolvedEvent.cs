namespace Content.Shared._WF.Xenonids.Evolution;

[ByRefEvent]
public readonly record struct XenoDevolvedEvent(EntityUid OldXeno, EntityUid NewXeno);
