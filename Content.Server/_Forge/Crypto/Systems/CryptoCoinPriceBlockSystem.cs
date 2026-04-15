using Content.Server.Cargo.Systems;
using Content.Shared._Forge.Crypto.Components;

namespace Content.Server._Forge.Crypto.Systems;

/// <summary>
/// Prevents generic cargo pricing from valuing crypto coins.
/// They must be sold through the dedicated crypto console.
/// </summary>
public sealed class CryptoCoinPriceBlockSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<CryptoCoinComponent, PriceCalculationEvent>(OnPriceCalculation);
    }

    private void OnPriceCalculation(Entity<CryptoCoinComponent> ent, ref PriceCalculationEvent args)
    {
        args.Handled = true;
        args.Price = 0;
    }
}
