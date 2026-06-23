namespace Content.Shared._Forge.Trade;


public static class ContractExecutionKinds
{
    public static ContractObjectiveType ToObjectiveType(ContractExecutionKind kind) =>
        kind switch
        {
            ContractExecutionKind.HuntObjective => ContractObjectiveType.Hunt,
            ContractExecutionKind.DroneHuntObjective => ContractObjectiveType.Hunt,
            ContractExecutionKind.GhostRoleObjective => ContractObjectiveType.GhostRole,
            ContractExecutionKind.ArtifactStudyObjective => ContractObjectiveType.ArtifactStudy,
            _ => ContractObjectiveType.Delivery
        };

    public static bool UsesWorldRuntime(ContractExecutionKind kind) => kind != ContractExecutionKind.InventoryDelivery;

    public static bool UsesStageProgress(ContractExecutionKind kind) =>
        kind is ContractExecutionKind.HuntObjective
            or ContractExecutionKind.DroneHuntObjective
            or ContractExecutionKind.GhostRoleObjective
            or ContractExecutionKind.ArtifactStudyObjective;
}
