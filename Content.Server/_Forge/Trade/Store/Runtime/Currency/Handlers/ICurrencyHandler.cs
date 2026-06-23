namespace Content.Server._Forge.Trade;

/// <summary>
///     Currency handler abstraction.
///     A currency id is an opaque string used by listings and presets.
///     Implementations (stack items, virtual balances, bank accounts, etc.) are resolved via <see cref="CanHandle" />.
/// </summary>
public interface ICurrencyHandler
{
    /// <summary>
    ///     Returns true if this handler can operate on the provided currency id.
    /// </summary>
    bool CanHandle(string currencyId);

    /// <summary>
    ///     Attempts to extract a balance for this currency using an inventory snapshot.
    ///     For non-inventory currencies (virtual/bank), implementations may ignore the snapshot and query components.
    /// </summary>
    bool TryGetBalance(EntityUid user, in NcInventorySnapshot snapshot, string currencyId, out int balance);

    /// <summary>
    ///     Returns true for currencies that are not represented by inventory entities.
    /// </summary>
    bool IsVirtualCurrency(string currencyId);

    /// <summary>
    ///     Attempts to take a positive amount of currency from the user.
    ///     Implementations must be atomic: a false return must not leave a partial charge behind.
    /// </summary>
    bool TryTake(EntityUid user, string currencyId, int amount);

    /// <summary>
    ///     Gives a positive amount of currency to the user.
    ///     Implementations must be atomic: a false return must not leave a partial payout behind.
    /// </summary>
    bool TryGiveCurrency(EntityUid user, string currencyId, int amount);

    /// <summary>
    ///     Opens a short-lived issue transaction. While active, spawned currency may be staged until commit.
    /// </summary>
    bool BeginCurrencyIssueTransaction();

    /// <summary>
    ///     Commits staged currency payout side effects for the receiver.
    /// </summary>
    void CommitCurrencyIssueTransaction(EntityUid user);

    /// <summary>
    ///     Rolls back staged currency payout side effects for the receiver.
    /// </summary>
    void RollbackCurrencyIssueTransaction(EntityUid user);

    /// <summary>
    ///     Opens a short-lived debit transaction. While active, implementations should stage
    ///     destructive currency takes until commit when possible.
    /// </summary>
    bool BeginCurrencyDebitTransaction();

    /// <summary>
    ///     Checks that all staged currency debits can still be finalized.
    /// </summary>
    bool PrepareCurrencyDebitTransaction(EntityUid user);

    /// <summary>
    ///     Finalizes staged currency debit side effects after every handler prepared successfully.
    /// </summary>
    bool CommitCurrencyDebitTransaction(EntityUid user);

    /// <summary>
    ///     Rolls back staged currency debit side effects for the payer.
    /// </summary>
    void RollbackCurrencyDebitTransaction(EntityUid user);

    /// <summary>
    ///     Returns true when a later <see cref="TryGiveCurrency" /> call is expected to be able to pay this receiver.
    ///     This is used before destructive sell/claim operations so unsupported currencies fail before items are removed.
    /// </summary>
    bool CanGiveCurrency(EntityUid user, string currencyId, int amount);
}
