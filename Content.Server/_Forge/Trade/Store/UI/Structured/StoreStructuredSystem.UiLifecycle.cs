using Content.Server.Storage.Components;
using Content.Shared._Forge.Trade;
using Content.Shared.Access.Components;
using Content.Shared.Storage.Components;
using Content.Shared.UserInterface;

namespace Content.Server._Forge.Trade;

public sealed partial class StoreStructuredSystem
{
    private bool TryGetMessageUser(EntityUid store, NcStoreComponent comp, BoundUserInterfaceMessage msg, out EntityUid user)
    {
        user = msg.Actor;
        if (user == EntityUid.Invalid)
            return false;

        if (!comp.OpenUsers.Contains(user))
            return false;

        if (!_ui.IsUiOpen(store, NcStoreUiKey.Key, user))
            return false;

        return true;
    }

    private void OnSetVisibleListings(EntityUid uid, NcStoreComponent comp, StoreSetVisibleListingsBoundUiMessage msg)
    {
        if (!TryGetMessageUser(uid, comp, msg, out var user))
            return;

        _visibleListingIdsScratch.Clear();
        _visibleListingIdsSetScratch.Clear();

        var ids = msg.Ids;
        var max = Math.Min(ids.Length, MaxVisibleListingIds);

        for (var i = 0; i < max; i++)
        {
            var id = ids[i];
            if (!StoreTradeLimits.IsValidMessageId(id))
                continue;

            if (!comp.ListingIndex.ContainsKey(NcStoreComponent.MakeListingKey(StoreMode.Buy, id)))
                continue;

            if (!_visibleListingIdsSetScratch.Add(id))
                continue;

            _visibleListingIdsScratch.Add(id);
        }

        var scratch = GetDynamicScratch(uid, user);
        if (!scratch.UpdateVisibleIds(_visibleListingIdsScratch.Count > 0 ? _visibleListingIdsScratch : null))
            return;

        RequestDynamicRefresh(uid, comp, user);
    }

    private void OnStorageOpen(EntityUid uid, EntityStorageComponent comp, ref StorageAfterOpenEvent args)
    {
        if (_storesByWatchedRoot.ContainsKey(uid))
            RefreshStoresAffectedBy(uid);
    }

    private void OnContractsChanged(EntityUid uid, NcStoreComponent comp, ref NcContractsChangedEvent args)
    {
        MarkDirty(uid);
    }

    private void OnStorageClose(EntityUid uid, EntityStorageComponent comp, ref StorageAfterCloseEvent args)
    {
        if (_storesByWatchedRoot.ContainsKey(uid))
            RefreshStoresAffectedBy(uid);
    }

    private void OnStoreShutdown(EntityUid uid, NcStoreComponent comp, ComponentShutdown args)
    {
        _catalogCache.Clear(uid);
        CloseAndCleanUp(uid);
        _contracts.ClearStoreRuntimeCaches(uid);
        _logic.ClearStoreRuntimeCaches(uid);
    }

    public void RefreshCatalog(EntityUid uid, NcStoreComponent comp)
    {
        _catalogCache.Clear(uid);

        comp.BumpCatalogRevision();

        _openStoreUsersScratch.Clear();
        foreach (var user in comp.OpenUsers)
            _openStoreUsersScratch.Add(new StoreUserKey(uid, user));

        foreach (var key in _openStoreUsersScratch)
        {
            if (!_ui.IsUiOpen(uid, NcStoreUiKey.Key, key.User))
                continue;

            _dynamicScratchByStoreUser.Remove(key);
            SendCatalog(uid, comp, key.User);
            RequestDynamicRefresh(uid, comp, key.User, true);
        }
    }

    private void SendInitialSnapshot(EntityUid uid, NcStoreComponent comp, EntityUid user, string reason)
    {
        _loader.EnsureLoaded(uid, comp, reason);
        SendCatalog(uid, comp, user);
        RequestDynamicRefresh(uid, comp, user, true);
    }

