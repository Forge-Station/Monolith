namespace Content.Shared._Forge.Weapons.Events;

/// <summary>
/// Raised on a ballistic gun before it cycles a round out of the tube.
/// </summary>
[ByRefEvent]
public readonly record struct BallisticBeforeCycleEvent;
