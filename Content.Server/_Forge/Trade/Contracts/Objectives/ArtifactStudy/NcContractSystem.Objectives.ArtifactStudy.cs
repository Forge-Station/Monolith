using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    [Dependency] private readonly ForgeArtifactStudyPresetSystem _forgeArtifactStudyPreset = default!;

    private bool TryInitializeArtifactStudyObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var targetProtoId = contract.Config.TargetPrototype;
        if (string.IsNullOrWhiteSpace(targetProtoId))
        {
            Sawmill.Warning(
                $"[Contracts] Artifact-study runtime init failed for '{contractId}': target artifact prototype is missing.");
            return false;
        }

        ResetObjectiveState(contract);

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, targetProtoId, false))
            return false;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid ||
            !TryComp(target, out ArtifactComponent? artifact))
        {
            Sawmill.Warning(
                $"[Contracts] Artifact-study runtime init failed for '{contractId}': spawned target is not an artifact.");
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        if (TryComp(target, out ForgeArtifactStudyPresetComponent? preset) &&
            !_forgeArtifactStudyPreset.ApplyPreset(target, preset))
        {
            Sawmill.Warning(
                $"[Contracts] Artifact-study runtime init failed for '{contractId}': Forge artifact preset could not be applied.");
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        state.ArtifactStudyCompleted = false;
        UpdateArtifactStudyRuntimeProgress(contract, state, artifact);
        return true;
    }

    private ClaimAttemptResult TryClaimArtifactStudyContract(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        EnsureObjectiveRuntimeDefaults(contract);

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return FailArtifactStudyObjective(key, comp, contract);

        if (!TryRefreshArtifactStudyProgressForClaim(store, user, contractId, contract, state, out var validationFail))
            return validationFail;

        if (!TryValidateContractRewards(user, contract.Rewards, out var rewardFail))
            return rewardFail;

        if (!TryGiveContractRewardsWithPreCommit(
                user,
                contract.Rewards,
                () =>
                {
                    if (!TryRefreshArtifactStudyProgressForClaim(
                            store,
                            user,
                            contractId,
                            contract,
                            state,
                            out var preCommitFail))
                        return preCommitFail;

                    return ClaimAttemptResult.Ok();
                },
                out var rewardExecFail))
            return rewardExecFail;

        FinalizeClaim(store, comp, contractId, contract.Repeatable);
        return ClaimAttemptResult.Ok();
    }

    private void SyncArtifactStudyObjectiveProgress(
        EntityUid store,
        string contractId,
        ContractServerData contract
    )
    {
        EnsureObjectiveRuntimeDefaults(contract);

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid)
        {
            SyncObjectiveProgressFromRuntime(contract);
            return;
        }

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return;
        }

        if (!TryComp(target, out ArtifactComponent? artifact))
        {
            SyncObjectiveProgressFromRuntime(contract);
            return;
        }

        if (TryComp(target, out TransformComponent? xform))
            state.LastKnownTargetCoordinates = xform.Coordinates;

        state.ArtifactStudyCompleted = state.ArtifactStudyCompleted &&
                                       IsArtifactFullyStudied(artifact) &&
                                       IsTrackedDeliveryTargetAtStore(store, target);
        UpdateArtifactStudyRuntimeProgress(contract, state, artifact);
    }

    private void RefreshArtifactStudyProgressFromSources(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems
    )
    {
        EnsureObjectiveRuntimeDefaults(contract);

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            !TryGetLiveArtifactStudyTarget(state, out var target, out var artifact))
        {
            SyncObjectiveProgressFromRuntime(contract);
            return;
        }

        if (TryComp(target, out TransformComponent? xform))
            state.LastKnownTargetCoordinates = xform.Coordinates;

        var inTurnInScope = ContainsTrackedDeliveryEntity(userItems, target) ||
                            ContainsTrackedDeliveryEntity(crateItems, target) ||
                            IsTrackedDeliveryTargetAtStore(store, target);

        state.ArtifactStudyCompleted = IsArtifactFullyStudied(artifact) && inTurnInScope;
        UpdateArtifactStudyRuntimeProgress(contract, state, artifact);
    }

    private void HandleArtifactStudyTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (contract.Completed)
            return;

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-artifact-study-target-lost"),
            deleteGuards: false);
    }

    private ClaimAttemptResult FailArtifactStudyObjective(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-artifact-study-target-lost"),
            deleteGuards: false);

        return ClaimAttemptResult.Fail(
            ClaimFailureReason.ObjectiveFailed,
            Loc.GetString("nc-store-contract-artifact-study-target-lost"));
    }

    private bool TryRefreshArtifactStudyProgressForClaim(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryGetLiveArtifactStudyTarget(state, out var target, out var artifact))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveFailed,
                Loc.GetString("nc-store-contract-artifact-study-target-lost"));
            return false;
        }

        ScanTrackedDeliveryTransferSources(user, out var userItems, out var crateEntity, out var crateItems);

        var fullyStudied = IsArtifactFullyStudied(artifact);
        var inUserInventory = ContainsTrackedDeliveryEntity(userItems, target);
        var inCrate = ContainsTrackedDeliveryEntity(crateItems, target);
        var atStore = IsTrackedDeliveryTargetAtStore(store, target);

        if (TryComp(target, out TransformComponent? xform))
            state.LastKnownTargetCoordinates = xform.Coordinates;

        state.ArtifactStudyCompleted = fullyStudied && (inUserInventory || inCrate || atStore);
        UpdateArtifactStudyRuntimeProgress(contract, state, artifact);

        if (!fullyStudied)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Artifact-study target for '{contractId}' is not fully studied yet.");
            return false;
        }

        if (!inUserInventory && !inCrate && !atStore)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Artifact-study target for '{contractId}' is not present in user inventory, pulled crate or at the store.");
            return false;
        }

        if (IsTrackedDeliveryProtectedFromDirectSale(user, target, crateEntity, inUserInventory, inCrate))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Artifact-study target for '{contractId}' is protected from direct sale.");
            return false;
        }

        return true;
    }

    private bool TryGetLiveArtifactStudyTarget(
        ObjectiveRuntimeState state,
        out EntityUid target,
        out ArtifactComponent artifact
    )
    {
        target = EntityUid.Invalid;
        artifact = default!;

        if (state.TargetEntity is not { } tracked ||
            tracked == EntityUid.Invalid ||
            TerminatingOrDeleted(tracked) ||
            !TryComp(tracked, out ArtifactComponent? artifactComp))
            return false;

        target = tracked;
        artifact = artifactComp;
        return true;
    }

    private void UpdateArtifactStudyRuntimeProgress(
        ContractServerData contract,
        ObjectiveRuntimeState state,
        ArtifactComponent artifact
    )
    {
        var total = Math.Max(1, artifact.NodeTree.Count);
        var triggered = CountTriggeredArtifactNodes(artifact);

        state.ArtifactStudyNodeTotal = total;
        state.ArtifactStudyTriggered = triggered;

        var runtime = contract.Runtime;
        runtime.StageGoal = total + 1;
        runtime.Stage = state.ArtifactStudyCompleted
            ? runtime.StageGoal
            : Math.Min(triggered, total);
        runtime.StatusHint = BuildArtifactStudyStatusHint(state, IsArtifactFullyStudied(artifact));

        SyncObjectiveProgressFromRuntime(contract);
    }

    private static int CountTriggeredArtifactNodes(ArtifactComponent artifact)
    {
        var count = 0;
        for (var i = 0; i < artifact.NodeTree.Count; i++)
        {
            if (artifact.NodeTree[i].Triggered)
                count++;
        }

        return count;
    }

    private static bool IsArtifactFullyStudied(ArtifactComponent artifact)
    {
        if (artifact.NodeTree.Count == 0)
            return false;

        for (var i = 0; i < artifact.NodeTree.Count; i++)
        {
            if (!artifact.NodeTree[i].Triggered)
                return false;
        }

        return true;
    }

    private string BuildArtifactStudyStatusHint(ObjectiveRuntimeState state, bool fullyStudied)
    {
        if (state.ArtifactStudyCompleted)
            return Loc.GetString("nc-store-contract-artifact-study-status-ready");

        var total = Math.Max(1, state.ArtifactStudyNodeTotal);
        var triggered = Math.Clamp(state.ArtifactStudyTriggered, 0, total);

        return fullyStudied
            ? Loc.GetString("nc-store-contract-artifact-study-status-bring")
            : Loc.GetString(
                "nc-store-contract-artifact-study-status-progress",
                ("triggered", triggered),
                ("total", total));
    }
}
