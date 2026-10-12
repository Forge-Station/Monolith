using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared.Mind.Components;

[RegisterComponent]
public sealed partial class TransferMindOnGibComponent : Component
{
    [DataField("targetTag", customTypeSerializer: typeof(ProtoId<TagPrototype>))]
    public string TargetTag = "MindTransferTarget";
}
