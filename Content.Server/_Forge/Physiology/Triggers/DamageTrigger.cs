using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared._Forge.Physiology.Disease.Triggers;

namespace Content.Server._Forge.Physiology.Disease.Triggers;

[DataDefinition]
public sealed partial class DamageTrigger : DiseaseTrigger
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