    public void RequestDynamicRefresh(EntityUid uid, NcStoreComponent comp, EntityUid user, bool forceSend = false)
    {
        var key = new StoreUserKey(uid, user);
        MarkDirty(key);

        var now = _timing.CurTime;
        var scratch = GetDynamicScratch(uid, user);
        if (!forceSend && now < scratch.NextDynamicAllowed)
            return;

        _dirtyStoreUsers.Remove(key);
        UpdateDynamicState(uid, comp, user, forceSend);
        SetNextDynamicUpdateTime(scratch, now);
    }

    public void RequestDynamicRefreshForAll(EntityUid uid, NcStoreComponent comp, EntityUid? immediateUser = null)
    {
        if (immediateUser is { } user && comp.OpenUsers.Contains(user))
            RequestDynamicRefresh(uid, comp, user);

        foreach (var openUser in comp.OpenUsers)
        {
            if (immediateUser != null && openUser == immediateUser.Value)
                continue;

            MarkDirty(new StoreUserKey(uid, openUser));
        }
    }

    private void OnUiOpenAttempt(EntityUid uid, NcStoreComponent comp, ref ActivatableUIOpenAttemptEvent ev)
    {
        ev.Cancel();
        var user = ev.User;

        if (!_ui.HasUi(uid, NcStoreUiKey.Key))
            return;
        if (!_storeSystem.CanUseStore(uid, comp, user))
            return;
        if (TryComp(uid, out TransformComponent? sX) && TryComp(user, out TransformComponent? uX) &&
            !_xform.InRange(sX.Coordinates, uX.Coordinates, AutoCloseDistance))
            return;

        comp.OpenUsers.Add(user);
        _openStoreUsers.Add(new StoreUserKey(uid, user));

        if (!_ui.IsUiOpen(uid, NcStoreUiKey.Key, user))
            _ui.OpenUi(uid, NcStoreUiKey.Key, user);

        EnsureCrateWatchUpToDate(uid, user);

        SendInitialSnapshot(uid, comp, user, "UiOpenAttempt");
    }

    private void OnUiClosed(EntityUid uid, NcStoreComponent comp, BoundUIClosedEvent ev)
    {
        if (!ev.UiKey.Equals(NcStoreUiKey.Key))
            return;
        if (ev.Actor == EntityUid.Invalid)
            return;

        CloseAndCleanUp(uid, comp, ev.Actor);
    }

    private void OnUiRefreshRequest(EntityUid uid, NcStoreComponent comp, RequestUiRefreshMessage msg)
    {
        if (!TryGetMessageUser(uid, comp, msg, out var user))
        {
            if (msg.Actor != EntityUid.Invalid)
                CloseAndCleanUp(uid, comp, msg.Actor, true);
            return;
        }

        if (!_storeSystem.CanUseStore(uid, comp, user))
        {
            CloseAndCleanUp(uid, comp, user, true);
            return;
        }

        if (TryComp(uid, out TransformComponent? sX) && TryComp(user, out TransformComponent? uX) &&
            !_xform.InRange(sX.Coordinates, uX.Coordinates, AutoCloseDistance))
        {
            CloseAndCleanUp(uid, comp, user, true);
            return;
        }

        EnsureCrateWatchUpToDate(uid, user);

        var scratch = GetDynamicScratch(uid, user);
        var now = _timing.CurTime;
        if (now < scratch.NextManualRefreshAllowed)
        {
            MarkDirty(new StoreUserKey(uid, user));
            return;
        }

        scratch.NextManualRefreshAllowed = now + TimeSpan.FromSeconds(MinManualRefreshInterval);
        SendInitialSnapshot(uid, comp, user, "UiRefreshRequest");
    }

    private void OnAccessReaderChanged(
        EntityUid uid,
        AccessReaderComponent comp,
        ref AccessReaderConfigurationChangedEvent args
    )
    {
        if (!TryComp<NcStoreComponent>(uid, out var store))
            return;

        _openStoreUsersScratch.Clear();
        foreach (var user in store.OpenUsers)
            _openStoreUsersScratch.Add(new StoreUserKey(uid, user));

        foreach (var key in _openStoreUsersScratch)
        {
            if (_storeSystem.CanUseStore(uid, store, key.User))
                continue;

            CloseAndCleanUp(uid, store, key.User, true);
        }
    }
}
