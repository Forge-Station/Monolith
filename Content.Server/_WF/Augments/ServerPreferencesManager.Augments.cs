using System.Linq;
using Content.Shared._WF.Augments;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Preferences.Managers;

public sealed partial class ServerPreferencesManager
{
    /// <summary>
    /// Settles the augments of a profile a client saved. Unlocks only come from the stored profile. A priced augment
    /// it hasn't unlocked is bought from the character's bank if the balance covers it, and dropped otherwise.
    /// </summary>
    /// <returns>Whether anything was bought or dropped, so the client's copy is out of date.</returns>
    private bool SettleAugments(NetUserId userId, ref HumanoidCharacterProfile profile, HumanoidCharacterProfile? stored)
    {
        var unlocks = new HashSet<ProtoId<WFAugmentPrototype>>(stored?.AugmentUnlocks ?? new());
        var choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
        var balance = profile.BankBalance;
        var changed = false;

        foreach (var (slot, choice) in profile.Augments.OrderBy(c => c.Key))
        {
            if (!_protos.TryIndex(choice.Augment, out var augment))
            {
                changed = true;
                continue;
            }

            if (augment.Price > 0 && !unlocks.Contains(augment.ID))
            {
                if (balance < augment.Price)
                {
                    changed = true;
                    continue;
                }

                balance -= augment.Price;
                unlocks.Add(augment.ID);
                changed = true;
                _sawmill.Info($"User {userId} unlocked augment {augment.ID} on {profile.Name} for {augment.Price}.");
            }

            choices[slot] = choice;
        }

        profile = profile.WithBankBalance(balance).WithAugmentUnlocks(unlocks).WithAugments(choices);
        return changed;
    }

    /// <summary>Sends a player their preferences again, so the creator shows what the server changed.</summary>
    public void ResendPreferences(NetUserId userId)
    {
        if (_cachedPlayerPrefs.TryGetValue(userId, out var data) && data.Prefs is { } prefs)
            ResendPreferences(userId, prefs);
    }

    /// <summary>Sends a player their preferences again, so the creator shows what the server changed.</summary>
    private void ResendPreferences(NetUserId userId, PlayerPreferences prefs)
    {
        if (!_playerManager.TryGetSessionById(userId, out var session))
            return;

        _netManager.ServerSendMessage(new MsgPreferencesAndSettings
        {
            Preferences = prefs,
            Settings = new GameSettings { MaxCharacterSlots = MaxCharacterSlots },
        }, session.Channel);
    }
}
