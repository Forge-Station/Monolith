using Content.Server.Cargo.Systems;
using Content.Shared._Mono.VendingMachine;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._Mono.VendingMachine;

/// <summary>
/// System that handles vending machine purchase tracking and pricing modifications.
/// </summary>
public sealed partial class VendingMachinePurchaseSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _mapManager = default!;

    // Forge-Change: which bought items still point at a grid. ComponentShutdown on MapGrid is already taken by the engine.
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _purchasesByGrid = new();
    private readonly Dictionary<EntityUid, EntityUid> _gridByPurchase = new();

    public override void Initialize()
    {
        base.Initialize();

        // Note: We don't subscribe to PriceCalculationEvent to avoid interfering with other pricing
        // Instead, we provide specific methods for cargo systems to call when needed

        // Forge-Change: a deleted grid uid stays on the item and PVS logs GetNetEntity every time the item is seen.
        EntityManager.EntityDeleted += OnEntityDeleted;
        SubscribeLocalEvent<VendingMachinePurchaseComponent, ComponentStartup>(OnPurchaseStartup);
    }

    public override void Shutdown()
    {
        EntityManager.EntityDeleted -= OnEntityDeleted;
        base.Shutdown();
    }

    private void OnPurchaseStartup(Entity<VendingMachinePurchaseComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.PurchaseGrid == EntityUid.Invalid)
            return;

        if (!Exists(ent.Comp.PurchaseGrid))
        {
            ent.Comp.PurchaseGrid = EntityUid.Invalid;
            Dirty(ent);
            return;
        }

        Track(ent.Owner, ent.Comp.PurchaseGrid);
    }

    private void OnEntityDeleted(Entity<MetaDataComponent> entity)
    {
        var uid = entity.Owner;
        if (_purchasesByGrid.Remove(uid, out var items))
        {
            foreach (var item in items)
            {
                _gridByPurchase.Remove(item);
                if (!TryComp<VendingMachinePurchaseComponent>(item, out var purchase) || TerminatingOrDeleted(item))
                    continue;

                purchase.PurchaseGrid = EntityUid.Invalid;
                Dirty(item, purchase);
            }
        }

        Untrack(uid);
    }

    private void Track(EntityUid item, EntityUid grid)
    {
        if (_gridByPurchase.TryGetValue(item, out var previous) && previous != grid)
            Untrack(item);

        _gridByPurchase[item] = grid;
        if (!_purchasesByGrid.TryGetValue(grid, out var items))
            _purchasesByGrid[grid] = items = new HashSet<EntityUid>();

        items.Add(item);
    }

    private void Untrack(EntityUid item)
    {
        if (!_gridByPurchase.Remove(item, out var grid))
            return;

        if (!_purchasesByGrid.TryGetValue(grid, out var items))
            return;

        items.Remove(item);
        if (items.Count == 0)
            _purchasesByGrid.Remove(grid);
    }

    /// <summary>
    /// Adds a VendingMachinePurchaseComponent to an entity when it's purchased from a vending machine.
    /// </summary>
    /// <param name="purchasedEntity">The entity that was purchased</param>
    /// <param name="vendingMachine">The vending machine it was purchased from</param>
    /// <param name="purchasePrice">The price paid for the entity</param>
    public void MarkAsPurchased(EntityUid purchasedEntity, EntityUid vendingMachine, double purchasePrice)
    {
        // Get the grid the vending machine is on
        var vendingTransform = Transform(vendingMachine);
        if (vendingTransform.GridUid == null)
            return;

        // Add the component to track this purchase
        var purchaseComponent = AddComp<VendingMachinePurchaseComponent>(purchasedEntity);
        purchaseComponent.PurchaseGrid = vendingTransform.GridUid.Value;
        purchaseComponent.OriginalPurchasePrice = purchasePrice;
        Track(purchasedEntity, purchaseComponent.PurchaseGrid);

        Dirty(purchasedEntity, purchaseComponent);
    }



    /// <summary>
    /// Gets the discounted price for a vending machine purchase if applicable.
    /// This method is called specifically by cargo systems to get the modified price.
    /// </summary>
    /// <param name="entity">The entity to check</param>
    /// <param name="currentGrid">The grid where the entity is being sold</param>
    /// <returns>The modified price if applicable, null otherwise</returns>
    public double? GetVendingMachineDiscountPrice(EntityUid entity, EntityUid currentGrid)
    {
        if (!TryComp<VendingMachinePurchaseComponent>(entity, out var component))
            return null;

        if (!TryComp<StaticPriceComponent>(entity, out var staticPrice))
            return null;

        // Only apply discount if on the same grid as original purchase
        if (component.PurchaseGrid == currentGrid)
        {
            return staticPrice.Price * 0.5; // 50% discount
        }

        return null;
    }

    /// <summary>
    /// Checks if an entity was purchased from a vending machine on the specified grid.
    /// </summary>
    /// <param name="entity">The entity to check</param>
    /// <param name="gridUid">The grid to check against</param>
    /// <returns>True if the entity was purchased from a vending machine on the specified grid</returns>
    public bool WasPurchasedOnGrid(EntityUid entity, EntityUid gridUid)
    {
        if (!TryComp<VendingMachinePurchaseComponent>(entity, out var component))
            return false;

        return component.PurchaseGrid == gridUid;
    }
}
