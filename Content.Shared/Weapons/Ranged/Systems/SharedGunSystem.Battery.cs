using Content.Shared.Examine;
using Content.Shared.Projectiles;
using Content.Shared.Power;
using Content.Shared.Damage;
using Content.Shared.PowerCell;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Maths; // Forge-Change
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    protected virtual void InitializeBattery()
    {
        SubscribeLocalEvent<BatteryAmmoProviderComponent, ComponentStartup>(OnBatteryStartup);
        SubscribeLocalEvent<BatteryAmmoProviderComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
        SubscribeLocalEvent<BatteryAmmoProviderComponent, TakeAmmoEvent>(OnBatteryTakeAmmo);
        SubscribeLocalEvent<BatteryAmmoProviderComponent, GetAmmoCountEvent>(OnBatteryAmmoCount);
        SubscribeLocalEvent<BatteryAmmoProviderComponent, ExaminedEvent>(OnBatteryExamine);
        //SubscribeLocalEvent<BatteryAmmoProviderComponent, DamageExamineEvent>(OnBatteryDamageExamine);
        SubscribeLocalEvent<BatteryAmmoProviderComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<BatteryAmmoProviderComponent, PredictedBatteryChargeChangedEvent>(OnPredictedChargeChanged);
        SubscribeLocalEvent<BatteryAmmoProviderComponent, ChargeChangedEvent>(OnChargeChanged);

        InitializeSelectiveFireBattery(); // Forge-Change
    }

    private void OnBatteryExamine(Entity<BatteryAmmoProviderComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("gun-battery-examine", ("color", AmmoExamineColor), ("count", ent.Comp.Shots)));
    }

    // private void OnBatteryDamageExamine(Entity<BatteryAmmoProviderComponent> ent, ref DamageExamineEvent args)
    // {
    //     var proto = ProtoManager.Index<EntityPrototype>(ent.Comp.Prototype);
    //     DamageSpecifier? damageSpec = null;
    //     var damageType = string.Empty;

    //     if (proto.TryGetComponent<ProjectileComponent>(out var projectileComp, Factory))
    //     {
    //         if (!projectileComp.Damage.Empty)
    //         {
    //             damageType = Loc.GetString("damage-projectile");
    //             damageSpec = projectileComp.Damage * Damageable.UniversalProjectileDamageModifier;
    //         }
    //     }
    //     else if (proto.TryGetComponent<HitscanBasicDamageComponent>(out var hitscanComp, Factory))
    //     {
    //         if (!hitscanComp.Damage.Empty)
    //         {
    //             damageType = Loc.GetString("damage-hitscan");
    //             damageSpec = hitscanComp.Damage * Damageable.UniversalHitscanDamageModifier;
    //         }
    //     }

    //     if (damageSpec == null)
    //         return;

    //     _damageExamine.AddDamageExamine(
    //         args.Message,
    //         Damageable.ApplyUniversalAllModifiers(damageSpec),
    //         damageType);
    // }

    private void OnBatteryTakeAmmo(Entity<BatteryAmmoProviderComponent> ent, ref TakeAmmoEvent args)
    {
        var shots = Math.Min(args.Shots, ent.Comp.Shots);

        // Don't dirty if it's an empty fire.
        if (shots == 0)
            return;

        for (var i = 0; i < shots; i++)
        {
            args.Ammo.Add(GetShootable(ent, args.Coordinates));
        }

        TakeCharge(ent, shots);
    }

    // Mono
    private void OnBatteryCheckProto(EntityUid uid, BatteryAmmoProviderComponent comp, ref CheckShootPrototypeEvent args)
    {
        if (ProtoManager.TryIndex(comp.Prototype, out var proto))
            args.ShootPrototype = proto;
    }

    private void OnBatteryAmmoCount(Entity<BatteryAmmoProviderComponent> ent, ref GetAmmoCountEvent args)
    {
        args.Count = ent.Comp.Shots;
        args.Capacity = ent.Comp.Capacity;
    }

    /// <summary>
    /// Use up the required amount of battery charge for firing.
    /// </summary>
    public void TakeCharge(Entity<BatteryAmmoProviderComponent> ent, int shots = 1)
    {
        // Take charge from either the BatteryComponent, PredictedBatteryComponent or PowerCellSlotComponent.
        var ev = new ChangeChargeEvent(-ent.Comp.FireCost * shots);
        RaiseLocalEvent(ent, ref ev);
        // UpdateShots is already called by the resulting PredictedBatteryChargeChangedEvent or ChargeChangedEvent
    }

    private (EntityUid? Entity, IShootable) GetShootable(BatteryAmmoProviderComponent component, EntityCoordinates coordinates)
    {
        var ent = Spawn(component.Prototype, coordinates);
        return (ent, EnsureShootable(ent));
    }

    public void UpdateShots(Entity<BatteryAmmoProviderComponent> ent)
    {
        var oldShots = ent.Comp.Shots;
        var oldCapacity = ent.Comp.Capacity;
        (var newShots, var newCapacity) = GetShots(ent);

        // Only dirty if necessary.
        if (oldShots == newShots && oldCapacity == newCapacity)
            return;

        ent.Comp.Shots = newShots;
        if (newCapacity > 0) // Don't make the capacity 0 when removing a power cell as this will make it be visualized as full instead of empty.
            ent.Comp.Capacity = newCapacity;

        // Update the ammo counter predictively if the change was predicted. On the server this does nothing.
        UpdateAmmoCount(ent.Owner);

        Dirty(ent); // Dirtying will update the client's ammo counter in an AfterAutoHandleStateEvent subscription in case it was not predicted.

        if (!TryComp<AppearanceComponent>(ent, out var appearance))
            return;

        // Update the visuals.
        Appearance.SetData(ent.Owner, AmmoVisuals.HasAmmo, newShots != 0, appearance);
        Appearance.SetData(ent.Owner, AmmoVisuals.AmmoCount, newShots, appearance);
        if (newCapacity > 0) // Don't make the capacity 0 when removing a power cell as this will make it be visualized as full instead of empty.
            Appearance.SetData(ent.Owner, AmmoVisuals.AmmoMax, newCapacity, appearance);
    }

    // For server side changes the client's ammo counter needs to be updated as well.
    private void OnAfterAutoHandleState(Entity<BatteryAmmoProviderComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateAmmoCount(ent); // Need to have prediction set to true because the state is applied repeatedly while prediction is running.
    }

    // For when a power cell gets inserted or removed.
    private void OnPowerCellChanged(Entity<BatteryAmmoProviderComponent> ent, ref PowerCellChangedEvent args)
    {
        UpdateShots(ent);
    }

    // For predicted batteries.
    // If the entity has a PowerCellSlotComponent then this event is relayed from the power cell to the slot entity.
    private void OnPredictedChargeChanged(Entity<BatteryAmmoProviderComponent> ent, ref PredictedBatteryChargeChangedEvent args)
    {
        // Update the visuals and charge counter UI.
        UpdateShots(ent);
        // Queue the update for when the autorecharge reaches enough charge for another shot.
        UpdateNextUpdate(ent, args.CurrentCharge, args.MaxCharge, args.CurrentChargeRate);
    }

    // For unpredicted batteries.
    private void OnChargeChanged(Entity<BatteryAmmoProviderComponent> ent, ref ChargeChangedEvent args)
    {
        // Update the visuals and charge counter UI.
        UpdateShots(ent);
        // No need to queue an update here since unpredicted batteries already update periodically as they charge/discharge.
    }

    private void UpdateNextUpdate(Entity<BatteryAmmoProviderComponent> ent, float currentCharge, float maxCharge, float currentChargeRate)
    {
        // Don't queue any updates if charge is constant.
        ent.Comp.NextUpdate = null;

        // ETA of the next full charge.
        if (currentChargeRate > 0f && currentCharge != maxCharge)
        {
            ent.Comp.NextUpdate = Timing.CurTime + TimeSpan.FromSeconds(
                (ent.Comp.FireCost - (currentCharge % ent.Comp.FireCost)) / currentChargeRate);
            ent.Comp.ChargeTime = TimeSpan.FromSeconds(ent.Comp.FireCost / currentChargeRate);
        }
        else if (currentChargeRate < 0f && currentCharge != 0f)
        {
            ent.Comp.NextUpdate = Timing.CurTime + TimeSpan.FromSeconds(
                -(currentCharge % ent.Comp.FireCost) / currentChargeRate);
            ent.Comp.ChargeTime = TimeSpan.FromSeconds(-ent.Comp.FireCost / currentChargeRate);
        }

        Dirty(ent);
    }

    // Shots are only cached, not a DataField, so we need to refresh this when the game is loaded.
    private void OnBatteryStartup(Entity<BatteryAmmoProviderComponent> ent, ref ComponentStartup args)
    {
        UpdateShots(ent);
    }

    /// <summary>
    /// Gets the current and maximum amount of shots from this entity's battery.
    /// This works for BatteryComponent, PredictedBatteryComponent and PowercellSlotComponent.
    /// </summary>
    public (int, int) GetShots(Entity<BatteryAmmoProviderComponent> ent)
    {
        var ev = new GetChargeEvent();
        RaiseLocalEvent(ent, ref ev);
        var currentShots = (int)(ev.CurrentCharge / ent.Comp.FireCost);
        var maxShots = (int)(ev.MaxCharge / ent.Comp.FireCost);

        return (currentShots, maxShots);
    }

    /// <summary>
    /// Update loop for refreshing the ammo counter for charging/draining predicted batteries.
    /// This is not needed for unpredicted batteries since those already raise ChargeChangedEvent periodically.
    /// </summary>
    private void UpdateBattery(float frameTime)
    {
        var curTime = Timing.CurTime;
        var batteryQuery = EntityQueryEnumerator<BatteryAmmoProviderComponent>();
        while (batteryQuery.MoveNext(out var uid, out var provider))
        {
            if (provider.NextUpdate == null || curTime < provider.NextUpdate)
                continue;

            UpdateShots((uid, provider));
            provider.NextUpdate += provider.ChargeTime; // Queue another update for when we reach the next full charge.
            Dirty(uid, provider);
            // TODO: Stop updating when full or empty.
        }
    }

    // Forge-Change-start: per-mode battery settings for guns using selective fire.
    protected virtual void InitializeSelectiveFireBattery()
    {
        SubscribeLocalEvent<SelectiveFireBatteryComponent, MapInitEvent>(OnSelectiveFireBatteryMapInit);
        SubscribeLocalEvent<SelectiveFireBatteryComponent, AttemptShootEvent>(OnSelectiveFireBatteryAttemptShoot);
        SubscribeLocalEvent<SelectiveFireBatteryComponent, GunRefreshModifiersEvent>(OnSelectiveFireBatteryRefreshModifiers);
        SubscribeLocalEvent<SelectiveFireBatteryComponent, CheckShootPrototypeEvent>(OnSelectiveFireBatteryCheckProto);
    }

    private void OnSelectiveFireBatteryMapInit(EntityUid uid, SelectiveFireBatteryComponent comp, MapInitEvent args)
    {
        SyncSelectiveFireBatteryAmmoProvider(uid, comp);
    }

    private void OnSelectiveFireBatteryAttemptShoot(EntityUid uid, SelectiveFireBatteryComponent comp, ref AttemptShootEvent args)
    {
        SyncSelectiveFireBatteryAmmoProvider(uid, comp);
        RefreshModifiers(uid, args.User);
    }

    private void OnSelectiveFireBatteryRefreshModifiers(EntityUid uid, SelectiveFireBatteryComponent comp, ref GunRefreshModifiersEvent args)
    {
        SyncSelectiveFireBatteryAmmoProvider(uid, comp);

        if (!TryComp<GunComponent>(uid, out var gun))
            return;

        var mode = GetSelectiveFireBatteryMode(comp, gun.SelectedMode);
        if (mode == null)
            return;

        var spreadChanged = false;

        if (mode.MinAngle != null)
        {
            args.MinAngle = mode.MinAngle.Value;
            spreadChanged = true;
        }

        if (mode.MaxAngle != null)
        {
            args.MaxAngle = mode.MaxAngle.Value;
            spreadChanged = true;
        }

        if (mode.FireRate != null)
            args.FireRate = mode.FireRate.Value;

        if (spreadChanged)
            gun.CurrentAngle = Angle.Zero;
    }

    private void OnSelectiveFireBatteryCheckProto(EntityUid uid, SelectiveFireBatteryComponent comp, ref CheckShootPrototypeEvent args)
    {
        if (!TryComp<GunComponent>(uid, out var gun))
            return;

        var mode = GetSelectiveFireBatteryMode(comp, gun.SelectedMode);
        if (mode == null || !ProtoManager.TryIndex(mode.Proto, out var proto))
            return;

        args.ShootPrototype = proto;
    }

    private void SyncSelectiveFireBatteryAmmoProvider(EntityUid uid, SelectiveFireBatteryComponent comp)
    {
        if (!TryComp<GunComponent>(uid, out var gun))
            return;

        if (!TryComp<BatteryAmmoProviderComponent>(uid, out var ammo))
            return;

        var mode = GetSelectiveFireBatteryMode(comp, gun.SelectedMode);
        if (mode == null)
            return;

        if (ammo.Prototype == mode.Proto && MathHelper.CloseTo(ammo.FireCost, mode.FireCost))
            return;

        var oldFireCost = ammo.FireCost;
        ammo.Prototype = mode.Proto;
        ammo.FireCost = mode.FireCost;

        if (!MathHelper.CloseTo(oldFireCost, 0) && !MathHelper.CloseTo(oldFireCost, mode.FireCost))
        {
            var fireCostDiff = mode.FireCost / oldFireCost;
            ammo.Shots = (int)Math.Round(ammo.Shots / fireCostDiff);
            ammo.Capacity = (int)Math.Round(ammo.Capacity / fireCostDiff);
        }

        Dirty(uid, ammo);
    }

    private static SelectiveFireBatteryMode? GetSelectiveFireBatteryMode(SelectiveFireBatteryComponent comp, SelectiveFire mode)
    {
        foreach (var entry in comp.Modes)
        {
            if (entry.Mode == mode)
                return entry;
        }

        return comp.Modes.Count > 0 ? comp.Modes[0] : null;
    }

    // Forge-Change-end
}
