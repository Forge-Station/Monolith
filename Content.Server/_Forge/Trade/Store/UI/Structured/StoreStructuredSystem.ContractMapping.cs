using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

public sealed partial class StoreStructuredSystem
{
    private ContractClientData MapContractToClient(
        EntityUid store,
        ContractServerData contract,
        ContractProgressPreview? preview
    )
    {
        var targets = MapContractTargetsToClient(contract, preview);
        var rewards = CloneContractRewards(contract);

        return new ContractClientData(
            contract.Id,
            contract.Name,
            contract.Icon,
            contract.Description,
            contract.Repeatable,
            contract.Taken,
            SupportsContractPinpointer(contract),
            preview?.PartialTurnInAvailable ?? _contracts.CanPartiallyTurnInNow(store, contract.Id, contract),
            contract.ExecutionKind,
            CloneRuntimeContext(preview?.Runtime ?? contract.Runtime),
            preview?.FlowStatus ?? contract.FlowStatus,
            preview?.Completed ?? contract.Completed,
            preview?.TargetItem ?? contract.TargetItem,
            contract.MatchMode,
            ResolveContractTurnInItem(contract),
            preview?.Required ?? contract.Required,
            preview?.Progress ?? contract.Progress,
            targets,
            rewards,
            contract.Config.RetrievalSourceHint,
            contract.Config.RetrievalDestinationHint,
            IsRetrievalRouteContract(contract),
            contract.Config.RetrievalClaimMode,
            IsRetrievalBearerProofContract(contract),
            contract.Config.HuntCompletionMode,
            contract.Config.GhostRoleCompletionMode,
            contract.OfferPoolId,
            contract.OfferPoolName,
            contract.OfferPoolOrder,
            contract.OfferPoolColor
        );
    }

    private static List<ContractTargetClientData> MapContractTargetsToClient(
        ContractServerData contract,
        ContractProgressPreview? preview
    )
    {
        var sourceTargets = contract.Targets;
        var targets = sourceTargets is { Count: > 0 }
            ? new List<ContractTargetClientData>(sourceTargets.Count)
            : new List<ContractTargetClientData>(1);

        if (sourceTargets is { Count: > 0 })
        {
            for (var i = 0; i < sourceTargets.Count; i++)
            {
                var target = sourceTargets[i];
                if (target == null || string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
                    continue;

                targets.Add(
                    new ContractTargetClientData(
                        target.TargetItem,
                        target.Required,
                        GetPreviewTargetProgress(preview, i, target.Progress))
                    {
                        MatchMode = target.MatchMode,
                        Solution = target.Solution,
                        ReagentAmount = target.ReagentAmount,
                    });
            }

            return targets;
        }

        var targetItem = preview?.TargetItem ?? contract.TargetItem;
        var required = preview?.Required ?? contract.Required;
        if (!string.IsNullOrWhiteSpace(targetItem) && required > 0)
        {
            targets.Add(
                new ContractTargetClientData(
                    targetItem,
                    required,
                    preview?.Progress ?? contract.Progress)
                {
                    MatchMode = contract.MatchMode,
                });
        }

        return targets;
    }

    private static List<ContractRewardData> CloneContractRewards(ContractServerData contract)
    {
        var rewards = contract.Rewards;
        return rewards.Count > 0
            ? new List<ContractRewardData>(rewards)
            : new List<ContractRewardData>(0);
    }

    private static string ResolveContractTurnInItem(ContractServerData contract)
    {
        var config = contract.Config;
        if (contract.IsHuntObjective &&
            config.HuntEnabled &&
            config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn)
            return config.HuntBodyPrototype ?? string.Empty;

        return config.ProofPrototype ?? string.Empty;
    }

    private static bool SupportsContractPinpointer(ContractServerData contract)
    {
        var config = contract.Config;
        if (!config.GivePinpointer)
            return false;

        if (SupportsRetrievalSpawnedPinpointer(contract))
            return true;

        return contract.UsesWorldObjectiveRuntime;
    }

    private static bool SupportsRetrievalSpawnedPinpointer(ContractServerData contract)
    {
        var config = contract.Config;
        return (contract.IsInventoryDelivery || contract.IsRetrievalRouteDelivery) &&
               config.RetrievalSpawnEnabled &&
               config.RetrievalRequireSpawnedEntities;
    }

    private static bool IsRetrievalRouteContract(ContractServerData contract)
    {
        return (contract.IsInventoryDelivery || contract.IsRetrievalRouteDelivery) &&
               !string.IsNullOrWhiteSpace(contract.Config.RetrievalRouteId);
    }

    private static bool IsRetrievalBearerProofContract(ContractServerData contract)
    {
        var config = contract.Config;
        return IsRetrievalRouteContract(contract) &&
               config.RetrievalProofEnabled &&
               config.RetrievalProofOwnership == NcRetrievalProofOwnership.Bearer;
    }

    private static ContractRuntimeContextData CloneRuntimeContext(ContractRuntimeContextData? runtime)
    {
        if (runtime == null)
            return new ContractRuntimeContextData();

        return new ContractRuntimeContextData
        {
            Stage = runtime.Stage,
            StageGoal = runtime.StageGoal,
            AcceptTimeoutRemainingSeconds = runtime.AcceptTimeoutRemainingSeconds,
            GhostRoleSurvivalRemainingSeconds = runtime.GhostRoleSurvivalRemainingSeconds,
            GhostRolePendingAcceptance = runtime.GhostRolePendingAcceptance,
            Failed = runtime.Failed,
            Outcome = runtime.Outcome,
            FailureReason = runtime.FailureReason,
            StatusHint = runtime.StatusHint,
        };
    }
}
