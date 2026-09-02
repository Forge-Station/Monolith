using System.Collections.Generic;
using Content.Server.Botany.Components;
using Content.Shared._Forge.Botany.PlantGrab;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Botany.PlantGrab;

public sealed class PlantGrabSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly DamageSpecifier GrabDamage = new()
    {
        DamageDict = { ["Slash"] = 5 }
    };

    private TimeSpan _nextGrabCheck = TimeSpan.Zero;
    private TimeSpan _nextDamage = TimeSpan.Zero;
    private static readonly TimeSpan GrabInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DamageInterval = TimeSpan.FromSeconds(3);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime >= _nextGrabCheck)
        {
            _nextGrabCheck = _timing.CurTime + GrabInterval;
            TryGrabNearby();
        }

        if (_timing.CurTime >= _nextDamage)
        {
            _nextDamage = _timing.CurTime + DamageInterval;
            ApplyGrabDamage();
        }
    }

    private void TryGrabNearby()
    {
        var query = EntityQueryEnumerator<PlantHolderComponent, TransformComponent>();
        while (query.MoveNext(out var tray, out var holder, out _))
        {
            if (holder.Seed is not { CarnivorousGrab: true } seed || holder.Dead)
                continue;

            if (holder.Age < seed.Maturation && !holder.Harvest)
                continue;

            if (HasGrabbedVictim(tray))
                continue;

            foreach (var target in _lookup.GetEntitiesInRange(tray, seed.GrabRange, LookupFlags.Dynamic))
            {
                if (target == tray)
                    continue;

                if (HasComp<GhostComponent>(target) || HasComp<PlantHolderComponent>(target))
                    continue;

                if (!HasComp<MobStateComponent>(target) || !_mobState.IsAlive(target))
                    continue;

                if (HasComp<PlantGrabbedComponent>(target))
                    continue;

                var grabbed = EnsureComp<PlantGrabbedComponent>(target);
                grabbed.Grabber = tray;
                Dirty(target, grabbed);

                _popup.PopupEntity(Loc.GetString("plant-grab-caught", ("plant", Loc.GetString(seed.DisplayName))), target, target, PopupType.LargeCaution);
                break;
            }
        }
    }

    private void ApplyGrabDamage()
    {
        var toRelease = new List<EntityUid>();
        var query = EntityQueryEnumerator<PlantGrabbedComponent>();
        while (query.MoveNext(out var uid, out var grabbed))
        {
            if (TerminatingOrDeleted(grabbed.Grabber) ||
                !TryComp<PlantHolderComponent>(grabbed.Grabber, out var holder) ||
                holder.Seed is not { CarnivorousGrab: true } ||
                holder.Dead)
            {
                toRelease.Add(uid);
                continue;
            }

            var plantPos = _transform.GetWorldPosition(grabbed.Grabber);
            var victimPos = _transform.GetWorldPosition(uid);
            // Only drop the grab if the victim was teleported/moved far away; walking out is not an escape.
            if ((plantPos - victimPos).Length() > holder.Seed.GrabRange + 8f)
            {
                toRelease.Add(uid);
                continue;
            }

            var damage = GrabDamage * MathF.Max(1f, holder.Seed.Potency / 50f);
            _damageable.TryChangeDamage(uid, damage, origin: grabbed.Grabber);

            if (_random.Prob(0.35f))
                _popup.PopupEntity(Loc.GetString("plant-grab-damage"), uid, uid, PopupType.SmallCaution);
        }

        foreach (var uid in toRelease)
        {
            RemComp<PlantGrabbedComponent>(uid);
        }
    }

    private bool HasGrabbedVictim(EntityUid tray)
    {
        var query = EntityQueryEnumerator<PlantGrabbedComponent>();
        while (query.MoveNext(out _, out var grabbed))
        {
            if (grabbed.Grabber == tray)
                return true;
        }

        return false;
    }

    public void ReleaseAllFrom(EntityUid tray)
    {
        var toRelease = new List<EntityUid>();
        var query = EntityQueryEnumerator<PlantGrabbedComponent>();
        while (query.MoveNext(out var uid, out var grabbed))
        {
            if (grabbed.Grabber == tray)
                toRelease.Add(uid);
        }

        foreach (var uid in toRelease)
            RemComp<PlantGrabbedComponent>(uid);
    }
}
