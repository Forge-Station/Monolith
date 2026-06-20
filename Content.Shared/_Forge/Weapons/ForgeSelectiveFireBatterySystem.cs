using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Weapons;

public sealed partial class ForgeSelectiveFireBatterySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ForgeSelectiveFireBatteryComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ForgeSelectiveFireBatteryComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<ForgeSelectiveFireBatteryComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
        SubscribeLocalEvent<ForgeSelectiveFireBatteryComponent, CheckShootPrototypeEvent>(OnCheckShootPrototype);
    }

    private void OnMapInit(EntityUid uid, ForgeSelectiveFireBatteryComponent comp, MapInitEvent args)
    {
        SyncAmmoProvider(uid, comp);
    }

    private void OnAttemptShoot(EntityUid uid, ForgeSelectiveFireBatteryComponent comp, ref AttemptShootEvent args)
    {
        SyncAmmoProvider(uid, comp);
        _gun.RefreshModifiers(uid, args.User);
    }

    private void OnGunRefreshModifiers(EntityUid uid, ForgeSelectiveFireBatteryComponent comp, ref GunRefreshModifiersEvent args)
    {
        SyncAmmoProvider(uid, comp);

        if (!TryComp<GunComponent>(uid, out var gun))
            return;

        var mode = GetModeEntry(comp, gun.SelectedMode);
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

    private void OnCheckShootPrototype(EntityUid uid, ForgeSelectiveFireBatteryComponent comp, ref CheckShootPrototypeEvent args)
    {
        if (!TryComp<GunComponent>(uid, out var gun))
            return;

        var mode = GetModeEntry(comp, gun.SelectedMode);
        if (mode == null || !_prototypes.TryIndex(mode.Proto, out var proto))
            return;

        args.ShootPrototype = proto;
    }

    private void SyncAmmoProvider(EntityUid uid, ForgeSelectiveFireBatteryComponent comp)
    {
        if (!TryComp<GunComponent>(uid, out var gun))
            return;

        if (!TryComp<ProjectileBatteryAmmoProviderComponent>(uid, out var ammo))
            return;

        var mode = GetModeEntry(comp, gun.SelectedMode);
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

    private static ForgeSelectiveFireBatteryMode? GetModeEntry(ForgeSelectiveFireBatteryComponent comp, SelectiveFire mode)
    {
        foreach (var entry in comp.Modes)
        {
            if (entry.Mode == mode)
                return entry;
        }

        return comp.Modes.Count > 0 ? comp.Modes[0] : null;
    }
}
