using Content.Server.Inventory;
using Content.Shared.EntityEffects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Popups;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class TransmutinMutateMonkey : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<SpeciesPrototype> Species;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;

        if (!entMan.TryGetComponent<HumanoidAppearanceComponent>(args.TargetEntity, out var humanoid) ||
            humanoid.Species != "Monkey")
            return;

        var proto = IoCManager.Resolve<IPrototypeManager>();
        if (!proto.TryIndex(Species, out var species))
            return;

        var transform = entMan.System<SharedTransformSystem>();
        var coords = transform.GetMapCoordinates(args.TargetEntity);
        var rotation = transform.GetWorldRotation(args.TargetEntity);
        var mutated = entMan.SpawnEntity(species.Prototype, coords);
        transform.SetWorldRotation(mutated, rotation);

        var popup = entMan.System<SharedPopupSystem>();
        popup.PopupEntity(Loc.GetString("transmutin-effect-popup"), mutated, PopupType.Medium);

        DropInventoryAndHands(args.TargetEntity, entMan);
        entMan.QueueDeleteEntity(args.TargetEntity);
    }

    private static void DropInventoryAndHands(EntityUid target, IEntityManager entMan)
    {
        var inventory = entMan.System<ServerInventorySystem>();
        if (inventory.TryGetContainerSlotEnumerator(target, out var enumerator))
        {
            while (enumerator.MoveNext(out var slot))
            {
                inventory.TryUnequip(target, slot.ID, silent: true, force: true);
            }
        }

        var hands = entMan.System<SharedHandsSystem>();
        foreach (var held in hands.EnumerateHeld(target))
        {
            hands.TryDrop(target, held, checkActionBlocker: false);
        }
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("reagent-effect-guidebook-transmutin-mutate-monkey");
    }
}
