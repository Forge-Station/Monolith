using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void RaiseContractsChanged(EntityUid store)
    {
        if (store == EntityUid.Invalid || TerminatingOrDeleted(store))
            return;

        var ev = new NcContractsChangedEvent();
        RaiseLocalEvent(store, ref ev);
    }

    private void RaiseContractsChanged((EntityUid Store, string ContractId) key)
    {
        RaiseContractsChanged(key.Store);
    }

    private void RaiseContractsChangedIfSnapshotChanged(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        int previousRequired,
        int previousProgress,
        ContractFlowStatus previousStatus
    )
    {
        if (contract.Required == previousRequired &&
            contract.Progress == previousProgress &&
            contract.FlowStatus == previousStatus)
            return;

        RaiseContractsChanged(key);
    }
}
