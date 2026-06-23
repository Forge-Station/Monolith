using Content.Shared._Forge.Trade;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared._NF.Bank.Events;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server._Forge.Trade;

public sealed partial class StoreStructuredSystem : EntitySystem
{
    private void PushDynamicState(
        EntityUid store,
        NcStoreComponent comp,
        EntityUid user,
        DynamicTabState tabs,
        DynamicScratch scratch,
        DynamicStateBuffer buf,
        bool forceSend = false
    )
    {
        _dynamicStatePublisher.PublishIfChanged(_ui, store, comp, user, tabs, scratch, buf, forceSend);
    }

    private bool TryFindWatchedRoot(EntityUid start, out EntityUid watchedRoot)
    {
        watchedRoot = default;
        if (_storesByWatchedRoot.Count == 0)
            return false;
        var cur = start;
        for (var i = 0; i < WatchedRootSearchLimit; i++)
        {
            if (_storesByWatchedRoot.TryGetValue(cur, out _))
            {
                watchedRoot = cur;
                return true;
            }

            if (!TryComp(cur, out TransformComponent? xform))
                return false;
            var parent = xform.ParentUid;
            if (parent == EntityUid.Invalid || parent == cur)
                return false;
            cur = parent;
        }

        return false;
    }

    private void RefreshStoresAffectedBy(EntityUid changedRoot)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (_pendingRefreshEntities.Add(changedRoot))
            _inventory.InvalidateInventoryCache(changedRoot);

        if (_timing.CurTime < _nextOpenStoreValidityCheck && _timing.CurTime >= _nextAccelAllowed)
        {
            _nextOpenStoreValidityCheck = _timing.CurTime;
            _nextAccelAllowed = _timing.CurTime + TimeSpan.FromSeconds(MinAccelInterval);
        }

        if (_pendingRefreshEntities.Count > 4096)
        {
            foreach (var key in _openStoreUsers)
            {
                if (_watchByStoreUser.TryGetValue(key, out var crate))
                {
                    _inventory.InvalidateInventoryCache(key.User);
                    if (crate is { } crateUid)
                        _inventory.InvalidateInventoryCache(crateUid);
                }

                MarkDirty(key);
            }

            _pendingRefreshEntities.Clear();
        }
    }

    private void OnUserEntInserted(EntityUid uid, ContainerManagerComponent comp, EntInsertedIntoContainerMessage args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnUserEntRemoved(EntityUid uid, ContainerManagerComponent comp, EntRemovedFromContainerMessage args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnStackCountChanged(EntityUid uid, StackComponent comp, ref StackCountChangedEvent args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnRefillableSolutionChanged(
        EntityUid uid,
        RefillableSolutionComponent comp,
        ref SolutionContainerChangedEvent args
    )
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnBankBalanceChanged(BalanceChangedEvent args)
    {
        if (args.Session.AttachedEntity is not { } user)
            return;

        foreach (var key in _openStoreUsers)
        {
            if (key.User == user)
                MarkDirty(key);
        }
    }

    private void OnWatchedEntityParentChanged(ref EntParentChangedMessage args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        EntityUid? refreshedRoot = null;

        if (TryFindWatchedRoot(args.Entity, out var currentRoot))
        {
            RefreshStoresAffectedBy(currentRoot);
            refreshedRoot = currentRoot;
        }

        if (args.OldParent is not { } oldParent || oldParent == EntityUid.Invalid)
            return;

        if (!TryFindWatchedRoot(oldParent, out var previousRoot))
            return;

        if (refreshedRoot == previousRoot)
            return;

        RefreshStoresAffectedBy(previousRoot);
    }


    private void ProcessPendingRefreshes()
    {
        if (_pendingRefreshEntities.Count == 0)
            return;

        if (_storesByWatchedRoot.Count == 0)
        {
            // No active watchers: drop stale pending roots to avoid carrying "air cache"
            // between unrelated store sessions.
            _pendingRefreshEntities.Clear();
            return;
        }

        _affectedStoreUsersScratch.Clear();
        foreach (var root in _pendingRefreshEntities)
        {
            if (!Exists(root))
                continue;
            if (_storesByWatchedRoot.TryGetValue(root, out var storeUsers))
            {
                foreach (var key in storeUsers)
                {
                    _affectedStoreUsersScratch.Add(key);
                }
            }
        }

        _pendingRefreshEntities.Clear();
        foreach (var key in _affectedStoreUsersScratch)
        {
            MarkDirty(key);
        }
    }

    private sealed class StoreDynamicStatePublisher
    {
        public void PublishIfChanged(
            UserInterfaceSystem ui,
            EntityUid store,
            NcStoreComponent comp,
            EntityUid user,
            DynamicTabState tabs,
            DynamicScratch scratch,
            DynamicStateBuffer buf,
            bool forceSend
        )
        {
            if (!forceSend &&
                scratch.EqualsLast(
                    buf,
                    comp.CatalogRevision,
                    tabs.HasBuyTab,
                    tabs.HasSellTab,
                    tabs.HasBarterTab,
                    tabs.HasContractsTab))
                return;

            comp.UiRevision = unchecked(comp.UiRevision + 1);

            ui.ServerSendUiMessage(
                (store, null),
                NcStoreUiKey.Key,
                new StoreDynamicState(
                    comp.UiRevision,
                    comp.CatalogRevision,
                    new Dictionary<string, int>(buf.BalancesByCurrency),
                    new Dictionary<string, int>(buf.RemainingById),
                    new Dictionary<string, int>(buf.OwnedById),
                    new Dictionary<string, int>(buf.CrateUnitsById),
                    new Dictionary<string, int>(buf.CrateTotals),
                    new List<ContractClientData>(buf.Contracts),
                    tabs.HasBuyTab,
                    tabs.HasSellTab,
                    tabs.HasBarterTab,
                    tabs.HasContractsTab,
                    buf.ContractSkipCost,
                    buf.ContractSkipCurrency,
                    scratch.HasVisibleIds,
                    new List<string>(buf.ListingScopeIds)
                ),
                user
            );

            scratch.Commit(
                comp.CatalogRevision,
                tabs.HasBuyTab,
                tabs.HasSellTab,
                tabs.HasBarterTab,
                tabs.HasContractsTab);
        }
    }
}
