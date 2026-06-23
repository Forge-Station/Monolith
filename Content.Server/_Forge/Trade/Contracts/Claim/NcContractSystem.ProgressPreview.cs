using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

internal sealed class ContractProgressPreview
{
    public bool Completed;
    public ContractFlowStatus FlowStatus;
    public bool PartialTurnInAvailable;
    public int Progress;
    public int Required;
    public ContractRuntimeContextData Runtime = new();
    public string TargetItem = string.Empty;
    public readonly List<int> TargetProgress = new();
}

public sealed partial class NcContractSystem : EntitySystem
{
    internal void UpdateContractsProgressForUi(
        EntityUid store,
        NcStoreComponent comp,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool includeStoreWorldItems,
        Dictionary<string, ContractProgressPreview> previews
    )
    {
        previews.Clear();
        if (comp.Contracts.Count == 0)
            return;

        if (!_storesUpdatingProgress.Add(store))
        {
            Sawmill.Warning(
                $"[Contracts] Re-entrant UI progress update on {ToPrettyString(store)} skipped. " +
                "The same store is already updating progress.");
            return;
        }

        if (_progressScratchInUse)
        {
            _storesUpdatingProgress.Remove(store);
            Sawmill.Warning(
                $"[Contracts] Nested UI progress update on {ToPrettyString(store)} skipped. " +
                "Progress scratch is already in use by another update; check event handlers.");
            return;
        }

        _progressScratchInUse = true;
        try
        {
            var storeNearbyItemsPrepared = false;
            var hasCrateWork = crate is not null && crateItems is { Count: > 0 };
            PopulateProgressContractIds(comp);

            try
            {
                for (var i = 0; i < _progressContractIdsScratch.Count; i++)
                {
                    UpdateProgressForContractUi(
                        comp,
                        _progressContractIdsScratch[i],
                        store,
                        user,
                        userItems,
                        crate,
                        crateItems,
                        includeStoreWorldItems,
                        hasCrateWork,
                        previews,
                        ref storeNearbyItemsPrepared);
                }
            }
            finally
            {
                _progressContractIdsScratch.Clear();
            }
        }
        finally
        {
            _progressScratchInUse = false;
            _storesUpdatingProgress.Remove(store);
        }
    }

    private void UpdateProgressForContractUi(
        NcStoreComponent comp,
        string contractId,
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool includeStoreWorldItems,
        bool hasCrateWork,
        Dictionary<string, ContractProgressPreview> previews,
        ref bool storeNearbyItemsPrepared
    )
    {
        if (!TryGetProgressContract(comp, contractId, out var contract))
            return;

        if (ShouldBuildUserScopedProgressPreview(contract))
        {
            BuildUserScopedProgressPreview(
                store,
                contractId,
                contract,
                user,
                userItems,
                crate,
                crateItems,
                includeStoreWorldItems,
                hasCrateWork,
                previews,
                ref storeNearbyItemsPrepared);
            return;
        }

        UpdateProgressForContract(
            comp,
            contractId,
            store,
            user,
            userItems,
            crate,
            crateItems,
            includeStoreWorldItems,
            hasCrateWork,
            ref storeNearbyItemsPrepared);
    }

    private bool ShouldBuildUserScopedProgressPreview(ContractServerData contract)
    {
        if (contract.Runtime.Failed)
            return false;

        if (contract.IsInventoryDelivery)
            return true;

        if (contract.IsTrackedDeliveryObjective)
            return !UsesTrackedDeliveryDropoff(contract);

        return contract.IsArtifactStudyObjective || contract.IsDroneHuntObjective;
    }

