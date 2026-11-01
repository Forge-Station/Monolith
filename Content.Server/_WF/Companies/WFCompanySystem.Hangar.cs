using System.Linq;
using System.Threading.Tasks;
using Content.Server.Cargo.Systems;
using Content.Server.Database;
using Content.Server._WF.RoundSetup;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Companies;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Preferences;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Companies;

public sealed partial class WFCompanySystem
{
    [Dependency] private WFShipScenarioSystem _shipScenarios = default!;
    [Dependency] private PricingSystem _pricing = default!;

    private sealed class Licence
    {
        public int Id;
        public string Vessel = string.Empty;
        /// <summary>The name this ship launches under, kept between rounds.</summary>
        public string ShipName = string.Empty;
    }

    /// <summary>What a launch charged, so a failed spawn can give it back.</summary>
    private sealed record LaunchCharge(int CompanyId, int Registry, int Fee, NetUserId Host);

    /// <summary>Licences whose ship is out this round.</summary>
    private readonly Dictionary<int, LaunchCharge> _launched = new();
    /// <summary>Licences held by a crew that hasn't launched yet.</summary>
    private readonly Dictionary<int, NetUserId> _reserved = new();

    /// <summary>Marks everything aboard a freshly launched hangar ship, contents included, as company property worth nothing to sell.</summary>
    public void MarkCompanyProperty(EntityUid grid, int companyId)
    {
        var pending = new Stack<EntityUid>();
        pending.Push(grid);
        while (pending.Count > 0)
        {
            var parent = pending.Pop();
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                EnsureComp<WFCompanyPropertyComponent>(child).CompanyId = companyId;
                pending.Push(child);
            }
        }
    }

    /// <summary>Company property sells for nothing itself; whatever was put inside it later keeps its value.</summary>
    private void OnPropertyPrice(Entity<WFCompanyPropertyComponent> property, ref PriceCalculationEvent args)
    {
        args.Handled = true;
        args.Price = 0;
        if (!TryComp<ContainerManagerComponent>(property, out var containers))
            return;
        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
                args.Price += _pricing.GetPrice(contained);
        }
    }

    /// <summary>A company's worth for its headline: the account plus what its fleet licences cost.</summary>
    private long CompanyValue(Company company) =>
        company.Balance + company.Licences.Sum(licence => WFCompanyPolicy.LicencePrice(ShipyardPrice(licence.Vessel)));

    private int LaunchedCount(Company company) => company.Licences.Count(licence => _launched.ContainsKey(licence.Id));

    private int ShipyardPrice(string vessel) => _prototypes.TryIndex<VesselPrototype>(vessel, out var prototype) ? prototype.Price : 0;

    private WFCompanyLicence[] SnapshotLicences(Company company) => company.Licences.Select(licence =>
        new WFCompanyLicence(licence.Id, licence.Vessel,
            _prototypes.TryIndex<VesselPrototype>(licence.Vessel, out var vessel) ? vessel.Name : licence.Vessel, ShipyardPrice(licence.Vessel),
            _launched.ContainsKey(licence.Id) || _reserved.ContainsKey(licence.Id), licence.ShipName)).ToArray();

    /// <summary>What's in the hangar, which anyone may see even when the member list is private.</summary>
    private WFCompanyProfileShip[] ProfileShips(Company company) => company.Licences.Select(licence =>
        new WFCompanyProfileShip(_prototypes.TryIndex<VesselPrototype>(licence.Vessel, out var vessel) ? vessel.Name : licence.Vessel,
            licence.ShipName, _launched.ContainsKey(licence.Id))).ToArray();

    /// <summary>Names a licence's ship; every later launch uses the name.</summary>
    private string? SetLicenceName(Company company, int licenceId, string? name)
    {
        if (company.Licences.FirstOrDefault(licence => licence.Id == licenceId && licence.Id != 0) is not { } licence)
            return "wf-company-error-licence";
        if (!WFRoundSetupPolicy.TryNormalizeTitle(name, out var shipName))
            return "wf-company-error-ship-name";
        RememberName(company, licence, shipName);
        return null;
    }

    /// <summary>Keeps a name given to a hangar ship in its crew's setup.</summary>
    public void RememberShipName(int licenceId, string name)
    {
        if (_companies.Values.FirstOrDefault(entry => entry.Licences.Any(licence => licence.Id == licenceId)) is { } company)
            RememberName(company, company.Licences.First(licence => licence.Id == licenceId), name);
    }

    private void RememberName(Company company, Licence licence, string name)
    {
        if (licence.ShipName == name)
            return;
        licence.ShipName = name;
        Track(_db.SetCompanyLicenceNameAsync(licence.Id, name));
        Touch(company);
    }

    /// <summary>Civilian designs plus the company-only Mercenary and Mieyo designs, cheapest first.</summary>
    private WFCompanyLicenceOffer[] LicenceOffers(Company company)
    {
        var civilian = _shipScenarios.GetVessels().Where(vessel => _shipScenarios.IsVesselAvailable(vessel.Id))
            .Select(vessel => (vessel.Id, vessel.Name, vessel.Price));
        var special = _prototypes.EnumeratePrototypes<VesselPrototype>()
            .Where(vessel => !vessel.Abstract && vessel.Purchasable && vessel.Price > 0 && WFCompanyPolicy.CompanyVesselAccess.Contains(vessel.Access))
            .Select(vessel => (vessel.ID, vessel.Name, vessel.Price));
        return civilian.Concat(special).DistinctBy(vessel => vessel.Item1)
            .Where(vessel => company.Licences.All(licence => licence.Vessel != vessel.Item1))
            .OrderBy(vessel => vessel.Price).ThenBy(vessel => vessel.Name)
            .Select(vessel => new WFCompanyLicenceOffer(vessel.Item1, vessel.Name, WFCompanyPolicy.LicencePrice(vessel.Price))).ToArray();
    }

    private string? BuyLicence(Company company, Member owner, string? vessel, out string? success)
    {
        success = null;
        if (company.Licences.Count >= WFCompanyPolicy.MaximumLicences)
            return "wf-company-error-licences-full";
        if (PlayerCount(company) < WFCompanyPolicy.MinimumFleetMembers)
            return "wf-company-error-fleet-members";
        if (vessel == null || LicenceOffers(company).FirstOrDefault(offer => offer.Vessel == vessel) is not { } offer)
            return "wf-company-error-licence";
        if (company.Frozen)
            return "wf-company-error-frozen";
        if (!Transact(company, WFCompanyLedgerKind.LicenceBuy, -offer.Price, owner.Profile, owner.Name, offer.Name))
            return "wf-company-error-balance";
        var licence = new Licence { Vessel = vessel };
        company.Licences.Add(licence);
        Track(AddLicenceRow(company, licence, offer.Price));
        success = "wf-company-licence-bought";
        return null;
    }

    private async Task AddLicenceRow(Company company, Licence licence, long price)
    {
        licence.Id = await _db.AddCompanyLicenceAsync(new WolfgateCompanyLicence
        {
            CompanyId = company.Id, Vessel = licence.Vessel, PricePaid = price, BoughtAt = DateTime.UtcNow,
        });
        Touch(company);
    }

    private string? SellLicence(Company company, int licenceId)
    {
        if (company.Licences.FirstOrDefault(licence => licence.Id == licenceId) is not { } licence)
            return "wf-company-error-licence";
        if (_launched.ContainsKey(licenceId) || _reserved.ContainsKey(licenceId))
            return "wf-company-error-licence-out";
        company.Licences.Remove(licence);
        Track(_db.DeleteCompanyLicenceAsync(licenceId));
        return null;
    }

    /// <summary>Starts a Crew Setup ship crew for the licence; the fees are only taken when the ship launches.</summary>
    private string? LaunchLicence(Company company, ICommonSession session, int licenceId, out string? success)
    {
        success = null;
        if (company.Licences.FirstOrDefault(licence => licence.Id == licenceId && licence.Id != 0) is not { } licence)
            return "wf-company-error-licence";
        if (_launched.ContainsKey(licenceId) || _reserved.ContainsKey(licenceId))
            return "wf-company-error-licence-out";
        if (PlayerCount(company) < WFCompanyPolicy.MinimumFleetMembers)
            return "wf-company-error-fleet-members";
        if (!_roundSetup.CreateHangarCrew(session, company.Id, licence.Id, licence.Vessel, licence.ShipName, out var error))
            return error;
        _reserved[licenceId] = session.UserId;
        success = "wf-company-launch-ready";
        return null;
    }

    /// <summary>Takes the registry fee from the account and the launch fee from the launching character, as the ship spawns.</summary>
    public bool ChargeLaunch(int licenceId, ICommonSession host, out string error)
    {
        error = "wf-company-error-licence";
        var company = _companies.Values.FirstOrDefault(entry => entry.Licences.Any(licence => licence.Id == licenceId));
        if (company == null || _launched.ContainsKey(licenceId))
            return false;
        // The host may have switched to a character who can't launch for this company.
        error = "wf-company-error-denied";
        if (CurrentCharacter(host) is not { } launcher || CompanyOf(launcher.ProfileId) != company ||
            !Has(company, company.Members[launcher.ProfileId], WFCompanyPermissions.Launch))
            return false;
        var vessel = company.Licences.First(licence => licence.Id == licenceId).Vessel;
        var registry = WFCompanyPolicy.RegistryFee(ShipyardPrice(vessel), LaunchedCount(company));
        var fee = company.LaunchFee;
        error = "wf-company-error-frozen";
        if (company.Frozen)
            return false;
        error = "wf-company-error-registry";
        if (company.Balance < registry)
            return false;
        error = "wf-company-error-launch-fee";
        var name = _preferences.GetPreferencesOrNull(host.UserId)?.SelectedCharacter is HumanoidCharacterProfile profile ? profile.Name : host.Name;
        if (fee > 0 && (_preferences.GetPreferencesOrNull(host.UserId) is not { } prefs || prefs.SelectedCharacter is not HumanoidCharacterProfile payer ||
                        !_bank.TryBankWithdraw(host, prefs, payer, fee, out _)))
            return false;
        Transact(company, WFCompanyLedgerKind.RegistryFee, -registry, null, name, vessel);
        if (fee > 0)
            Transact(company, WFCompanyLedgerKind.LaunchFee, fee, null, name, vessel);
        _reserved.Remove(licenceId);
        _launched[licenceId] = new LaunchCharge(company.Id, registry, fee, host.UserId);
        Touch(company);
        return true;
    }

    /// <summary>Gives back what a launch charged when the ship then fails to spawn.</summary>
    public void RefundLaunch(int licenceId)
    {
        if (!_launched.Remove(licenceId, out var charge) || !_companies.TryGetValue(charge.CompanyId, out var company))
            return;
        Transact(company, WFCompanyLedgerKind.RegistryFee, charge.Registry, null, "Refund", "launch failed", ignoreFrozen: true);
        if (charge.Fee > 0 && Transact(company, WFCompanyLedgerKind.LaunchFee, -charge.Fee, null, "Refund", "launch failed", ignoreFrozen: true) &&
            _players.TryGetSessionById(charge.Host, out var host) && _preferences.GetPreferencesOrNull(host.UserId) is { } prefs &&
            prefs.SelectedCharacter is HumanoidCharacterProfile payer)
            _bank.TryBankDeposit(host, prefs, payer, charge.Fee, out _);
        _reserved[licenceId] = charge.Host;
        Touch(company);
    }

    /// <summary>Frees a licence whose crew disbanded before launching.</summary>
    public void ReleaseLicence(int licenceId)
    {
        if (!_reserved.Remove(licenceId))
            return;
        if (_companies.Values.FirstOrDefault(company => company.Licences.Any(licence => licence.Id == licenceId)) is { } owner)
            Touch(owner);
    }
}
