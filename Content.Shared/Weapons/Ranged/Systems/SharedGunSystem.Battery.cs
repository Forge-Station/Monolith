using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths; // Forge-Change
using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    protected virtual void InitializeBattery()
    {
        // Trying to dump comp references hence the below
        // Hitscan
        SubscribeLocalEvent<HitscanBatteryAmmoProviderComponent, ComponentGetState>(OnBatteryGetState);
        SubscribeLocalEvent<HitscanBatteryAmmoProviderComponent, ComponentHandleState>(OnBatteryHandleState);
        SubscribeLocalEvent<HitscanBatteryAmmoProviderComponent, TakeAmmoEvent>(OnBatteryTakeAmmo);
        SubscribeLocalEvent<HitscanBatteryAmmoProviderComponent, CheckShootPrototypeEvent>(OnBatteryCheckProto); // Mono
        SubscribeLocalEvent<HitscanBatteryAmmoProviderComponent, GetAmmoCountEvent>(OnBatteryAmmoCount);
        SubscribeLocalEvent<HitscanBatteryAmmoProviderComponent, ExaminedEvent>(OnBatteryExamine);

        // Projectile
        SubscribeLocalEvent<ProjectileBatteryAmmoProviderComponent, ComponentGetState>(OnBatteryGetState);
        SubscribeLocalEvent<ProjectileBatteryAmmoProviderComponent, ComponentHandleState>(OnBatteryHandleState);
        SubscribeLocalEvent<ProjectileBatteryAmmoProviderComponent, TakeAmmoEvent>(OnBatteryTakeAmmo);
        SubscribeLocalEvent<ProjectileBatteryAmmoProviderComponent, CheckShootPrototypeEvent>(OnBatteryCheckProto); // Mono
        SubscribeLocalEvent<ProjectileBatteryAmmoProviderComponent, GetAmmoCountEvent>(OnBatteryAmmoCount);
        SubscribeLocalEvent<ProjectileBatteryAmmoProviderComponent, ExaminedEvent>(OnBatteryExamine);

        InitializeSelectiveFireBattery(); // Forge-Change
    }

    private void OnBatteryHandleState(EntityUid uid, BatteryAmmoProviderComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not BatteryAmmoProviderComponentState state)
            return;

        component.Shots = state.Shots;
        component.Capacity = state.MaxShots;
        component.FireCost = state.FireCost;

        if (component is HitscanBatteryAmmoProviderComponent hitscan && state.Prototype != null) // Shitmed Change
            hitscan.HitscanEntityProto = state.Prototype; // Mono - Changed to HitscanEntityProto
    }

    private void OnBatteryGetState(EntityUid uid, BatteryAmmoProviderComponent component, ref ComponentGetState args)
    {
        var state = new BatteryAmmoProviderComponentState() // Shitmed Change
        {
            Shots = component.Shots,
            MaxShots = component.Capacity,
            FireCost = component.FireCost,
        };

        if (TryComp<HitscanBatteryAmmoProviderComponent>(uid, out var hitscan)) // Shitmed Change
            state.Prototype = hitscan.HitscanEntityProto; // Mono - Changed to HitscanEntityProto

        args.State = state; // Shitmed Change
    }

    private void OnBatteryExamine(EntityUid uid, BatteryAmmoProviderComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("gun-battery-examine", ("color", AmmoExamineColor), ("count", component.Shots)));
    }

    private void OnBatteryTakeAmmo(EntityUid uid, BatteryAmmoProviderComponent component, TakeAmmoEvent args)
    {
        var shots = Math.Min(args.Shots, component.Shots);

        // Don't dirty if it's an empty fire.
        if (shots == 0)
            return;

        for (var i = 0; i < shots; i++)
        {
            args.Ammo.Add(GetShootable(component, args.Coordinates));
            component.Shots--;
        }

        TakeCharge(uid, component);
        UpdateBatteryAppearance(uid, component);
        Dirty(uid, component);
    }

    // Mono
    private void OnBatteryCheckProto(EntityUid uid, BatteryAmmoProviderComponent comp, ref CheckShootPrototypeEvent args)
    {
        switch (comp)
        {
            case ProjectileBatteryAmmoProviderComponent proj:
                ProtoManager.TryIndex(proj.Prototype, out var proto);
                args.ShootPrototype = proto;
                break;
            case HitscanBatteryAmmoProviderComponent hitscan:
                ProtoManager.TryIndex(hitscan.HitscanEntityProto, out var hitProto);
                args.ShootPrototype = hitProto;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void OnBatteryAmmoCount(EntityUid uid, BatteryAmmoProviderComponent component, ref GetAmmoCountEvent args)
    {
        args.Count = component.Shots;
        args.Capacity = component.Capacity;
    }

    /// <summary>
    /// Update the battery (server-only) whenever fired.
    /// </summary>
    protected virtual void TakeCharge(EntityUid uid, BatteryAmmoProviderComponent component)
    {
        UpdateAmmoCount(uid, prediction: false);
    }

    protected void UpdateBatteryAppearance(EntityUid uid, BatteryAmmoProviderComponent component)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        Appearance.SetData(uid, AmmoVisuals.HasAmmo, component.Shots != 0, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoCount, component.Shots, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, component.Capacity, appearance);
    }

    private (EntityUid? Entity, IShootable) GetShootable(BatteryAmmoProviderComponent component, EntityCoordinates coordinates)
    {
        switch (component)
        {
            case ProjectileBatteryAmmoProviderComponent proj:
                var ent = Spawn(proj.Prototype, coordinates);
                return (ent, EnsureShootable(ent));
            case HitscanBatteryAmmoProviderComponent hitscan:
                var hitscanEnt = Spawn(hitscan.HitscanEntityProto);
                return (hitscanEnt, EnsureShootable(hitscanEnt));
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Serializable, NetSerializable]
    private sealed class BatteryAmmoProviderComponentState : ComponentState
    {
        public int Shots;
        public int MaxShots;
        public float FireCost;
        public string? Prototype;
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

        if (!TryComp<ProjectileBatteryAmmoProviderComponent>(uid, out var ammo))
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
            ammo.Shots = (int) Math.Round(ammo.Shots / fireCostDiff);
            ammo.Capacity = (int) Math.Round(ammo.Capacity / fireCostDiff);
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
