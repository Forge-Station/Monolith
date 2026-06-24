using JetBrains.Annotations;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Server._Forge.Body.Syndromes.Triggers;

[UsedImplicitly]
public sealed partial class DamageTrigger : SyndromeTrigger
{
    [DataField]
    public float DamageThreshold { get; set; } = 50f;

    // null = all damage
    [DataField]
    public string? DamageGroup { get; set; }

    [DataField]
    public float Severity { get; set; } = 1f;

    [DataField]
    public float Chance { get; set; } = 1f;

    [DataField]
    public float Cooldown { get; set; } = 0f;
}
