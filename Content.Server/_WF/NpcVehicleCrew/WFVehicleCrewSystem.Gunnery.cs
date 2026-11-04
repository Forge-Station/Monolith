using Content.Server._WF.Vehicles.Npc;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.NpcVehicleCrew;

/// <summary>The gunner: picks a target and a gun with rounds, lays the turret and fires with his skill's delay and error.</summary>
public sealed partial class WFVehicleCrewSystem
{
    private static readonly TimeSpan GunneryInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>How long a target out of sight is kept before it is given up.</summary>
    private static readonly TimeSpan TargetForgetTime = TimeSpan.FromSeconds(60);

    /// <summary>Burst discipline of automatic guns: fire this long, then rest.</summary>
    private static readonly TimeSpan AutomaticBurst = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan AutomaticRest = TimeSpan.FromSeconds(1.5);

    /// <summary>Guns firing faster than this many rounds a second keep burst discipline; slower ones fire on cooldown.</summary>
    private const float AutomaticFireRate = 1.5f;

    /// <summary>How far off the target's bearing the barrel may be when the trigger is pulled.</summary>
    private const float FireConeDegrees = 8f;

    /// <summary>Speed below which a vehicle that does not fire on the move counts as stopped.</summary>
    private const float StoppedSpeed = 0.3f;

    private readonly List<VehicleMountedSlot> _slots = new();
    private readonly HashSet<EntityUid> _candidates = new();

    /// <summary>Seconds from picking a target to the first shot, and degrees a shot may stray, by skill.</summary>
    private static (float Delay, float Error) GunnerySkill(WFCrewSkill skill)
    {
        return skill switch
        {
            WFCrewSkill.Green => (2.5f, 8f),
            WFCrewSkill.Regular => (2f, 6f),
            WFCrewSkill.Elite => (0.8f, 2f),
            _ => (1.5f, 4f),
        };
    }

    /// <summary>The gunner, if he is alive, awake and working the guns.</summary>
    private bool TryGetGunner(WFVehicleCrewComponent crew, out EntityUid gunner)
    {
        gunner = crew.Gunner ?? EntityUid.Invalid;
        return crew.Gunner != null &&
               IsAlive(gunner) &&
               !_mobState.IsIncapacitated(gunner) &&
               HasComp<VehicleWeaponsOperatorComponent>(gunner);
    }

    /// <summary>Picks the gun and keeps or changes the target: the current one while in sight, else the nearest in sight.</summary>
    private void UpdateTarget(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, TimeSpan now)
    {
        if (!TryGetGunner(crew, out var gunner))
        {
            crew.Target = null;
            crew.TargetVisible = false;
            crew.Weapon = null;
            return;
        }

        crew.Weapon = PickWeapon(uid, crew, gunner);
        var here = _transform.GetMapCoordinates(uid);
        var range = proto.EngageRange;

        if (crew.Target is { } current)
        {
            if (!IsValidTarget(uid, crew, current, here, range, out var position))
            {
                crew.Target = null;
                crew.TargetVisible = false;
            }
            else if (CanSee(here, position, range))
            {
                See(crew, position, now);
                return;
            }
        }

        CollectCandidates(uid, crew, proto, range);
        EntityUid? best = null;
        var bestPosition = default(MapCoordinates);
        var bestDistance = float.MaxValue;
        foreach (var candidate in _candidates)
        {
            if (candidate == crew.Target ||
                !IsValidTarget(uid, crew, candidate, here, range, out var position) ||
                !CanSee(here, position, range))
            {
                continue;
            }

            var distance = Distance(here, position);
            if (distance >= bestDistance)
                continue;

            best = candidate;
            bestPosition = position;
            bestDistance = distance;
        }

        if (best is { } found)
        {
            crew.Target = found;
            crew.AcquiredAt = now;
            See(crew, bestPosition, now);
            return;
        }

        crew.TargetVisible = false;
        if (crew.Target != null && now - crew.LastSeenAt > TargetForgetTime)
            crew.Target = null;
    }

    private static void See(WFVehicleCrewComponent crew, MapCoordinates position, TimeSpan now)
    {
        crew.TargetVisible = true;
        crew.LastSeenTarget = position;
        crew.LastSeenAt = now;
    }

    /// <summary>Who the crew may fight: its attackers, an escorted lead's attackers, and faction hostiles for an on-sight crew.</summary>
    private void CollectCandidates(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, float range)
    {
        _candidates.Clear();
        foreach (var attacker in crew.Attackers.Keys)
            _candidates.Add(attacker);

        if (crew.Behaviour == WFVehicleBehaviour.Escort)
        {
            if (crew.Lead is { } lead && TryComp<WFVehicleCrewComponent>(lead, out var leadCrew))
            {
                foreach (var attacker in leadCrew.Attackers.Keys)
                    _candidates.Add(attacker);
            }

            return;
        }

        if (proto.Engagement != WFCrewEngagement.OnSight)
            return;

        // The vehicle carries the crew's faction, and unlike the crew it stands on the map the hostiles are on.
        foreach (var hostile in _factions.GetNearbyHostiles(uid, range))
            _candidates.Add(hostile);
    }