    private void BuildUserScopedProgressPreview(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool includeStoreWorldItems,
        bool hasCrateWork,
        Dictionary<string, ContractProgressPreview> previews,
        ref bool storeNearbyItemsPrepared
    )
    {
        var contractSnapshot = CaptureContractProgressSnapshot(contract);
        var runtimeSnapshot = CaptureUserScopedRuntimeSnapshot(store, contractId, contract);
        var wasFailed = contract.Runtime.Failed;

        try
        {
            EnsureStoreNearbyProgressItems(
                store,
                contract,
                includeStoreWorldItems,
                ref storeNearbyItemsPrepared);
            var contractStoreNearbyItems = includeStoreWorldItems && contract.AllowsStoreWorldTurnIn
                ? _scratchStoreNearbyItems
                : null;

            if (TryUpdateContractProgressByExecutionKind(store, contractId, contract, userItems, crateItems))
            {
                previews[contractId] = CaptureContractProgressPreview(store, contractId, contract);
                return;
            }

            if (TryUpdateRetrievalSpawnedProgress(
                    store,
                    contractId,
                    contract,
                    user,
                    userItems,
                    crate,
                    crateItems,
                    contractStoreNearbyItems,
                    hasCrateWork,
                    failIfTrackedCargoLost: false))
            {
                ApplyPartialTurnInProgress(store, contractId, contract);
                previews[contractId] = CaptureContractProgressPreview(store, contractId, contract);
                return;
            }

            UpdateContractProgressForSingleContract(
                contract,
                store,
                user,
                userItems,
                crate,
                crateItems,
                contractStoreNearbyItems,
                hasCrateWork);
            ApplyPartialTurnInProgress(store, contractId, contract);
            previews[contractId] = CaptureContractProgressPreview(store, contractId, contract);
        }
        finally
        {
            if (wasFailed || !contract.Runtime.Failed)
            {
                contractSnapshot.Restore(contract);
                runtimeSnapshot.Restore();
            }
        }
    }

    private ContractProgressPreview CaptureContractProgressPreview(
        EntityUid store,
        string contractId,
        ContractServerData contract
    )
    {
        var preview = new ContractProgressPreview
        {
            Required = contract.Required,
            Progress = contract.Progress,
            FlowStatus = contract.FlowStatus,
            Completed = contract.Completed,
            PartialTurnInAvailable = CanPartiallyTurnInNow(store, contractId, contract),
            TargetItem = contract.TargetItem,
            Runtime = CloneContractRuntime(contract.Runtime),
        };

        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
            preview.TargetProgress.Add(Math.Max(0, targets[i].Progress));

        return preview;
    }

    private ContractProgressSnapshot CaptureContractProgressSnapshot(ContractServerData contract)
    {
        var snapshot = new ContractProgressSnapshot
        {
            Required = contract.Required,
            Progress = contract.Progress,
            FlowStatus = contract.FlowStatus,
            TargetItem = contract.TargetItem,
            Runtime = CloneContractRuntime(contract.Runtime),
        };

        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
            snapshot.TargetProgress.Add(targets[i].Progress);

        return snapshot;
    }

    private UserScopedRuntimeSnapshot CaptureUserScopedRuntimeSnapshot(
        EntityUid store,
        string contractId,
        ContractServerData contract
    )
    {
        if (!contract.IsArtifactStudyObjective)
            return default;

        return _objectiveRuntime.ByContract.TryGetValue((store, contractId), out var state)
            ? new UserScopedRuntimeSnapshot(state, state.ArtifactStudyCompleted)
            : default;
    }

    private static ContractRuntimeContextData CloneContractRuntime(ContractRuntimeContextData runtime)
    {
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

    private static void CopyContractRuntime(
        ContractRuntimeContextData source,
        ContractRuntimeContextData target
    )
    {
        target.Stage = source.Stage;
        target.StageGoal = source.StageGoal;
        target.AcceptTimeoutRemainingSeconds = source.AcceptTimeoutRemainingSeconds;
        target.GhostRoleSurvivalRemainingSeconds = source.GhostRoleSurvivalRemainingSeconds;
        target.GhostRolePendingAcceptance = source.GhostRolePendingAcceptance;
        target.Failed = source.Failed;
        target.Outcome = source.Outcome;
        target.FailureReason = source.FailureReason;
        target.StatusHint = source.StatusHint;
    }

    private sealed class ContractProgressSnapshot
    {
        public ContractFlowStatus FlowStatus;
        public int Progress;
        public int Required;
        public ContractRuntimeContextData Runtime = new();
        public string TargetItem = string.Empty;
        public readonly List<int> TargetProgress = new();

        public void Restore(ContractServerData contract)
        {
            contract.Required = Required;
            contract.Progress = Progress;
            contract.FlowStatus = FlowStatus;
            contract.TargetItem = TargetItem;
            CopyContractRuntime(Runtime, contract.Runtime);

            var targets = GetEffectiveTargets(contract);
            for (var i = 0; i < targets.Count && i < TargetProgress.Count; i++)
            {
                var target = targets[i];
                target.Progress = TargetProgress[i];
                targets[i] = target;
            }
        }
    }

    private readonly record struct UserScopedRuntimeSnapshot(
        ObjectiveRuntimeState? State,
        bool ArtifactStudyCompleted
    )
    {
        public void Restore()
        {
            if (State == null)
                return;

            State.ArtifactStudyCompleted = ArtifactStudyCompleted;
        }
    }
}
