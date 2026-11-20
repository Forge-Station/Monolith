using Content.Server._NF.Atmos.Components;
using Content.Server.Popups;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Store;

namespace Content.Server._NF.Atmos.Systems;

public sealed partial class GasDepositSystem
{
    [Dependency] private PopupSystem _popup = default!;

    /// <summary>Stack the stock gas consoles pay in, shown as credits.</summary>
    private const string CreditCash = "Credit";

    /// <summary>Short name of the currency a gas console pays in, such as MIC; null when it pays credits.</summary>
    private string? PayoutCurrency(Entity<GasSaleConsoleComponent> ent)
    {
        var cash = ent.Comp.CashType;
        if (cash == CreditCash)
            return null;

        // A currency shares its cash stack's ID and has the short display name.
        return _prototype.TryIndex<CurrencyPrototype>(cash.Id, out var currency)
            ? Loc.GetString(currency.DisplayName)
            : _prototype.Index(cash).Name;
    }

    /// <summary>Refuses a sale that would pay nothing, leaving the gas in the sale points.</summary>
    private bool RefuseWorthlessSale(Entity<GasSaleConsoleComponent> ent, EntityUid gridUid, EntityUid user)
    {
        GetNearbyMixtures(ent, gridUid, out _, out var value);
        if (TryComp<MarketModifierComponent>(ent, out var priceMod))
            value *= priceMod.Mod;

        if ((int) value > 0)
            return false;

        _popup.PopupEntity(Loc.GetString("gas-sale-worthless"), ent, user);
        UpdateConsoleInterface(ent);
        return true;
    }
}
