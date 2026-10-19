using Content.Shared._WF.Xenonids.Construction;
using Content.Shared._WF.Xenonids.Hive;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.ManageHive.Boons;

[DataDefinition]
[ImplicitDataDefinitionForInheritors]
[Serializable, NetSerializable]
public partial class HiveBoonEvent
{
    [NonSerialized]
    public EntityUid Boon;

    [NonSerialized]
    public Entity<HiveComponent> Hive;

    [NonSerialized]
    public EntityUid? Core;
}
