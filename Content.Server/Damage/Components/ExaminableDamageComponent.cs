using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Damage.Components;

/// <summary>
///     This component shows entity damage severity when it is examined by player.
/// </summary>
[RegisterComponent]
public sealed partial class ExaminableDamageComponent : Component
{
    [DataField("messages", required: true, customTypeSerializer:typeof(ProtoId<ExaminableDamagePrototype>))]
    public string? MessagesProtoId;

    public ExaminableDamagePrototype? MessagesProto;
}
