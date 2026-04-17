using Content.Shared.Actions;
using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Forge.Demon.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DamageOnActionComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public DamageSpecifier Damage = default!;

    [DataField("action", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string? Action = "InstantRegeneration";

    [DataField("actionEntity")]
    public EntityUid? ActionEntity;

    [DataField("hungerPerUse")]
    public float HungerPerUse = 35f;

    [DataField("cooldown")]
    public double Cooldown = 80.0;

    public TimeSpan Delay => TimeSpan.FromSeconds(Cooldown);
}

public sealed partial class DamageOnActionEvent : InstantActionEvent;
