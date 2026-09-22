using Content.Server.Explosion.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Explosion.Components;

[RegisterComponent, Access(typeof(TriggerSystem))]
public sealed partial class SpawnOnTriggerComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("proto", required: true, customTypeSerializer:typeof(ProtoId<EntityPrototype>))]
    public string Proto = string.Empty;
}
