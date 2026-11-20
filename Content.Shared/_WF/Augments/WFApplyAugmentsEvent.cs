using Content.Shared.Preferences;

namespace Content.Shared._WF.Augments;

/// <summary>
/// Raised on a humanoid while its profile loads, after its biotype is applied and before the augments are drawn.
/// The server swaps in the augmented parts and organs.
/// </summary>
[ByRefEvent]
public readonly record struct WFApplyAugmentsEvent(HumanoidCharacterProfile Profile);
