using Content.Shared._Forge.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Trade;

internal interface IStoreCurrencyService
{
    bool TryGetBalance(EntityUid user, in NcInventorySnapshot snapshot, string currencyId, out int balance);

    bool TryPickCurrencyForBuy(
        EntityUid user,
        NcStoreComponent store,
        NcStoreListingDef listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance
    );

    bool TryPickCurrencyForSell(
        NcStoreComponent store,
        NcStoreListingDef listing,
        out string currency,
        out int unitPrice
    );

    bool CanHandleCurrency(string currencyId);
    bool IsVirtualCurrency(string currencyId);
    bool CanGiveCurrency(EntityUid user, string currencyId, int amount);
    bool TryTakeCurrency(EntityUid user, string currencyId, int amount);
    bool TryGiveCurrency(EntityUid user, string currencyId, int amount);
    bool BeginCurrencyIssueTransaction();
    void CommitCurrencyIssueTransaction(EntityUid user);
    void RollbackCurrencyIssueTransaction(EntityUid user);
    bool BeginCurrencyDebitTransaction();
    bool CommitCurrencyDebitTransaction(EntityUid user);
    void RollbackCurrencyDebitTransaction(EntityUid user);
}

internal interface IStoreCurrencyDebitService
{
    bool TryTakeCurrency(EntityUid user, string currencyId, int amount);
}

internal interface IStoreRewardSpawner
{
    int SpawnRewardProduct(EntityUid user, string productEntity, EntityPrototype productProto, int units);

    int SpawnPurchasedProduct(
        EntityUid user,
        string productEntity,
        EntityPrototype productProto,
        int purchases,
        int unitsPerPurchase
    );

    bool BeginRewardTransaction();
    void CommitRewardTransaction(EntityUid user);
    void RollbackRewardTransaction();
}

internal interface IStoreRewardExecutionService
{
    bool TryValidateRewardList(
        EntityUid receiver,
        IReadOnlyList<ContractRewardData>? rewards,
        out string reason
    );

    bool TryExecuteRewardList(
        EntityUid receiver,
        IReadOnlyList<ContractRewardData>? rewards,
        string context,
        out string reason
    );

    bool TryExecuteRewardListWithPreCommit(
        EntityUid receiver,
        IReadOnlyList<ContractRewardData>? rewards,
        string context,
        Func<string?> preCommit,
        out string reason
    );
}

internal interface IStoreTransactionCoordinator
{
    List<ContractRewardData> BuildSingleReward(StoreRewardType type, string id, int amount);
    List<ContractRewardData> BuildCurrencyRewards(IReadOnlyDictionary<string, int> incomeByCurrency);
    string? TryCommitInventoryTake(string context, EntityUid root, Func<string?> takeAction);
}
