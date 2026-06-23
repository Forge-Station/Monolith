using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

public sealed partial class StoreStructuredSystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ProcessPendingRefreshes();
        ProcessDirtyStoreUpdates();

        var now = _timing.CurTime;

        if (now >= _nextRealtimeOpenStoreUpdate)
        {
            _nextRealtimeOpenStoreUpdate = now + RealtimeOpenStoreUpdateInterval;
            ProcessRealtimeOpenStoreUpdates();
        }

        if (now >= _nextOpenStoreValidityCheck)
        {
            _nextOpenStoreValidityCheck = now + OpenStoreValidityCheckInterval;
            ProcessOpenStoreValidityChecks();
        }
    }

    private void ProcessRealtimeOpenStoreUpdates()
    {
        if (_openStoreUsers.Count == 0)
        {
            _realtimeOpenStoreCursor = 0;
            return;
        }

        var now = _timing.CurTime;
        _openStoreUsersScratch.Clear();
        _openStoreUsersScratch.AddRange(_openStoreUsers);

        if (_realtimeOpenStoreCursor >= _openStoreUsersScratch.Count)
            _realtimeOpenStoreCursor = 0;

        var processed = 0;
        var inspected = 0;
        var count = _openStoreUsersScratch.Count;

        while (inspected < count && processed < MaxRealtimeDynamicUpdatesPerTick)
        {
            var index = (_realtimeOpenStoreCursor + inspected) % count;
            if (ProcessRealtimeOpenStoreUpdate(_openStoreUsersScratch[index], now))
                processed++;

            inspected++;
        }

        _realtimeOpenStoreCursor = (_realtimeOpenStoreCursor + Math.Max(1, inspected)) % count;
    }

    private bool ProcessRealtimeOpenStoreUpdate(StoreUserKey key, TimeSpan now)
    {
        if (!TryGetOpenStoreUser(key, out var store))
            return false;

        if (EnsureCrateWatchUpToDate(key.Store, key.User))
            MarkDirty(key);

        if (!_contracts.HasRealtimeContractState(store) || !TryGetDynamicScratchForUpdate(key, now, out var scratch))
            return false;

        _dirtyStoreUsers.Remove(key);
        UpdateDynamicState(key.Store, store, key.User);
        SetNextDynamicUpdateTime(scratch, now);
        return true;
    }

    private void ProcessDirtyStoreUpdates()
    {
        if (_dirtyStoreUsers.Count == 0)
            return;

        var now = _timing.CurTime;
        var processed = 0;

        _dirtyStoreUsersScratch.Clear();
        _dirtyStoreUsersScratch.AddRange(_dirtyStoreUsers);

        foreach (var key in _dirtyStoreUsersScratch)
        {
            if (processed >= MaxDynamicUpdatesPerTick)
                break;

            if (!TryGetOpenStoreUser(key, out var store))
            {
                _dirtyStoreUsers.Remove(key);
                continue;
            }

            if (!TryGetDynamicScratchForUpdate(key, now, out var scratch))
                continue;

            UpdateDynamicState(key.Store, store, key.User);
            SetNextDynamicUpdateTime(scratch, now);
            _dirtyStoreUsers.Remove(key);
            processed++;
        }
    }

    private void ProcessOpenStoreValidityChecks()
    {
        if (_openStoreUsers.Count == 0)
            return;

        _openStoreUsersScratch.Clear();
        _openStoreUsersScratch.AddRange(_openStoreUsers);

        foreach (var key in _openStoreUsersScratch)
        {
            ValidateOpenStore(key);
        }
    }

    private bool TryGetOpenStoreUser(StoreUserKey key, out NcStoreComponent store)
    {
        store = default!;

        if (!TryComp(key.Store, out NcStoreComponent? foundStore) ||
            key.User == EntityUid.Invalid ||
            !foundStore.OpenUsers.Contains(key.User))
            return false;

        if (!_ui.IsUiOpen(key.Store, NcStoreUiKey.Key, key.User))
            return false;

        store = foundStore;
        return true;
    }

    private bool TryGetDynamicScratchForUpdate(StoreUserKey key, TimeSpan now, out DynamicScratch scratch)
    {
        scratch = GetDynamicScratch(key.Store, key.User);
        return now >= scratch.NextDynamicAllowed;
    }

    private void SetNextDynamicUpdateTime(DynamicScratch scratch, TimeSpan now)
    {
        scratch.NextDynamicAllowed = now + TimeSpan.FromSeconds(MinDynamicInterval);
    }

    private void ValidateOpenStore(StoreUserKey key)
    {
        if (!TryComp(key.Store, out NcStoreComponent? store) || !TryComp(key.Store, out TransformComponent? xform))
        {
            CloseAndCleanUp(key.Store);
            return;
        }

        if (!store.OpenUsers.Contains(key.User))
        {
            CloseAndCleanUp(key.Store, store, key.User);
            return;
        }

        if (!_ui.IsUiOpen(key.Store, NcStoreUiKey.Key, key.User))
        {
            CloseAndCleanUp(key.Store, store, key.User);
            return;
        }

        if (!IsStoreUserInRange(xform, key.User))
        {
            CloseStoreForDetachedUser(key.Store, store, key.User);
            return;
        }

        if (_storeSystem.CanUseStore(key.Store, store, key.User))
            return;

        CloseStoreForNoAccess(key.Store, store, key.User);
    }

    private bool IsStoreUserInRange(TransformComponent storeXform, EntityUid userUid)
    {
        return TryComp(userUid, out TransformComponent? userXform) &&
               _xform.InRange(storeXform.Coordinates, userXform.Coordinates, AutoCloseDistance);
    }

    private void CloseStoreForDetachedUser(EntityUid uid, NcStoreComponent store, EntityUid userUid)
    {
        CloseAndCleanUp(uid, store, userUid, true);
    }

    private void CloseStoreForNoAccess(EntityUid uid, NcStoreComponent store, EntityUid userUid)
    {
        CloseAndCleanUp(uid, store, userUid, true);
        _popups.PopupEntity(Loc.GetString("nc-store-no-access"), uid, userUid);
    }

    private void CloseAndCleanUp(EntityUid storeUid)
    {
        _openStoreUsersScratch.Clear();
        foreach (var key in _openStoreUsers)
        {
            if (key.Store == storeUid)
                _openStoreUsersScratch.Add(key);
        }

        if (TryComp(storeUid, out NcStoreComponent? store))
        {
            foreach (var key in _openStoreUsersScratch)
            {
                CloseAndCleanUp(storeUid, store, key.User, true);
            }
            return;
        }

        foreach (var key in _openStoreUsersScratch)
        {
            CloseAndCleanUpMissingStore(key, true);
        }
    }

    private void CloseAndCleanUp(EntityUid storeUid, NcStoreComponent store, EntityUid user, bool closeUi = false)
    {
        var key = new StoreUserKey(storeUid, user);

        if (_watchByStoreUser.TryGetValue(key, out var crate))
        {
            _inventory.InvalidateInventoryCache(user);
            if (crate is { } crateUid)
                _inventory.InvalidateInventoryCache(crateUid);
        }

        if (_dynamicScratchByStoreUser.TryGetValue(key, out var scratch))
            scratch.UpdateVisibleIds(null);

        store.OpenUsers.Remove(user);
        _openStoreUsers.Remove(key);
        _dirtyStoreUsers.Remove(key);
        _storesUpdatingDynamic.Remove(key);
        _dynamicScratchByStoreUser.Remove(key);
        UnregisterStoreWatch(key);

        if (closeUi && _ui.IsUiOpen(storeUid, NcStoreUiKey.Key, user))
            _ui.CloseUi(storeUid, NcStoreUiKey.Key, user);
    }

    private void CloseAndCleanUpMissingStore(StoreUserKey key, bool closeUi)
    {
        if (_watchByStoreUser.TryGetValue(key, out var crate))
        {
            _inventory.InvalidateInventoryCache(key.User);
            if (crate is { } crateUid)
                _inventory.InvalidateInventoryCache(crateUid);
        }

        if (_dynamicScratchByStoreUser.TryGetValue(key, out var scratch))
            scratch.UpdateVisibleIds(null);

        _openStoreUsers.Remove(key);
        _dirtyStoreUsers.Remove(key);
        _storesUpdatingDynamic.Remove(key);
        _dynamicScratchByStoreUser.Remove(key);
        UnregisterStoreWatch(key);

        if (closeUi && _ui.IsUiOpen(key.Store, NcStoreUiKey.Key, key.User))
            _ui.CloseUi(key.Store, NcStoreUiKey.Key, key.User);
    }

    private void MarkDirty(EntityUid storeUid)
    {
        if (storeUid == EntityUid.Invalid || !TryComp(storeUid, out NcStoreComponent? store))
            return;

        foreach (var user in store.OpenUsers)
        {
            MarkDirty(new StoreUserKey(storeUid, user));
        }
    }

    private void MarkDirty(StoreUserKey key)
    {
        if (key.Store != EntityUid.Invalid && key.User != EntityUid.Invalid)
            _dirtyStoreUsers.Add(key);
    }
}
