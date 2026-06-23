using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Trade;

public sealed partial class NcStoreLogicSystem
{
    private int SpawnPurchasedProduct(
        EntityUid user,
        string productEntity,
        EntityPrototype productProto,
        int purchases,
        int unitsPerPurchase
    )
    {
        return _spawnService.SpawnPurchasedProduct(user, productEntity, productProto, purchases, unitsPerPurchase);
    }
}
