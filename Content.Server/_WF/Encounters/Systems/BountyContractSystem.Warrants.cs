using Content.Shared._NF.Bank;
using Content.Shared._NF.BountyContracts;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.BountyContracts;

/// <summary>
/// Contracts the sector posts itself, with no player behind them: the encounter warrants. They skip the access
/// checks players go through, grow in place and come off the board when the sector withdraws them.
/// </summary>
public sealed partial class BountyContractSystem
{
    /// <summary>The collection encounter warrants go in. Players can read it and cannot post to it or delete from it.</summary>
    public static readonly ProtoId<BountyContractCollectionPrototype> WarrantCollection = "WFWarrants";

    /// <summary>
    /// Posts a contract no player owns and sends the collection's usual notification. Null when the board is not up or
    /// the collection is unknown.
    /// </summary>
    public BountyContract? TryPostSystemContract(ProtoId<BountyContractCollectionPrototype> collection,
        BountyContractCategory category, string name, int reward, string? description = null, string? vessel = null,
        string? author = null)
    {
        var data = GetContracts();
        if (data?.Contracts == null || !data.Contracts.TryGetValue(collection, out var contracts))
            return null;

        if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];
        if (vessel != null && vessel.Length > MaxVesselLength)
            vessel = vessel[..MaxVesselLength];
        if (description != null && description.Length > MaxDescriptionLength)
            description = description[..MaxDescriptionLength];

        // The sector's own entity stands as the author: it is never a PDA, so no one can remove the entry as its poster.
        var contractId = data.LastId++;
        var contract = new BountyContract(contractId, category, name, reward, GetNetEntity(_sectorService.GetServiceEntity()),
            null, vessel, description, author);
        if (!contracts.TryAdd(contractId, contract))
        {
            _sawmill.Error($"Failed to post system contract {contractId}! LastId: {data.LastId}.");
            return null;
        }

        if (_proto.TryIndex(collection, out var bountyCollection))
            Notify(contract, bountyCollection.NotificationType);

        _adminLog.Add(LogType.BountyContractCreated, $"The sector posted a {category} bounty with ID {contractId} in the {collection} collection for ${reward}: {description ?? ""}");
        RefreshOpenLists(collection);
        return contract;
    }

    /// <summary>The collection a contract is posted in, if it is on the board.</summary>
    public ProtoId<BountyContractCollectionPrototype>? CollectionOf(uint contractId)
    {
        var data = GetContracts();
        if (data?.Contracts == null)
            return null;

        foreach (var (collectionId, collection) in data.Contracts)
        {
            if (collection.ContainsKey(contractId))
                return collectionId;
        }

        return null;
    }

    /// <summary>Changes what a contract pays, keeping its id and place and sending no notification.</summary>
    public bool TrySetContractReward(uint contractId, int reward)
    {
        var data = GetContracts();
        if (data?.Contracts == null)
            return false;

        foreach (var (collectionId, collection) in data.Contracts)
        {
            if (!collection.TryGetValue(contractId, out var old))
                continue;

            collection[contractId] = new BountyContract(old.ContractId, old.Category, old.Name, reward, old.AuthorUid,
                old.DNA, old.Vessel, old.Description, old.Author);
            RefreshOpenLists(collectionId);
            return true;
        }

        return false;
    }

    /// <summary>Takes a contract off the board, whoever posted it. False if it is not there.</summary>
    public bool TryRemoveSystemContract(uint contractId)
    {
        var data = GetContracts();
        if (data?.Contracts == null)
            return false;

        foreach (var (collectionId, collection) in data.Contracts)
        {
            if (!collection.Remove(contractId))
                continue;

            _adminLog.Add(LogType.BountyContractRemoved, $"The sector withdrew bounty with ID {contractId}");
            RefreshOpenLists(collectionId);
            return true;
        }

        return false;
    }

    /// <summary>The same notification a player's contract sends: a PDA message to those with the program, or a radio broadcast.</summary>
    private void Notify(BountyContract contract, BountyContractNotificationType notificationType)
    {
        LocId announcement = "bounty-contracts-announcement-generic-create";
        if (CategoriesMeta.TryGetValue(contract.Category, out var categoryMeta) && categoryMeta.Announcement != null)
            announcement = categoryMeta.Announcement.Value;

        var target = !string.IsNullOrEmpty(contract.Vessel) && contract.Vessel != Loc.GetString("bounty-contracts-ui-create-vessel-unknown")
            ? $"{contract.Name} ({contract.Vessel})"
            : contract.Name;
        var msg = Loc.GetString(announcement, ("target", target), ("reward", BankSystemExtensions.ToSpesoString(contract.Reward)));
        if (notificationType == BountyContractNotificationType.PDA)
        {
            var sender = Loc.GetString("bounty-contracts-announcement-pda-name");
            var loaders = EntityQueryEnumerator<CartridgeLoaderComponent>();
            while (loaders.MoveNext(out var loaderUid, out var loaderComp))
            {
                if (_cartridgeLoader.TryGetProgram<BountyContractsCartridgeComponent>(loaderUid, out _, out var cartComp, true, loaderComp)
                    && cartComp.NotificationsEnabled)
                {
                    _cartridgeLoader.SendNotification(loaderUid, sender, msg, loaderComp);
                }
            }
        }
        else if (notificationType == BountyContractNotificationType.Radio)
        {
            var sender = Loc.GetString("bounty-contracts-announcement-radio-name");
            _chat.DispatchGlobalAnnouncement(msg, sender, false, colorOverride: Color.FromHex("#D7D7BE"));
        }
    }

    /// <summary>Sends the list again to every PDA whose bounty program has that collection open.</summary>
    private void RefreshOpenLists(ProtoId<BountyContractCollectionPrototype> collection)
    {
        var cartridges = EntityQueryEnumerator<BountyContractsCartridgeComponent, CartridgeComponent>();
        while (cartridges.MoveNext(out var uid, out var program, out var cartridge))
        {
            if (program.Collection != collection || cartridge.LoaderUid is not { } loader
                || !TryComp<CartridgeLoaderComponent>(loader, out var loaderComp) || loaderComp.ActiveProgram != uid)
                continue;

            CartridgeRefreshListUi((uid, program), loader);
        }
    }
}
