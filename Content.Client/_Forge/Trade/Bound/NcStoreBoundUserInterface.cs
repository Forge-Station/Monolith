using Content.Shared._Forge.Trade;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Trade;

public sealed class NcStoreStructuredBoundUi(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly TimeSpan CatalogRefreshRetryInterval = TimeSpan.FromSeconds(0.5);
    private readonly IPlayerManager _player = IoCManager.Resolve<IPlayerManager>();

    private int _lastCatalogRevision = int.MinValue;
    private int _lastStateRevision = int.MinValue;

    private NcStoreMenu? _menu;

    private StoreDynamicState? _pendingDynamic;
    private DateTime? _lastCatalogRefreshRequest;

    private EntityUid? Actor => _player.LocalSession?.AttachedEntity;

    protected override void Open()
    {
        var wasOpened = IsOpened;
        base.Open();

        if (wasOpened)
            return;

        EnsureMenuCreated();
        if (_menu == null)
            return;

        _menu.Visible = false;
        RequestUiRefresh();
    }

    private void DetachMenuHandlers(NcStoreMenu menu)
    {
        menu.OnBarterPressed -= OnBarter;
        menu.OnBuyPressed -= OnBuy;
        menu.OnSellPressed -= OnSell;
        menu.OnMassSellPulledCrate -= OnMassSellPulledCrate;
        menu.OnContractClaim -= OnContractClaim;
        menu.OnContractTake -= OnContractTake;
        menu.OnContractSkip -= OnContractSkip;
        menu.OnContractRequestPinpointer -= OnContractRequestPinpointer;
        menu.OnVisibleListingIdsChanged -= OnVisibleListingIdsChanged;
        menu.OnClose -= OnMenuClosed;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        switch (message)
        {
            case StoreCatalogMessage cat:
                ReceiveCatalog(cat);
                break;
            case StoreDynamicState st:
                ReceiveDynamic(st);
                break;
        }
    }

    private void ReceiveDynamic(StoreDynamicState st)
    {
        EnsureMenuCreated();
        if (_menu == null)
            return;

        if (st.CatalogRevision != _lastCatalogRevision)
        {
            _pendingDynamic = st;
            _menu.Visible = false;
            RequestUiRefreshIfDue();

            return;
        }

        if (st.Revision <= _lastStateRevision)
            return;

        _lastStateRevision = st.Revision;

        _lastCatalogRefreshRequest = null;

        ApplyDynamic(st);
        _menu.Visible = true;
    }

    private void ReceiveCatalog(StoreCatalogMessage cat)
    {
        EnsureMenuCreated();
        if (_menu == null)
            return;

        if (cat.CatalogRevision == _lastCatalogRevision)
            return;

        _lastCatalogRevision = cat.CatalogRevision;
        _lastStateRevision = int.MinValue;

        _menu.PopulateCatalog(
            cat.Listings,
            cat.HasBuyTab,
            cat.HasSellTab,
            cat.HasBarterTab,
            cat.HasContractsTab,
            cat.UiColors);

        if (_pendingDynamic is { } pending &&
            pending.CatalogRevision == _lastCatalogRevision)
        {
            _pendingDynamic = null;
            _lastCatalogRefreshRequest = null;
            _lastStateRevision = pending.Revision;
            ApplyDynamic(pending);
            _menu.Visible = true;
        }
        else
            _menu.Visible = false;
    }

    private void ApplyDynamic(StoreDynamicState st) =>
        _menu!.ApplyDynamicState(
            st.BalanceByCurrency,
            st.RemainingById,
            st.OwnedById,
            st.CrateUnitsById,
            st.MassSellTotals,
            st.HasBuyTab,
            st.HasSellTab,
            st.HasBarterTab,
            st.HasContractsTab,
            st.Contracts,
            st.ContractSkipCost,
            st.ContractSkipCurrency,
            st.IsSparseDynamicSnapshot,
            st.SnapshotScopeIds);

    private void EnsureMenuCreated()
    {
        if (_menu != null)
            return;

        _menu = this.CreateWindow<NcStoreMenu>();
        _menu.Visible = false;

        _lastCatalogRevision = int.MinValue;
        _lastStateRevision = int.MinValue;
        _pendingDynamic = null;
        _lastCatalogRefreshRequest = null;

        if (EntMan.TryGetComponent(Owner, out MetaDataComponent? meta))
            _menu.SetDisplayTitle(meta.EntityName);
        else
            _menu.SetDisplayTitle(string.Empty);

        _menu.OnBarterPressed += OnBarter;
        _menu.OnBuyPressed += OnBuy;
        _menu.OnSellPressed += OnSell;
        _menu.OnMassSellPulledCrate += OnMassSellPulledCrate;
        _menu.OnContractClaim += OnContractClaim;
        _menu.OnContractTake += OnContractTake;
        _menu.OnContractSkip += OnContractSkip;
        _menu.OnContractRequestPinpointer += OnContractRequestPinpointer;
        _menu.OnVisibleListingIdsChanged += OnVisibleListingIdsChanged;

        _menu.OnClose += OnMenuClosed;
    }

    private void OnMenuClosed()
    {
        if (_menu == null)
            return;

        DetachMenuHandlers(_menu);

        _menu.CleanupBeforeClose();
        _menu.Orphan();
        _menu = null;
        _lastCatalogRevision = int.MinValue;
        _lastStateRevision = int.MinValue;
        _pendingDynamic = null;
        _lastCatalogRefreshRequest = null;
    }

    private void OnBuy(StoreListingData data, int qty)
    {
        if (Actor == null)
            return;

        SendMessage(new StoreBuyListingBoundUiMessage(data.ListingId, qty));
    }

    private void OnSell(StoreListingData data, int qty)
    {
        if (Actor == null)
            return;

        SendMessage(new StoreSellListingBoundUiMessage(data.ListingId, qty, data.Flavor == StoreListingFlavor.Crate));
    }

    private void OnBarter(StoreListingData data, int qty)
    {
        if (Actor == null)
            return;

        SendMessage(new StoreBarterListingBoundUiMessage(data.ListingId, qty));
    }

    private void OnContractClaim(string contractId)
    {
        if (Actor == null)
            return;

        SendMessage(new ClaimContractBoundMessage(contractId));
    }

    private void OnContractTake(string contractId)
    {
        if (Actor == null)
            return;

        SendMessage(new TakeContractBoundMessage(contractId));
    }

    private void OnContractSkip(string contractId)
    {
        if (Actor == null)
            return;

        SendMessage(new SkipContractBoundMessage(contractId));
    }

    private void OnContractRequestPinpointer(string contractId)
    {
        if (Actor == null)
            return;

        SendMessage(new RequestContractPinpointerBoundMessage(contractId));
    }

    private void OnMassSellPulledCrate()
    {
        if (Actor == null)
            return;

        SendMessage(new StoreMassSellPulledCrateBoundUiMessage());
    }

    private void OnVisibleListingIdsChanged(string[] ids)
    {
        if (Actor == null)
            return;

        SendMessage(new StoreSetVisibleListingsBoundUiMessage(ids));
    }

    protected override void Dispose(bool disposing)
    {
        if (_menu != null)
        {
            DetachMenuHandlers(_menu);
            _menu.CleanupBeforeClose();
            _menu.Orphan();
            _menu = null;
        }

        _lastCatalogRevision = int.MinValue;
        _lastStateRevision = int.MinValue;
        _pendingDynamic = null;
        _lastCatalogRefreshRequest = null;

        base.Dispose(disposing);
    }

    private void RequestUiRefreshIfDue()
    {
        var now = DateTime.UtcNow;
        if (_lastCatalogRefreshRequest is { } last &&
            now - last < CatalogRefreshRetryInterval)
            return;

        RequestUiRefresh(now);
    }

    private void RequestUiRefresh(DateTime? now = null)
    {
        if (Actor == null)
            return;

        _lastCatalogRefreshRequest = now ?? DateTime.UtcNow;
        SendMessage(new RequestUiRefreshMessage());
    }
}
