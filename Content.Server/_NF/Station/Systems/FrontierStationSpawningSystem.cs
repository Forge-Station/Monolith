using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Station.Systems;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Station.Systems;

/// <summary>
/// Charges frontier loadout prices and equips fallbacks the character can afford.
/// </summary>
public sealed class FrontierStationSpawningSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private StationSpawningSystem _spawning = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerRoleLoadoutEquipEvent>(OnRoleLoadoutEquip);
    }

    private void OnRoleLoadoutEquip(ref PlayerRoleLoadoutEquipEvent ev)
    {
        if (ev.Profile == null)
            return;

        ev.Handled = true;

        var profile = ev.Profile;
        var entity = ev.Entity;
        var loadout = ev.Loadout;
        var roleProto = ev.RoleProto;
        var session = ev.Session;
        var initialBankBalance = profile.BankBalance;
        var bankBalance = initialBankBalance;

        PlayerPreferences? prefs = null;
        var hasBalance = session != null &&
                         _preferences.TryGetCachedPreferences(session.UserId, out prefs) &&
                         prefs.IndexOfCharacter(profile) != -1;

        foreach (var group in loadout.SelectedLoadouts.OrderBy(x => roleProto.Groups.FindIndex(e => e == x.Key)))
        {
            var equippedItems = new List<ProtoId<LoadoutPrototype>>();
            foreach (var items in group.Value)
            {
                if (!_prototypes.TryIndex(items.Prototype, out var loadoutProto))
                {
                    Log.Error($"Unable to find loadout prototype for {items.Prototype}");
                    continue;
                }

                // Unloaded preferences only receive free items. Priced items are skipped until the cache exists.
                if (loadoutProto.Price <= bankBalance && (loadoutProto.Price <= 0 || hasBalance))
                {
                    bankBalance -= int.Max(0, loadoutProto.Price);
                    _spawning.EquipStartingGear(entity, loadoutProto, raiseEvent: false);
                    equippedItems.Add(loadoutProto.ID);
                }
            }

            if (_prototypes.TryIndex(group.Key, out var groupPrototype) &&
                equippedItems.Count < groupPrototype.MinLimit)
            {
                foreach (var fallback in groupPrototype.Fallbacks)
                {
                    if (equippedItems.Contains(fallback))
                        continue;

                    if (!_prototypes.TryIndex(fallback, out var loadoutProto))
                    {
                        Log.Error($"Unable to find loadout prototype for fallback {fallback}");
                        continue;
                    }

                    if (!loadout.IsValid(profile, session, fallback, IoCManager.Instance!, out _))
                        continue;

                    _spawning.EquipStartingGear(entity, loadoutProto, raiseEvent: false);
                    equippedItems.Add(fallback);

                    if (equippedItems.Count >= groupPrototype.MinLimit)
                        break;
                }
            }
        }

        EnsureComp<BankAccountComponent>(entity);

        if (hasBalance)
        {
            _bank.TryBankWithdraw(session!, prefs!, profile, initialBankBalance - bankBalance, out _, spendLongTerm: false);
        }
    }
}