    /// <summary>Whether a candidate can be fought: alive, on this map, in range, not a grid, an empty vehicle or one of the crew.</summary>
    private bool IsValidTarget(EntityUid uid, WFVehicleCrewComponent crew, EntityUid target, MapCoordinates here, float range,
        out MapCoordinates position)
    {
        position = default;
        if (target == uid || TerminatingOrDeleted(target) || IsOwnCrew(crew, target) || HasComp<MapGridComponent>(target))
            return false;

        position = _transform.GetMapCoordinates(target);
        if (Distance(here, position) > range)
            return false;

        if (TryComp<MobStateComponent>(target, out var mobState) && _mobState.IsIncapacitated(target, mobState))
            return false;

        if (HasComp<RMCVehicleComponent>(target) && !HasComp<WFOccupiedVehicleComponent>(target))
            return false;

        // An escort never strays further than its engage range from the lead.
        if (crew.Behaviour == WFVehicleBehaviour.Escort &&
            crew.Lead is { } lead &&
            !TerminatingOrDeleted(lead) &&
            Distance(_transform.GetMapCoordinates(lead), position) > range)
        {
            return false;
        }

        return true;
    }

    private bool CanSee(MapCoordinates from, MapCoordinates to, float range)
    {
        return _examine.InRangeUnOccluded(from, to, range, null);
    }

    /// <summary>
    /// Gives the gunner the gun he has while it has rounds, else the first mounted gun with rounds in slot order, and
    /// records whether every gun is dry.
    /// </summary>
    private EntityUid? PickWeapon(EntityUid uid, WFVehicleCrewComponent crew, EntityUid gunner)
    {
        _topology.GetMountedSlots(uid, _slots);
        EntityUid? first = null;
        var keepCurrent = false;
        foreach (var slot in _slots)
        {
            if (slot.Item is not { } item ||
                !HasComp<GunComponent>(item) ||
                !HasComp<VehicleTurretComponent>(item) ||
                !HasRounds(uid, slot))
            {
                continue;
            }

            first ??= item;
            if (crew.Weapon == item)
                keepCurrent = true;
        }

        crew.OutOfAmmo = first == null;
        var choice = keepCurrent ? crew.Weapon : first;
        return choice is { } weapon && _weapons.TrySelectHardpointForNpc(uid, gunner, weapon) ? weapon : null;
    }

    /// <summary>Whether a mounted gun has a round ready or in reserve; one without ballistic ammo always does.</summary>
    private bool HasRounds(EntityUid uid, VehicleMountedSlot slot)
    {
        if (!_topology.TryGetMountedAmmoProvider(uid, slot.Path, out var provider))
            return true;

        return provider.Ammo.Count > 0 ||
               provider.HardpointAmmo.StoredRounds > 0 ||
               provider.HardpointAmmo.StoredMagazines > 0;
    }

    /// <summary>Lays the gun on a visible target and fires when the barrel bears, after the skill's delay, with its error.</summary>
    private void UpdateTrigger(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, TimeSpan now)
    {
        crew.Face = null;
        crew.CanBear = true;
        if (crew.Target is not { } target ||
            !crew.TargetVisible ||
            crew.Weapon is not { } weapon ||
            TerminatingOrDeleted(weapon) ||
            !TryGetGunner(crew, out var gunner) ||
            !TryComp<GunComponent>(weapon, out var gun))
        {
            return;
        }

        var here = _transform.GetMapCoordinates(uid);
        var targetPosition = _transform.GetMapCoordinates(target);
        if (targetPosition.MapId != here.MapId)
            return;

        crew.CanBear = _turret.TryAimAtTarget(weapon, target, out _);
        if (!crew.CanBear)
            crew.Face = targetPosition;

        var origin = _turret.TryGetTurretOrigin(weapon, out var originCoords) ? _transform.ToMapCoordinates(originCoords) : here;
        if (origin.MapId != targetPosition.MapId)
            origin = here;

        var toTarget = targetPosition.Position - origin.Position;
        if (toTarget.LengthSquared() < 0.01f || !_turret.TryGetBarrelWorldRotation(weapon, out var barrel))
            return;

        if (Math.Abs(Angle.ShortestDistance(barrel, toTarget.ToWorldAngle()).Degrees) > FireConeDegrees)
            return;

        var (delay, error) = GunnerySkill(proto.Skill);
        if (now < crew.AcquiredAt + TimeSpan.FromSeconds(delay))
            return;

        // A crew that does not fire on the move stops first; a retreating one returns fire whatever it is doing.
        if (!proto.FireOnTheMove &&
            crew.Behaviour != WFVehicleBehaviour.Retreat &&
            TryComp<GridVehicleMoverComponent>(uid, out var mover) &&
            MathF.Abs(mover.CurrentSpeed) > StoppedSpeed)
        {
            return;
        }

        if (gun.FireRateModified > AutomaticFireRate)
        {
            if (now >= crew.RestUntil)
            {
                crew.BurstUntil = now + AutomaticBurst;
                crew.RestUntil = crew.BurstUntil + AutomaticRest;
            }

            if (now > crew.BurstUntil)
                return;
        }

        var stray = Angle.FromDegrees(_random.NextFloat(-error, error));
        var aim = new MapCoordinates(origin.Position + stray.RotateVec(toTarget), origin.MapId);
        if (!_combat.IsInCombatMode(gunner))
            _combat.SetInCombatMode(gunner, true);

        _gun.AttemptShoot(gunner, weapon, gun, _transform.ToCoordinates(aim));
    }
}
