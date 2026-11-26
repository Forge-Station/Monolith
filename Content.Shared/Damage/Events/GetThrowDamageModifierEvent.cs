namespace Content.Shared.Damage.Events;

/// <summary>
/// Raised on a thrown entity before its throw damage is applied, so other systems can scale it.
/// </summary>
[ByRefEvent]
public record struct GetThrowDamageModifierEvent(float Multiplier = 1f, bool Consume = false);
