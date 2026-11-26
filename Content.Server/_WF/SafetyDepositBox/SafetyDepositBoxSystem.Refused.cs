using Content.Shared.Database;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.SafetyDepositBox;

// Wolfgate's handling of stored items a box no longer takes, kept out of the upstream file.
public sealed partial class SafetyDepositBoxSystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    /// <summary>
    /// Takes out anything the box refuses that is tucked inside a stored item, such as a cell in a device's slot,
    /// and hands it back. Returns how many came out.
    /// </summary>
    private int EjectNestedRefused(EntityUid player, EntityUid boxEntity, StorageComponent storage)
    {
        var refused = new List<EntityUid>();
        foreach (var item in storage.Container.ContainedEntities)
            FindNestedRefused(item, storage, refused);

        foreach (var nested in refused)
        {
            _container.TryRemoveFromContainer(nested, force: true);
            _hands.PickupOrDrop(player, nested);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(player):actor} got {ToPrettyString(nested):entity} back from inside an item in safety deposit box {ToPrettyString(boxEntity)}");
        }

        return refused.Count;
    }

    private void FindNestedRefused(EntityUid parent, StorageComponent storage, List<EntityUid> refused)
    {
        if (!TryComp<ContainerManagerComponent>(parent, out var manager))
            return;

        foreach (var container in _container.GetAllContainers(parent, manager))
        {
            foreach (var nested in container.ContainedEntities)
            {
                // A refused item comes out whole, with whatever is inside it.
                if (_whitelist.IsBlacklistPass(storage.Blacklist, nested))
                    refused.Add(nested);
                else
                    FindNestedRefused(nested, storage, refused);
            }
        }
    }

    /// <summary>
    /// Puts a withdrawn item its box refuses down at the box, so it isn't lost.
    /// </summary>
    private void DropRefusedItem(EntityUid player, Guid boxId, EntityUid boxEntity, EntityUid itemEntity)
    {
        _transform.DropNextTo(itemEntity, boxEntity);

        // Saved inside the box, it loads with collision off; wake it as leaving a container does.
        if (TryComp<PhysicsComponent>(itemEntity, out var body))
            _physics.WakeBody(itemEntity, body: body);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):actor} got {ToPrettyString(itemEntity):entity} back beside safety deposit box {boxId}, which no longer accepts it");
    }

    /// <summary>
    /// The popup for a finished withdrawal, which says how many items were left at the box.
    /// </summary>
    private string WithdrawPopup(int refused)
    {
        return refused == 0
            ? Loc.GetString("safety-deposit-console-withdraw-success")
            : Loc.GetString("safety-deposit-console-withdraw-refused", ("count", refused));
    }
}
