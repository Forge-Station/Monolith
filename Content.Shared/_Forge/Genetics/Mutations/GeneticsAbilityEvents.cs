using Content.Shared.Actions;

namespace Content.Shared._Forge.Genetics.Mutations;

public sealed partial class GeneticTelekinesisActionEvent : EntityTargetActionEvent;

/// <summary>
/// Fling a telekinetically held object toward a world point (force-gun style).
/// </summary>
public sealed partial class GeneticTelekinesisFlingActionEvent : WorldTargetActionEvent;

public sealed partial class GeneticTelepathyActionEvent : EntityTargetActionEvent;

public sealed partial class GeneticSlimeTransformActionEvent : InstantActionEvent;

public sealed partial class GeneticBoneWeaponActionEvent : InstantActionEvent;
