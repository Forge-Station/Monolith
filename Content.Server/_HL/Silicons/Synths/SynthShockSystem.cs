using Content.Server._HL.Silicons.Synths.Battery;
using Content.Shared._HL.Silicons.Synths;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Server.Power.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._HL.Silicons.Synths;

public sealed partial class SynthShockSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly SynthBatterySystem _synthBattery = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SynthShockComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(Entity<SynthShockComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased ||
            args.DamageDelta == null ||
            !args.DamageDelta.DamageDict.TryGetValue(ent.Comp.ShockDamageType, out var shockDamage) ||
            shockDamage <= 0)
            return;

        ChargeBattery(ent, shockDamage);
        ApplyCellularDamage(ent, shockDamage, args.Origin);
    }

    private void ChargeBattery(Entity<SynthShockComponent> ent, FixedPoint2 shockDamage)
    {
        if (ent.Comp.BatteryChargeMultiplier <= 0f ||
            !TryComp(ent.Owner, out SynthBatteryComponent? synthBattery) ||
            !_synthBattery.TryGetBattery(ent.Owner, out var battery, synthBattery))
            return;

        var charge = shockDamage.Float() * ent.Comp.BatteryChargeMultiplier;
        _battery.ChangeCharge(battery.Value.Owner, charge, battery.Value.Comp);
    }

    private void ApplyCellularDamage(Entity<SynthShockComponent> ent, FixedPoint2 shockDamage, EntityUid? origin)
    {
        if (ent.Comp.CellularDamageMultiplier <= 0f)
            return;

        var cellularDamage = FixedPoint2.New(shockDamage.Float() * ent.Comp.CellularDamageMultiplier);
        if (cellularDamage <= 0)
            return;

        var damage = new DamageSpecifier(_prototypeManager.Index(ent.Comp.CellularDamageType), cellularDamage);
        _damageable.TryChangeDamage(ent.Owner, damage, origin: origin);
    }
}
