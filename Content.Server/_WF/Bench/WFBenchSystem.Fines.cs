using System.Linq;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Bank.Events;
using Content.Shared._WF.Bench;
using Content.Shared._WF.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Security;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Bench;

/// <summary>Taking fines, garnishing debt from later gains, and making long or large debtors Wanted.</summary>
public sealed partial class WFBenchSystem
{
    [Dependency] private IPlayerManager _players = default!;

    private const SectorBankAccount _nfsd = SectorBankAccount.Nfsd;

    private readonly Dictionary<NetUserId, WFBenchDebtor> _debtors = new();

    /// <summary>What a player still owes the Bench across all their tickets.</summary>
    public int Owed(NetUserId user)
    {
        return _tickets.Where(t => t.User == user && !t.Pardoned).Sum(t => t.Owed);
    }

    /// <summary>Takes what the offender can pay of a new ticket and starts watching their bank for the rest.</summary>
    private void Collect(ICommonSession session, WFBenchTicket ticket)
    {
        _bank.TryGetBalance(session, out var balance);
        var take = WFBenchMath.Collect(balance, ticket.Total);
        if (take > 0 && Take(session, take))
        {
            ticket.Paid = take;
            _bank.TrySectorDeposit(_nfsd, take, LedgerEntryType.StationDepositFines);
        }

        ticket.Owed = ticket.Total - ticket.Paid;
        if (ticket.Owed <= 0)
            return;

        var debtor = Debtor(session.UserId);
        _bank.TryGetBalance(session, out var after);
        debtor.LastBalance = after;
    }

    private WFBenchDebtor Debtor(NetUserId user)
    {
        if (!_debtors.TryGetValue(user, out var debtor))
            _debtors[user] = debtor = new WFBenchDebtor();
        return debtor;
    }

    /// <summary>Notes each gain to a debtor's bank; the garnish is taken on the next update, outside the bank's call.</summary>
    private void OnBalanceChanged(BalanceChangedEvent ev)
    {
        if (!_debtors.TryGetValue(ev.Session.UserId, out var debtor))
            return;

        if (debtor.LastBalance is { } last && ev.Amount > last)
            debtor.Pending += ev.Amount - last;
        debtor.LastBalance = ev.Amount;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;
        Garnish();
        foreach (var user in _tickets.Where(t => t.Owed > 0 && !t.Overdue).Select(t => t.User).Distinct().ToList())
        {
            Escalate(user);
        }

        RefreshOpen();
    }

    /// <summary>Takes each debtor's share of what they gained since the last update, oldest ticket first.</summary>
    private void Garnish()
    {
        var share = _cfg.GetCVar(BenchCVars.Garnish);
        foreach (var (user, debtor) in _debtors)
        {
            if (debtor.Pending <= 0)
                continue;

            var gained = debtor.Pending;
            debtor.Pending = 0;
            var cut = WFBenchMath.Garnish(gained, share, Owed(user));
            if (cut <= 0 || SessionOf(user) is not { } session || !Take(session, cut))
                continue;

            _bank.TrySectorDeposit(_nfsd, cut, LedgerEntryType.StationDepositFines);
            Apply(user, cut);
            var owed = Owed(user);
            _chat.DispatchServerMessage(session, Loc.GetString("wf-bench-garnished", ("amount", cut), ("owed", owed)));
            if (owed <= 0)
                Escalate(user);
        }
    }

    /// <summary>Pays an amount off a player's tickets, oldest first.</summary>
    private void Apply(NetUserId user, int amount)
    {
        foreach (var ticket in _tickets.Where(t => t.User == user && !t.Pardoned && t.Owed > 0))
        {
            if (amount <= 0)
                break;

            var part = Math.Min(amount, ticket.Owed);
            ticket.Owed -= part;
            ticket.Paid += part;
            amount -= part;
        }
    }

    /// <summary>
    /// Marks overdue tickets, then sets the player's record Wanted if any ticket holds them, or clears a Wanted the
    /// Bench set once none does.
    /// </summary>
    private void Escalate(NetUserId user)
    {
        var now = _timing.CurTime;
        var cap = _cfg.GetCVar(BenchCVars.DebtCap);
        var owed = Owed(user);
        var tickets = _tickets.Where(t => t.User == user).ToList();
        foreach (var ticket in tickets)
        {
            if (!ticket.Pardoned && ticket.Owed > 0 && !ticket.Overdue && (now >= ticket.Deadline || owed > cap))
                ticket.Overdue = true;
        }

        var holding = tickets.FirstOrDefault(t => t.Wanting);
        var debtor = Debtor(user);
        if (holding != null)
        {
            if (debtor.MarkedWanted || RecordKey(holding.Offender) is not { } key)
                return;

            var reason = holding.Level == WFPenaltyLevel.Outlawry
                ? Loc.GetString("wf-bench-wanted-outlaw", ("number", holding.Number))
                : Loc.GetString("wf-bench-wanted-debt", ("owed", owed));
            _criminalRecords.TryChangeStatus(key, SecurityStatus.Wanted, reason, Loc.GetString("wf-bench-stamp"));
            debtor.MarkedWanted = true;
            if (SessionOf(user) is { } session)
                _chat.DispatchServerMessage(session, Loc.GetString("wf-bench-now-wanted"));
            return;
        }

        if (!debtor.MarkedWanted)
            return;

        debtor.MarkedWanted = false;
        if (tickets.Count > 0 && RecordKey(tickets[0].Offender) is { } clearKey)
            _criminalRecords.TryChangeStatus(clearKey, SecurityStatus.None, null, Loc.GetString("wf-bench-stamp"));
    }

    private ICommonSession? SessionOf(NetUserId user)
    {
        return _players.TryGetSessionById(user, out var session) ? session : null;
    }

    /// <summary>Withdraws from a player's bank, through their body if it carries the account so it shows at once.</summary>
    private bool Take(ICommonSession session, int amount)
    {
        if (session.AttachedEntity is { } body && HasComp<BankAccountComponent>(body))
            return _bank.TryBankWithdraw(body, amount);

        return Profile(session) is { } found
            && _bank.TryBankWithdraw(session, found.Prefs, found.Profile, amount, out _);
    }

    /// <summary>Deposits untaxed into a player's bank, as <see cref="Take"/> withdraws.</summary>
    private bool Give(ICommonSession session, int amount)
    {
        if (session.AttachedEntity is { } body && HasComp<BankAccountComponent>(body))
            return _bank.TryBankDeposit(body, amount, tax: false);

        return Profile(session) is { } found
            && _bank.TryBankDeposit(session, found.Prefs, found.Profile, amount, out _);
    }

    private (PlayerPreferences Prefs, HumanoidCharacterProfile Profile)? Profile(ICommonSession session)
    {
        if (!_prefs.TryGetCachedPreferences(session.UserId, out var prefs)
            || prefs.SelectedCharacter is not HumanoidCharacterProfile profile)
            return null;
        return (prefs, profile);
    }
}
