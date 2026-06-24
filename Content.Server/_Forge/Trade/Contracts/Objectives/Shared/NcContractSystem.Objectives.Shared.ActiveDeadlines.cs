using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void StartActiveContractDeadline(ContractServerData contract)
    {
        if (contract.ActiveTimeLimitSeconds <= 0)
        {
            contract.ActiveExpiresAt = null;
            contract.Runtime.ActiveTimeRemainingSeconds = 0;
            return;
        }

        contract.ActiveExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(contract.ActiveTimeLimitSeconds);
        contract.Runtime.ActiveTimeRemainingSeconds = contract.ActiveTimeLimitSeconds;
    }

    private void UpdateActiveContractDeadlines()
    {
        if (_objectiveRuntime.ByContract.Count == 0)
            return;

        _objectiveRuntime.KeysScratch.Clear();
        foreach (var key in _objectiveRuntime.ByContract.Keys)
        {
            _objectiveRuntime.KeysScratch.Add(key);
        }

        for (var i = 0; i < _objectiveRuntime.KeysScratch.Count; i++)
        {
            var key = _objectiveRuntime.KeysScratch[i];

            if (!TryGetObjectiveContract(key, out var comp, out var contract))
            {
                CleanupObjectiveRuntime(key.Store, key.ContractId, true);
                continue;
            }

            RefreshActiveContractDeadline(key, comp, contract);
        }

        _objectiveRuntime.KeysScratch.Clear();
    }

    private void RefreshActiveContractDeadline(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (!contract.Taken ||
            contract.Completed ||
            contract.Runtime.Failed ||
            contract.ActiveExpiresAt is not { } expiresAt)
        {
            contract.Runtime.ActiveTimeRemainingSeconds = 0;
            return;
        }

        var remaining = expiresAt - _timing.CurTime;
        if (remaining > TimeSpan.Zero)
        {
            contract.Runtime.ActiveTimeRemainingSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            return;
        }

        contract.Runtime.ActiveTimeRemainingSeconds = 0;
        FinalizeActiveDeadlineExpired(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-active-timeout"));
    }

    private void FinalizeActiveDeadlineExpired(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract,
        string failureReason
    )
    {
        MarkObjectiveFailed(contract, failureReason);
        CleanupObjectiveRuntime(key.Store, key.ContractId, true);
        comp.Contracts.Remove(key.ContractId);
        RefillContractsForStore(key.Store, comp, key.ContractId);
        RaiseContractsChanged(key.Store);
    }
}
