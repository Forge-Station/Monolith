using System.Linq;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Companies;
using Content.Shared.Clothing;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Player;

namespace Content.Server._WF.Companies;

// Rank loadouts: kit the company buys its members at each spawn, falling back to the member's own bank.
public sealed partial class WFCompanySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private LoadoutSystem _loadouts = default!;

    /// <summary>Members whose loadout the company has paid for this round; later spawns are on the member.</summary>
    private readonly HashSet<int> _companyPaidLoadouts = new();

    /// <summary>The owner edits their own rank's loadout; others need Edit ranks and to outrank the rank.</summary>
    private static bool CanEditLoadout(Company company, Member you, Rank rank)
    {
        if (rank == company.OwnerRank)
            return you.Profile == company.Owner;
        var order = company.Ranks.IndexOf(rank);
        return order >= 0 && Has(company, you, WFCompanyPermissions.EditRanks) && WFCompanyPolicy.Outranks(RankOrder(company, you), order);
    }

    private string? SetRankLoadout(Company company, Rank rank, string[]? loadouts)
    {
        var picked = (loadouts ?? Array.Empty<string>()).Distinct().ToList();
        if (picked.Count > WFCompanyPolicy.MaximumRankLoadouts)
            return "wf-company-error-loadout";
        var allowed = WFCompanyLoadouts.AllowedIds(_prototypes);
        if (picked.Any(id => !allowed.Contains(id)))
            return "wf-company-error-loadout";
        rank.Loadout = picked;
        if (rank == company.OwnerRank)
            PersistOwnerRank(company);
        else
            PersistRanks(company);
        return null;
    }

    /// <summary>Spawns the member's rank loadout. The company pays once per member per round while its account covers it, so
    /// respawning can't farm it; otherwise the member pays, and anything unpaid is skipped.</summary>
    private void IssueRankLoadout(ICommonSession session, EntityUid body, int slot)
    {
        if (!_profileIds.TryGetValue(session.UserId, out var ids) || !ids.TryGetValue(slot, out var profile) ||
            CompanyOf(profile) is not { } company)
            return;
        var member = company.Members[profile];
        if (IconRankOf(company, member) is not { Loadout.Count: > 0 } rank)
            return;
        var allowed = WFCompanyLoadouts.Catalogue(_prototypes).ToDictionary(entry => entry.Loadout.ID, entry => entry.Loadout);
        var budget = company.Frozen || _companyPaidLoadouts.Contains(profile) ? 0 : company.Balance;
        long companyPaid = 0;
        long memberPaid = 0;
        var issued = new List<LoadoutPrototype>();
        var missed = new List<string>();
        foreach (var id in rank.Loadout)
        {
            if (!allowed.TryGetValue(id, out var loadout))
                continue;
            var price = Math.Max(0, loadout.Price);
            if (price <= budget - companyPaid)
                companyPaid += price;
            else if (WithdrawFromMember(session, body, member, price))
                memberPaid += price;
            else
            {
                missed.Add(_loadouts.GetName(loadout));
                continue;
            }
            issued.Add(loadout);
        }
        if (companyPaid > 0)
        {
            Transact(company, WFCompanyLedgerKind.Loadout, -companyPaid, member.Profile, member.Name, rank.Title);
            _companyPaidLoadouts.Add(profile);
        }

        var coordinates = Transform(body).Coordinates;
        foreach (var loadout in issued)
        {
            foreach (var proto in WFCompanyLoadouts.Items(_prototypes, loadout))
            {
                var item = Spawn(proto, coordinates);
                if (_inventory.TryGetSlotEntity(body, "back", out var bag) && _storage.Insert(bag.Value, item, out _, playSound: false))
                    continue;
                _hands.PickupOrDrop(body, item, checkActionBlocker: false, animate: false);
            }
        }
        if (issued.Count > 0)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("wf-company-loadout-issued", ("company", company.Name),
                ("count", issued.Count), ("company_paid", companyPaid), ("member_paid", memberPaid)));
        }
        if (missed.Count > 0)
            _chat.DispatchServerMessage(session, Loc.GetString("wf-company-loadout-missed", ("items", string.Join(", ", missed))));
    }

    private bool WithdrawFromMember(ICommonSession session, EntityUid body, Member member, int amount)
    {
        if (amount <= 0)
            return true;
        if (HasComp<BankAccountComponent>(body))
            return _bank.TryBankWithdraw(body, amount);
        return _preferences.GetPreferencesOrNull(session.UserId) is { } prefs &&
               prefs.Characters.GetValueOrDefault(member.Slot) is HumanoidCharacterProfile profile &&
               _bank.TryBankWithdraw(session, prefs, profile, amount, out _);
    }
}
