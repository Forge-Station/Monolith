using Content.Server._NF.Shipyard.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Bank.BUI;
using Content.Shared.Database;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    /// <summary>
    /// Forge-change: Loads a shuttle template and returns its appraisal value without keeping the grid.
    /// </summary>
    public bool TryAppraiseShuttleTemplate(ResPath shuttlePath, out int appraisal)
    {
        appraisal = 0;

        if (!TryAddShuttle(shuttlePath, out var shuttleGrid))
            return false;

        appraisal = (int)_pricing.AppraiseGrid(shuttleGrid.Value, LacksPreserveOnSaleComp);
        QueueDel(shuttleGrid.Value);
        return true;
    }

    /// <summary>
    /// Forge-change: Calculates the sell value shown in the shipyard console UI.
    /// </summary>
    private int CalculateDisplayedSellValue(EntityUid consoleUid, ShipyardConsoleComponent component, ShuttleDeedComponent? deed)
    {
        if (deed?.ShuttleUid is not { Valid: true } shuttleUid)
            return 0;

        var currentAppraisal = (int)_pricing.AppraiseGrid(shuttleUid, LacksPreserveOnSaleComp);
        var baseline = deed.PurchasedWithVoucher ? GetSpawnAppraisalBaseline(deed, shuttleUid) : 0;
        return CalculateSellPayout((consoleUid, component), currentAppraisal, baseline);
    }

    /// <summary>
    /// Forge-change: Resolves the spawn-time appraisal baseline for voucher resale payouts.
    /// </summary>
    private int GetSpawnAppraisalBaseline(ShuttleDeedComponent deed, EntityUid shuttleUid)
    {
        if (deed.SpawnAppraisalValue > 0)
            return deed.SpawnAppraisalValue;

        if (!deed.PurchasedWithVoucher)
            return 0;

        if (TryComp<VesselComponent>(shuttleUid, out var vessel)
            && _prototypeManager.TryIndex(vessel.VesselId, out VesselPrototype? proto)
            && TryAppraiseShuttleTemplate(proto.ShuttlePath, out var templateAppraisal))
        {
            deed.SpawnAppraisalValue = templateAppraisal;
            return templateAppraisal;
        }

        return 0;
    }

    /// <summary>
    /// Forge-change: Applies sell rate and taxes to the taxable portion of a ship appraisal.
    /// </summary>
    private int CalculateSellPayout(Entity<ShipyardConsoleComponent?> console, int appraisal, int baseline = 0)
    {
        var taxable = Math.Max(0, appraisal - baseline);
        return CalculateShipResaleValue(console, taxable);
    }

    /// <summary>
    /// Forge-change: Deposits voucher or standard shuttle sale payout to the player.
    /// </summary>
    private int ApplyShuttleSalePayout(EntityUid player, EntityUid consoleUid, ShipyardConsoleComponent component, int rawAppraisal, int baseline = 0)
    {
        var bill = Math.Max(0, rawAppraisal - baseline);
        if (bill <= 0)
            return 0;

        if (!component.IgnoreBaseSaleRate)
            bill = (int)(bill * _baseSaleRate);

        var originalBill = bill;
        foreach (var (account, taxCoeff) in component.TaxAccounts)
        {
            var tax = CalculateSalesTax(originalBill, taxCoeff);
            _bank.TrySectorDeposit(account, tax, LedgerEntryType.ShipyardTax);
            bill -= tax;
        }

        bill = int.Max(0, bill);
        _bank.TryBankDeposit(player, bill);
        PlayConfirmSound(player, consoleUid, component);
        return bill;
    }
}
