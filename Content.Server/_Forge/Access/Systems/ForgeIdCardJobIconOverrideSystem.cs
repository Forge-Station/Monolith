using Content.Server.Access.Components;
using Content.Server.Access.Systems;
using Content.Server.GameTicking;
using Content.Shared._Forge.Access.Components;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Access.Systems;

/// <summary>
/// Applies visual-only job icon overrides after preset ID cards are configured.
/// </summary>
public sealed class ForgeIdCardJobIconOverrideSystem : EntitySystem
{
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ForgeIdCardJobIconOverrideComponent, MapInitEvent>(
            OnMapInit,
            after: [typeof(PresetIdCardComponent)]);

        SubscribeLocalEvent<ForgeIdCardJobIconOverrideComponent, EntInsertedIntoContainerMessage>(
            OnInsertedIntoContainer);

        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(
            OnJobsAssigned,
            after: [typeof(PresetIdCardSystem)]);

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);

        SubscribeLocalEvent<InventoryComponent, StartingGearEquippedEvent>(OnStartingGearEquipped);
    }

    private void OnMapInit(EntityUid uid, ForgeIdCardJobIconOverrideComponent comp, MapInitEvent args)
    {
        ApplyOverride(uid, comp);
    }

    private void OnInsertedIntoContainer(
        EntityUid uid,
        ForgeIdCardJobIconOverrideComponent comp,
        EntInsertedIntoContainerMessage args)
    {
        ApplyOverride(uid, comp);
    }

    private void OnJobsAssigned(RulePlayerJobsAssignedEvent args)
    {
        ReapplyAllOverrides();
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        Timer.Spawn(0, () => TryApplyOverrideForPlayer(args.Mob));
    }

    private void OnStartingGearEquipped(
        EntityUid uid,
        InventoryComponent component,
        ref StartingGearEquippedEvent args)
    {
        // SetPdaAndIdCardData runs synchronously after StartingGearEquippedEvent and resets the icon from the mind job.
        Timer.Spawn(0, () => TryApplyOverrideForPlayer(uid));
    }

    private void ReapplyAllOverrides()
    {
        var query = EntityQueryEnumerator<ForgeIdCardJobIconOverrideComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            ApplyOverride(uid, comp);
        }
    }

    private void TryApplyOverrideForPlayer(EntityUid player)
    {
        if (!Exists(player))
            return;

        if (!_inventory.TryGetSlotEntity(player, "id", out var idUid))
            return;

        var cardId = idUid.Value;
        if (TryComp<PdaComponent>(idUid, out var pda) && pda.ContainedId != null)
            cardId = pda.ContainedId.Value;

        if (TryComp<ForgeIdCardJobIconOverrideComponent>(cardId, out var comp))
            ApplyOverride(cardId, comp);
    }

    private void ApplyOverride(EntityUid uid, ForgeIdCardJobIconOverrideComponent comp)
    {
        if (!_prototypes.TryIndex(comp.JobIcon, out JobIconPrototype? icon))
            return;

        _idCard.TryChangeJobIcon(uid, icon);
    }
}
