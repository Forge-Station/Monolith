namespace Content.Shared._WF.Xenonids.Evolution;

[ByRefEvent]
public readonly record struct NewXenoEvolvedEvent(Entity<XenoEvolutionComponent> OldXeno, EntityUid NewXeno, bool SubtractPoints);
