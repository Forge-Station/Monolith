using System.Linq;
using Content.Server.Database;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Network;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    [Dependency] private IPlayerManager _refundPlayers = default!;
    [Dependency] private UserDbDataManager _refundUserDb = default!;

    /// <summary>Payment receipts survive round cleanup independently of scenario entities and old sessions.</summary>
    private readonly List<PendingRefund> _pendingRefunds = new();
    private float _refundRetry;

    private sealed record PendingRefund(NetUserId User, int Slot, int Amount);

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _refundRetry -= frameTime;
        if (_refundRetry > 0 || _pendingRefunds.Count == 0)
            return;
        _refundRetry = 1;
        foreach (var refund in _pendingRefunds.ToArray())
        {
            if (TryRefund(refund))
                _pendingRefunds.Remove(refund);
        }
    }

    /// <summary>Credits the original slot using fresh preferences after a reconnect has finished loading.</summary>
    private bool TryRefund(PendingRefund refund)
    {
        if (!_refundPlayers.TryGetSessionById(refund.User, out var session) || !_refundUserDb.IsLoadComplete(session))
            return false;
        var preferences = _preferences.GetPreferencesOrNull(refund.User);
        return preferences != null && preferences.Characters.TryGetValue(refund.Slot, out var current) &&
               current is HumanoidCharacterProfile profile && profile.BankBalance <= int.MaxValue - refund.Amount &&
               _bank.TryBankDeposit(session, preferences, profile, refund.Amount, out _);
    }
}
