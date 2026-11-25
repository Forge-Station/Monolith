using Content.Shared.Actions;

namespace Content.Shared._Forge.Weapons.MeleeStance;

/// <summary>
/// Raised by the switch action on the weapon.
/// </summary>
public sealed partial class MeleeStanceSwitchActionEvent : InstantActionEvent;

/// <summary>
/// Raised locally on the weapon after a stance was applied (on both client and server).
/// </summary>
/// <param name="Previous">Previously applied stance, null on first application.</param>
/// <param name="Current">The new stance index.</param>
/// <param name="User">Who switched it, if anyone.</param>
[ByRefEvent]
public readonly record struct MeleeStanceChangedEvent(int? Previous, int Current, EntityUid? User);

/// <summary>
/// Raised on the weapon before switching. Set <see cref="Cancelled"/> to block the switch.
/// </summary>
[ByRefEvent]
public record struct MeleeStanceSwitchAttemptEvent(EntityUid? User, int Current, int Next, bool Cancelled = false);
