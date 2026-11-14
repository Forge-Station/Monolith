using Content.Shared.Damage;

namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Raised after modifiers and before damage is written. Clearing the dict suppresses the write.</summary>
/// <remarks>
/// P6: <see cref="Suppressed"/> is the other way to stop the write, for a handler that wants the caller to
/// keep seeing the damage it would have dealt. The server's routing clears the dict, because the body really
/// takes none of it; the client sets this flag, because the hit did land, it just is not the client's to
/// apply. See <c>Content.Client._WF.Wolfmed.Damage.WolfmedPredictedDamageSystem</c>.
/// </remarks>
[ByRefEvent]
public record struct DamageDealtEvent(
    DamageSpecifier Damage,
    EntityUid? Origin,
    bool InterruptsDoAfters,
    bool Suppressed = false);
