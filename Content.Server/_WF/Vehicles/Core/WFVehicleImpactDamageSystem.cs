using System;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>
/// A vehicle that hits something hard hurts its frame and its wheels, more the faster it was going; one it hits
/// takes a share of that too, more from a heavier vehicle.
/// </summary>
public sealed class WFVehicleImpactDamageSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private VehicleWheelSystem _wheels = default!;

    /// <summary>Hits slower than this share of top speed are bumps and cost nothing.</summary>
    public const float MinSpeedFraction = 0.15f;

    /// <summary>Flat damage to each wheel from a hit at top speed.</summary>
    public const float WheelDamage = 30f;

    /// <summary>For a vehicle with no frame integrity: the damage that wrecks it, a hit deals a share of it.</summary>
    public const float BodyHealth = 300f;

    private static readonly ProtoId<DamageTypePrototype> ImpactDamageType = "Blunt";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridVehicleMoverComponent, VehicleCollisionEvent>(OnCollision);
    }

    /// <summary>How much sturdier a weight class is against a crash: light vehicles crumple, heavy ones shrug.</summary>
    public static float WeightScale(VehicleWeightClass weight)
    {
        return weight switch
        {
            VehicleWeightClass.Weak => 1.25f,
            VehicleWeightClass.Light => 1f,
            VehicleWeightClass.Medium => 0.8f,
            _ => 0.6f,
        };
    }

    private void OnCollision(Entity<GridVehicleMoverComponent> ent, ref VehicleCollisionEvent args)
    {
        if (args.Kind != VehicleCollisionKind.Impact)
            return;

        var ratio = args.MaxSpeed > 0f ? Math.Min(MathF.Abs(args.Speed) / args.MaxSpeed, 1.5f) : 0f;
        if (ratio < MinSpeedFraction)
            return;

        Hurt(ent.Owner, ent.Comp.WeightClass, ratio, 1f);

        // The one that was hit feels it as well: more from a heavier vehicle, less from a lighter one.
        if (args.Other is { } other && TryComp(other, out GridVehicleMoverComponent? struck))
        {
            var heavier = 1f + 0.25f * ((int) ent.Comp.WeightClass - (int) struck.WeightClass);
            Hurt(other, struck.WeightClass, ratio, Math.Clamp(heavier, 0.5f, 2f));
        }
    }

    /// <summary>Damages a vehicle's frame by its share, and its wheels, for a hit of this fraction of top speed.</summary>
    public void Hurt(EntityUid vehicle, VehicleWeightClass weight, float ratio, float scale)
    {
        if (TerminatingOrDeleted(vehicle))
            return;

        var share = _cfg.GetCVar(VehicleImpactCVars.FrameFraction) * ratio * WeightScale(weight) * scale;
        if (TryComp(vehicle, out HardpointIntegrityComponent? frame))
        {
            _hardpoints.DamageHardpoint(vehicle, vehicle, frame.MaxIntegrity * share, frame);
        }
        else if (HasComp<DamageableComponent>(vehicle))
        {
            var damage = new DamageSpecifier { DamageDict = { [ImpactDamageType] = BodyHealth * share } };
            _damageable.TryChangeDamage(vehicle, damage, ignoreResistances: true);
        }

        if (TerminatingOrDeleted(vehicle))
            return;

        // Wheels that carry their own collision wear keep it; the rest wear by the same share.
        if (TryComp(vehicle, out VehicleWheelSlotsComponent? slots) && slots.CollisionDamagePerSpeed <= 0f)
            _wheels.DamageWheels(vehicle, WheelDamage * ratio * WeightScale(weight) * scale);
    }
}
