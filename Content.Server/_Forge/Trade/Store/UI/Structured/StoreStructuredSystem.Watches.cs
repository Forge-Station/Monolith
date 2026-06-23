namespace Content.Server._Forge.Trade;

public sealed partial class StoreStructuredSystem
{
    private bool EnsureCrateWatchUpToDate(EntityUid storeUid, EntityUid user)
    {
        EntityUid? crateUid = null;
        if (_logic.TryGetPulledClosedCrate(user, out var pulledCrate))
            crateUid = pulledCrate;

        var key = new StoreUserKey(storeUid, user);
        if (_watchByStoreUser.TryGetValue(key, out var prevCrate))
        {
            if (prevCrate == crateUid)
                return false;

            if (prevCrate is { } oldCrate)
                _inventory.InvalidateInventoryCache(oldCrate);
            if (crateUid is { } newCrate)
                _inventory.InvalidateInventoryCache(newCrate);
        }
        else
        {
            _inventory.InvalidateInventoryCache(user);
            if (crateUid is { } newCrate)
                _inventory.InvalidateInventoryCache(newCrate);
        }

        UpdateStoreWatch(storeUid, user, crateUid);
        return true;
    }

    private void AddWatchedRoot(EntityUid root, StoreUserKey key)
    {
        if (!_storesByWatchedRoot.TryGetValue(root, out var set))
        {
            set = new HashSet<StoreUserKey>();
            _storesByWatchedRoot[root] = set;
        }

        set.Add(key);
    }

    private void RemoveWatchedRoot(EntityUid root, StoreUserKey key)
    {
        if (!_storesByWatchedRoot.TryGetValue(root, out var set))
            return;
        set.Remove(key);
        if (set.Count == 0)
            _storesByWatchedRoot.Remove(root);
    }

    private void UpdateStoreWatch(EntityUid storeUid, EntityUid user, EntityUid? crate)
    {
        if (user == EntityUid.Invalid)
        {
            return;
        }

        var key = new StoreUserKey(storeUid, user);
        if (_watchByStoreUser.TryGetValue(key, out var prevCrate))
        {
            if (prevCrate == crate)
                return;
            if (prevCrate is { } oldCrate)
                RemoveWatchedRoot(oldCrate, key);
        }
        else
            AddWatchedRoot(user, key);

        _watchByStoreUser[key] = crate;
        _inventory.InvalidateInventoryCache(user);
        if (crate is { } c)
        {
            AddWatchedRoot(c, key);
            _inventory.InvalidateInventoryCache(c);
        }
    }

    private void UnregisterStoreWatch(StoreUserKey key)
    {
        if (!_watchByStoreUser.TryGetValue(key, out var crate))
            return;
        if (key.User != EntityUid.Invalid)
            RemoveWatchedRoot(key.User, key);
        if (crate is { } crateUid)
            RemoveWatchedRoot(crateUid, key);
        _watchByStoreUser.Remove(key);
    }
}
