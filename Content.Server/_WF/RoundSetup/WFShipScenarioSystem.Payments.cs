using System.Linq;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    /// <summary>A verified contributor and their explicitly approved character slot and amount.</summary>
    public sealed record Contribution(ICommonSession Player, int Slot, int Amount);

    /// <summary>Retains the ordinary single-buyer purchase path.</summary>
    public bool TryCharge(int lobbyId, ICommonSession host, out string reason) => TryCharge(lobbyId, host,
        _scenarios.TryGetValue(lobbyId, out var scenario)
            ? new[] { new Contribution(host, _preferences.GetPreferences(host.UserId).SelectedCharacterIndex,
                _prototypes.Index<VesselPrototype>(scenario.Vessel).Price) }
            : Array.Empty<Contribution>(), out reason);

    /// <summary>Checks every contributor before withdrawing, retaining separate receipts for rollback.</summary>
    public bool TryCharge(int lobbyId, ICommonSession host, IReadOnlyCollection<Contribution> contributions, out string reason)
    {
        reason = "wf-round-setup-ship-gone";
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || TerminatingOrDeleted(scenario.Station) ||
            TerminatingOrDeleted(scenario.Hull) || !TryGetSpawn(lobbyId, out _) ||
            !IsVesselAvailable(scenario.Vessel, scenario.Faction, scenario.PurchaseJob))
            return false;
        if (!HasSectorServices())
        {
            reason = "wf-round-setup-ship-no-sector";
            return false;
        }
        var vessel = _prototypes.Index<VesselPrototype>(scenario.Vessel);
        reason = "wf-round-setup-payment-total-error";
        if (contributions.Count == 0 || contributions.Any(part => part.Amount <= 0) ||
            contributions.Select(part => part.Player.UserId).Distinct().Count() != contributions.Count ||
            contributions.Sum(part => (long) part.Amount) != vessel.Price)
            return false;
        if (scenario.Paid > 0)
            return scenario.Buyer?.UserId == host.UserId && scenario.Paid == vessel.Price &&
                scenario.Receipts.Count == contributions.Count && contributions.All(part =>
                    scenario.Receipts.Contains(new PendingRefund(part.Player.UserId, part.Slot, part.Amount)));
        foreach (var part in contributions)
        {
            reason = "wf-round-setup-ship-bank-unavailable";
            var preferences = _preferences.GetPreferencesOrNull(part.Player.UserId);
            if (!_refundUserDb.IsLoadComplete(part.Player) || preferences?.SelectedCharacterIndex != part.Slot ||
                preferences.SelectedCharacter is not HumanoidCharacterProfile profile || profile.TraitPreferences.Contains("Ironman"))
                return false;
            reason = "wf-round-setup-payment-funds-error";
            if (profile.BankBalance < part.Amount)
                return false;
        }
        var attempt = new AttemptShipyardShuttlePurchaseEvent(scenario.Hull, host.AttachedEntity ?? EntityUid.Invalid, vessel);
        RaiseLocalEvent(ref attempt);
        if (attempt.Cancelled)
        {
            reason = attempt.CancelReason;
            return false;
        }
        foreach (var part in contributions)
        {
            var preferences = _preferences.GetPreferences(part.Player.UserId);
            reason = "wf-round-setup-payment-funds-error";
            if (preferences.SelectedCharacterIndex != part.Slot || preferences.SelectedCharacter is not HumanoidCharacterProfile profile ||
                !_bank.TryBankWithdraw(part.Player, preferences, profile, part.Amount, out _))
            {
                RefundPayments(scenario);
                return false;
            }
            scenario.Receipts.Add(new PendingRefund(part.Player.UserId, part.Slot, part.Amount));
            scenario.Paid += part.Amount;
        }
        scenario.Buyer = host;
        return true;
    }

    /// <summary>Refunds each original character once, retaining unavailable accounts in the retry queue.</summary>
    private void RefundPayments(Scenario scenario)
    {
        foreach (var receipt in scenario.Receipts)
        {
            if (!TryRefund(receipt))
            {
                _pendingRefunds.Add(receipt);
                Log.Warning($"Queued crew purchase refund of {receipt.Amount} for {receipt.User} slot {receipt.Slot} until preferences load.");
            }
        }
        scenario.Receipts.Clear();
        scenario.Paid = 0;
        scenario.Buyer = null;
    }
}
