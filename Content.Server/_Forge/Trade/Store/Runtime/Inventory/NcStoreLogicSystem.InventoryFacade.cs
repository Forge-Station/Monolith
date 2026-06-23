using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

public sealed partial class NcStoreLogicSystem
{
    public NcInventorySnapshot BuildInventorySnapshot(EntityUid root)
    {
        return _inventory.BuildInventorySnapshot(root);
    }

    public int GetInventoryRevision(EntityUid root)
    {
        return _inventory.GetInventoryRevision(root);
    }

    public void FillInventorySnapshot(EntityUid root, NcInventorySnapshot buffer)
    {
        _inventory.FillInventorySnapshot(root, buffer);
    }

    public void ScanInventory(EntityUid root, List<EntityUid> itemsBuffer, NcInventorySnapshot snapshotBuffer)
    {
        _inventory.ScanInventory(root, itemsBuffer, snapshotBuffer);
    }

    public void ScanInventoryItems(EntityUid root, List<EntityUid> itemsBuffer)
    {
        _inventory.ScanInventoryItems(root, itemsBuffer);
    }

    public int GetOwnedFromSnapshot(
        in NcInventorySnapshot snapshot,
        string productProtoId,
        PrototypeMatchMode matchMode
    )
    {
        return _inventory.GetOwnedFromSnapshot(snapshot, productProtoId, matchMode);
    }

    public bool TryTakeProductUnitsFromRootCached(
        EntityUid root,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    )
    {
        return _inventory.TryTakeProductUnitsFromRootCached(root, protoId, amount, matchMode);
    }

    public bool TryTakeProductUnitsFromCachedList(
        EntityUid root,
        List<EntityUid> cachedItems,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    )
    {
        return _inventory.TryTakeProductUnitsFromCachedList(root, cachedItems, protoId, amount, matchMode);
    }

    public bool IsProtectedFromDirectSale(EntityUid root, EntityUid item)
    {
        return _inventory.IsProtectedFromDirectSale(root, item);
    }
}
