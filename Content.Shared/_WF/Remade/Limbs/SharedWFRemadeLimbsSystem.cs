using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._WF.Remade.Limbs;

/// <summary>Reads what a mob's remaining limbs give it. Cutting a limb off is the server's job.</summary>
public abstract class SharedWFRemadeLimbsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeLimbsComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<WFRemadeLimbsComponent, GetMeleeDamageEvent>(OnMeleeDamage);
    }

    private void OnSpeed(Entity<WFRemadeLimbsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(Stat(ent.Owner, WFRemadeLimbStat.Speed, ent.Comp));
    }

    private void OnMeleeDamage(Entity<WFRemadeLimbsComponent> ent, ref GetMeleeDamageEvent args)
    {
        args.Damage = args.Damage * Stat(ent.Owner, WFRemadeLimbStat.Melee, ent.Comp);
    }

    /// <summary>The stat's value: 1 with every limb on, less as limbs come off. 1 for a mob with no limbs or no limb giving it.</summary>
    public float Stat(EntityUid uid, WFRemadeLimbStat stat, WFRemadeLimbsComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return 1f;

        var any = false;
        var total = 0f;
        foreach (var limb in comp.Limbs)
        {
            if (!limb.Provides.TryGetValue(stat, out var share))
                continue;

            any = true;
            if (!comp.Severed.Contains(limb.Id))
                total += share;
        }

        if (!any)
            return 1f;

        var floor = comp.Floors.GetValueOrDefault(stat);
        return Math.Clamp(Math.Max(total, floor), 0f, 1f);
    }

    /// <summary>Whether it can still do what the stat is for. A mob with no limbs always can.</summary>
    public bool Can(EntityUid uid, WFRemadeLimbStat stat, WFRemadeLimbsComponent? comp = null)
    {
        return Stat(uid, stat, comp) > 0f;
    }

    public bool IsSevered(EntityUid uid, string limb, WFRemadeLimbsComponent? comp = null)
    {
        return Resolve(uid, ref comp, false) && comp.Severed.Contains(limb);
    }
}
