using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.ManageHive;

[Serializable, NetSerializable]
public record ManageHiveDevolveConfirmEvent(EntProtoId Choice);
