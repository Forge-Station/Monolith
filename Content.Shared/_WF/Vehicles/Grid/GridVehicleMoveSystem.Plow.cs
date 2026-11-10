using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>
/// What a plow does to the wall it is driven into: a ram's worth of damage, and from a plow chassis a steady grind.
/// The vehicle is stopped by the wall either way and drives on once the wall is gone.
/// </summary>
public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    /// <summary>Contacts slower than this do no ram damage, whatever the vehicle's own gate says.</summary>
    private const float PlowMinRamSpeed = 0.5f;

    private const int PlowCooldownPruneCount = 64;
    private static readonly TimeSpan PlowContactGrace = TimeSpan.FromSeconds(0.25);

    private readonly record struct PlowContact(
        EntityUid Target,
        TimeSpan StartedAt,
        TimeSpan LastContactAt,
        TimeSpan NextSoundAt,
        bool Announced);

    private readonly Dictionary<EntityUid, PlowContact> _plowContacts = new();
    private readonly Dictionary<(EntityUid Vehicle, EntityUid Target), TimeSpan> _plowCooldowns = new();
    private readonly List<(EntityUid Vehicle, EntityUid Target)> _plowExpired = new();

    /// <summary>Damages what stopped the vehicle when a working plow is pushed front-on into something a plow can break.</summary>
    private void TryPlowInto(
        EntityUid vehicle,
        GridVehicleMoverComponent mover,
        FixturesComponent fixtures,
        Vector2 gridPos,
        Angle rotation,
        in CollisionCandidate candidate)
    {
        if (_net.IsClient || candidate.IsVehicle || candidate.Door != null || candidate.MobState != null)
            return;

        // Only a vehicle pushing forwards plows.
        if (mover.CurrentSpeed <= 0f || !TryGetWorkingPlow(vehicle, out var plow))
            return;

        if (!TryGetFixtureLocalAabb(fixtures, out var hull) ||
            !GridVehicleMotionSimulator.IsFrontImpact(gridPos, rotation, hull, candidate.Aabb))
        {
            return;
        }

        var target = candidate.Entity;
        var now = _timing.CurTime;
        TryComp(vehicle, out VehiclePlowChassisComponent? chassis);
        var grinds = chassis is { PoweredDemolitionDamagePerSecond: > 0f };

        var contact = _plowContacts.TryGetValue(vehicle, out var known) &&
                      known.Target == target &&
                      now - known.LastContactAt <= PlowContactGrace
            ? known with { LastContactAt = now }
            : new PlowContact(target, now, now, TimeSpan.Zero, false);

        if (!_smashRules.IsPlowable(target))
        {
            // A grinding plow tells its driver once that this wall will not give.
            if (grinds && !contact.Announced && _smashRules.IsWall(target))
            {
                PopupPlow(vehicle, "wf-vehicle-plow-indestructible", PopupType.LargeCaution);
                contact = contact with { Announced = true };
            }

            if (grinds)
                _plowContacts[vehicle] = contact;

            return;
        }

        var bonus = MathF.Max(1f, plow.StructureDamageMultiplier) * MathF.Max(1f, chassis?.StructureDamageMultiplier ?? 1f);
        var speed = mover.CurrentSpeed;
        var ram = speed >= MathF.Max(PlowMinRamSpeed, mover.PlowRamMinSpeed)
            ? speed * speed * MathF.Max(0f, mover.PlowRamDamage) * bonus
            : 0f;

        var interval = MathF.Max(mover.PlowRamCooldown, MovementFixedStep);
        var grind = 0f;
        if (grinds && now - contact.StartedAt >= TimeSpan.FromSeconds(MathF.Max(0f, chassis!.PoweredDemolitionWarmup)))
        {
            grind = chassis.PoweredDemolitionDamagePerSecond * interval;

            if (!contact.Announced)
            {
                PopupPlow(vehicle, "wf-vehicle-plow-working", PopupType.Medium);
                contact = contact with { Announced = true };
            }

            if (chassis.PoweredDemolitionSound != null && now >= contact.NextSoundAt)
            {
                _audio.PlayPvs(chassis.PoweredDemolitionSound, vehicle);
                contact = contact with
                {
                    NextSoundAt = now + TimeSpan.FromSeconds(MathF.Max(0f, chassis.PoweredDemolitionSoundCooldown)),
                };
            }
        }

        if (grinds)
            _plowContacts[vehicle] = contact;

        var amount = MathF.Max(ram, grind);
        if (amount <= 0f)
            return;

        var key = (vehicle, target);
        if (_plowCooldowns.TryGetValue(key, out var until) && now < until)
            return;

        PrunePlowCooldowns(now);
        _plowCooldowns[key] = now + TimeSpan.FromSeconds(interval);

        var damage = new DamageSpecifier
        {
            DamageDict =
            {
                [CollisionDamageType] = amount,
            },
        };

        _damageable.TryChangeDamage(target, damage, true, origin: vehicle, tool: vehicle);
    }

    /// <summary>The plow fitted to a vehicle, if it has one that is not broken.</summary>
    private bool TryGetWorkingPlow(EntityUid vehicle, [NotNullWhen(true)] out VehiclePlowComponent? plow)
    {
        plow = null;
        if (!TryComp(vehicle, out ItemSlotsComponent? slots))
            return false;

        foreach (var slot in slots.Slots.Values)
        {
            if (slot.Item is not { } item || !TryComp(item, out VehiclePlowComponent? candidate))
                continue;

            if (TryComp(item, out HardpointIntegrityComponent? integrity) && integrity.Integrity <= 0f)
                continue;

            plow = candidate;
            return true;
        }

        return false;
    }

    private void PopupPlow(EntityUid vehicle, string message, PopupType type)
    {
        if (TryComp(vehicle, out RMCVehicleComponent? comp) && comp.Operator is { } driver)
            _popup.PopupCursor(Loc.GetString(message), driver, type);
    }

    private void PrunePlowCooldowns(TimeSpan now)
    {
        if (_plowCooldowns.Count < PlowCooldownPruneCount)
            return;

        _plowExpired.Clear();
        foreach (var (key, until) in _plowCooldowns)
        {
            if (now >= until)
                _plowExpired.Add(key);
        }

        foreach (var key in _plowExpired)
        {
            _plowCooldowns.Remove(key);
        }
    }
}
