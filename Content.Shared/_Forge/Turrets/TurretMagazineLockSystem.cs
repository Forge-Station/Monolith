using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Forge.Turrets;

/// <summary>
/// A closed turret keeps its magazine. An empty turret can still be loaded.
/// </summary>
public sealed class TurretMagazineLockSystem : EntitySystem
{
    private const string MagazineSlot = "gun_magazine";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TurretCommandLinkComponent, ItemSlotEjectAttemptEvent>(OnEjectAttempt);
    }

    private void OnEjectAttempt(Entity<TurretCommandLinkComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if (!ent.Comp.Managed || !ent.Comp.MagazineLocked)
            return;

        if (args.Slot.ContainerSlot?.ID != MagazineSlot)
            return;

        args = args with { Cancelled = true };
    }
}
