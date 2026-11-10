using System.Linq;
using Content.Server._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    [Dependency] private IComponentFactory _factionComponents = default!;

    /// <summary>
    /// TSF and PDV offer the ships on their existing T0 and T1 vouchers, including downstream additions. VG, USSP and
    /// the Underground have no tier vouchers: they offer their shipyard's purchasable ships and every ship on one of their
    /// company's vouchers, which a TSF or PDV crew would also buy with cash. A voucher-only ship on no voucher stays out.
    /// </summary>
    private HashSet<string> FactionVessels(WFCrewFaction faction)
    {
        var vessels = new HashSet<string>();
        var company = WFFactionCrewPolicy.Company(faction);
        var prefix = faction switch { WFCrewFaction.TSF => "Tsf", WFCrewFaction.PDV => "Pdv", _ => null };
        if (prefix != null)
        {
            foreach (var tier in new[] { 0, 1 })
            {
                if (_prototypes.TryIndex<EntityPrototype>($"ShipVoucher{prefix}T{tier}", out var prototype))
                    AddVoucher(prototype, company, vessels);
            }
            return vessels;
        }
        if (company == null || FactionGroup(faction) is not { } group)
            return vessels;
        if (VoucherVessels().TryGetValue(company, out var vouchered))
            vessels.UnionWith(vouchered);
        foreach (var vessel in _prototypes.EnumeratePrototypes<VesselPrototype>())
        {
            if (vessel.Group == group && vessel.Purchasable)
                vessels.Add(vessel.ID);
        }
        return vessels;
    }

    /// <summary>Every voucher's ships by company, built once per prototype load.</summary>
    private Dictionary<string, HashSet<string>> VoucherVessels()
    {
        if (_voucherVessels != null)
            return _voucherVessels;
        _voucherVessels = new Dictionary<string, HashSet<string>>();
        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (!prototype.Components.TryGetComponent<ShipyardVoucherComponent>(_factionComponents, out var voucher) ||
                voucher.CompanyName is not { } company)
                continue;
            if (!_voucherVessels.TryGetValue(company, out var set))
                _voucherVessels[company] = set = new HashSet<string>();
            foreach (var id in voucher.Vessels)
                set.Add(id.Id);
        }
        return _voucherVessels;
    }

    private Dictionary<string, HashSet<string>>? _voucherVessels;

    private void OnFactionPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            _voucherVessels = null;
    }

    private void AddVoucher(EntityPrototype prototype, string? company, HashSet<string> vessels)
    {
        if (!prototype.Components.TryGetComponent<ShipyardVoucherComponent>(_factionComponents, out var voucher) ||
            voucher.CompanyName != company)
            return;
        foreach (var id in voucher.Vessels)
            vessels.Add(id.Id);
    }

    /// <summary>The shipyard a faction's ships are sold from.</summary>
    private static ShipyardConsoleUiKey? FactionGroup(WFCrewFaction faction) => faction switch
    {
        WFCrewFaction.TSF => ShipyardConsoleUiKey.Security,
        WFCrewFaction.PDV => ShipyardConsoleUiKey.BlackMarket,
        WFCrewFaction.VG => ShipyardConsoleUiKey.Viper,
        WFCrewFaction.USSP => ShipyardConsoleUiKey.Ussp,
        WFCrewFaction.Underground => ShipyardConsoleUiKey.Syndicate,
        _ => null,
    };

    private bool IsFactionVessel(VesselPrototype vessel, WFCrewFaction faction, HashSet<string> catalog) =>
        !vessel.Abstract && vessel.Price > 0 && FactionGroup(faction) is { } group && vessel.Group == group &&
        catalog.Contains(vessel.ID);
}
