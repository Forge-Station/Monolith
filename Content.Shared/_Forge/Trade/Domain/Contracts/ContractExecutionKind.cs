using Robust.Shared.Serialization;


namespace Content.Shared._Forge.Trade;


[Serializable, NetSerializable,]
public enum ContractExecutionKind : byte
{
    InventoryDelivery = 0,
    TrackedDeliveryObjective = 1,
    RetrievalRouteDelivery = 2,
    HuntObjective = 3,
    GhostRoleObjective = 4,
    ArtifactStudyObjective = 5,
    DroneHuntObjective = 6
}
