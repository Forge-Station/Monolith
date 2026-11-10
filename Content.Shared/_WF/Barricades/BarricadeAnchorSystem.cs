using Content.Shared.Damage;
using Content.Shared.Doors;
using Content.Shared.FixedPoint;

namespace Content.Shared._WF.Barricades;

/// <summary>Unanchored barricades take extra damage, and folding barricade doors only close while anchored.</summary>
public sealed class BarricadeAnchorSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<DamageReceivedModifierWhenUnanchoredComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<RMCOpenOnlyWhenUnanchoredComponent, BeforeDoorClosedEvent>(OnBeforeDoorClosed);
    }

    private void OnDamageModify(Entity<DamageReceivedModifierWhenUnanchoredComponent> ent, ref DamageModifyEvent args)
    {
        if (Transform(ent).Anchored || args.Damage.GetTotal() <= FixedPoint2.Zero)
            return;

        args.Damage *= ent.Comp.Multiplier;
    }

    private void OnBeforeDoorClosed(Entity<RMCOpenOnlyWhenUnanchoredComponent> ent, ref BeforeDoorClosedEvent args)
    {
        if (!Transform(ent).Anchored)
            args.Cancel();
    }
}
