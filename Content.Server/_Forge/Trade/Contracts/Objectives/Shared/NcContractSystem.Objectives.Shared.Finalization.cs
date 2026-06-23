using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void FinalizeObjectiveCompletion((EntityUid Store, string ContractId) key, ContractServerData contract)
    {
        MarkObjectiveComplete(contract);
        RaiseContractsChanged(key);

        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        if (state.ProofEntity is { } proof && proof != EntityUid.Invalid && !TerminatingOrDeleted(proof))
        {
            RetargetObjectivePinpointers(key, state, proof);
            return;
        }

        if (RequiresSpawnedHuntBodyTurnIn(contract) && TryGetHuntBodyEntity(state, out var body))
        {
            RetargetObjectivePinpointers(key, state, body);
            return;
        }

        CleanupObjectivePinpointers(key, state);
    }

    private void FinalizeObjectiveTerminalOutcome(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract,
        string failureReason,
        ContractObjectiveOutcome outcome = ContractObjectiveOutcome.Failed,
        bool deleteTrackedEntities = true,
        bool deleteGuards = false
    )
    {
        MarkObjectiveFailed(contract, failureReason, outcome);

        if (_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            CleanupObjectivePinpointers(key, state);

        FailObjectiveContract(key, deleteTrackedEntities, deleteGuards);
    }

    private void FailObjectiveContract(
        (EntityUid Store, string ContractId) key,
        bool deleteTrackedEntities,
        bool deleteGuards
    )
    {
        CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities, deleteGuards);
        RaiseContractsChanged(key);
    }
}
