using Content.Server._NF.Bank;
using Content.Shared._Mono.CCVar;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Configuration;

namespace Content.Server._Forge.Trade;

/// <summary>
///     Virtual store currency backed by a player's Frontier bank account.
/// </summary>
public sealed class BankCurrencyHandler : ICurrencyHandler
{
    public const string CurrencyId = "BankCredit";

    private static ISawmill Sawmill => Logger.GetSawmill("ncstore-logic");
    private readonly BankSystem _bank;
    private readonly IConfigurationManager _cfg;
    private readonly IEntityManager _ents;
    private readonly Dictionary<EntityUid, int> _pendingDebit = new();
    private readonly Dictionary<EntityUid, int> _pendingIssue = new();
    private bool _currencyDebitTransactionActive;
    private bool _currencyIssueTransactionActive;

    public BankCurrencyHandler(BankSystem bank, IEntityManager ents, IConfigurationManager cfg)
    {
        _bank = bank;
        _ents = ents;
        _cfg = cfg;
    }

    public bool CanHandle(string currencyId)
    {
        return string.Equals(currencyId, CurrencyId, StringComparison.Ordinal);
    }

    public bool IsVirtualCurrency(string currencyId)
    {
        return CanHandle(currencyId);
    }

    public bool TryGetBalance(EntityUid user, in NcInventorySnapshot snapshot, string currencyId, out int balance)
    {
        balance = 0;
        if (!CanHandle(currencyId))
            return false;

        if (!_bank.TryGetBalance(user, out balance))
            return false;

        if (_currencyDebitTransactionActive)
            balance = Math.Max(0, balance - GetPendingDebit(user));

        return true;
    }

    public bool TryTake(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;
        if (!CanHandle(currencyId))
            return false;

        if (!_currencyDebitTransactionActive)
            return _bank.TryBankWithdraw(user, amount);

        if (!_bank.TryGetBalance(user, out var balance))
            return false;

        var pending = GetPendingDebit(user);
        var total = (long)pending + amount;
        if (total <= 0 || total > int.MaxValue || balance < total)
            return false;

        _pendingDebit[user] = (int)total;
        return true;
    }

    public bool TryGiveCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;
        if (!CanHandle(currencyId) || !CanGiveCurrency(user, currencyId, amount))
            return false;

        if (!_currencyIssueTransactionActive)
            return _bank.TryBankDeposit(user, amount);

        var pending = GetPendingIssue(user);
        var total = (long)pending + amount;
        if (total <= 0 || total > int.MaxValue)
            return false;

        _pendingIssue[user] = (int)total;
        return true;
    }

    public bool BeginCurrencyIssueTransaction()
    {
        if (_currencyIssueTransactionActive)
            return false;

        _pendingIssue.Clear();
        _currencyIssueTransactionActive = true;
        return true;
    }

    public void CommitCurrencyIssueTransaction(EntityUid user)
    {
        if (!_currencyIssueTransactionActive)
            return;

        foreach (var (target, amount) in _pendingIssue)
        {
            if (amount <= 0)
                continue;

            if (!_bank.TryBankDeposit(target, amount))
            {
                Sawmill.Error(
                    $"[NcStore] Failed to commit bank currency payout '{CurrencyId}' x{amount} to {target}.");
            }
        }

        _pendingIssue.Clear();
        _currencyIssueTransactionActive = false;
    }

    public void RollbackCurrencyIssueTransaction(EntityUid user)
    {
        if (!_currencyIssueTransactionActive)
            return;

        _pendingIssue.Clear();
        _currencyIssueTransactionActive = false;
    }

    public bool BeginCurrencyDebitTransaction()
    {
        if (_currencyDebitTransactionActive)
            return false;

        _pendingDebit.Clear();
        _currencyDebitTransactionActive = true;
        return true;
    }

    public bool CommitCurrencyDebitTransaction(EntityUid user)
    {
        if (!_currencyDebitTransactionActive)
            return true;

        if (!PrepareCurrencyDebitTransaction(user))
            return false;

        foreach (var (target, amount) in _pendingDebit)
        {
            if (amount <= 0)
                continue;

            if (_bank.TryBankWithdraw(target, amount))
                continue;

            Sawmill.Error(
                $"[NcStore] Failed to commit bank currency debit '{CurrencyId}' x{amount} from {target}.");
            _pendingDebit.Clear();
            _currencyDebitTransactionActive = false;
            return false;
        }

        _pendingDebit.Clear();
        _currencyDebitTransactionActive = false;
        return true;
    }

    public bool PrepareCurrencyDebitTransaction(EntityUid user)
    {
        if (!_currencyDebitTransactionActive)
            return true;

        foreach (var (target, amount) in _pendingDebit)
        {
            if (amount <= 0)
                continue;

            if (_bank.TryGetBalance(target, out var balance) && balance >= amount)
                continue;

            Sawmill.Error(
                $"[NcStore] Failed to prepare bank currency debit '{CurrencyId}' x{amount} from {target}.");
            return false;
        }

        return true;
    }

    public void RollbackCurrencyDebitTransaction(EntityUid user)
    {
        if (!_currencyDebitTransactionActive)
            return;

        _pendingDebit.Clear();
        _currencyDebitTransactionActive = false;
    }

    public bool CanGiveCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;
        if (!CanHandle(currencyId))
            return false;

        if (!_cfg.GetCVar(MonoCVars.DepositEnabled))
            return false;

        if (!_ents.EntityExists(user) ||
            !_ents.HasComponent<BankAccountComponent>(user) ||
            !_bank.TryGetBalance(user, out var balance))
            return false;

        var total = (long)balance + GetPendingIssue(user) + amount;
        return total <= int.MaxValue;
    }

    private int GetPendingIssue(EntityUid user)
    {
        if (!_currencyIssueTransactionActive)
            return 0;

        return _pendingIssue.GetValueOrDefault(user);
    }

    private int GetPendingDebit(EntityUid user)
    {
        if (!_currencyDebitTransactionActive)
            return 0;

        return _pendingDebit.GetValueOrDefault(user);
    }
}
