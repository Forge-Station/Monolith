using Content.Shared._Forge.Weapons.Events;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Weapons;

/// <summary>
/// Fired seismic charges become impact-detonating projectiles. The tube itself only ever holds normal charges.
/// </summary>
public sealed partial class SeismicChargeBallisticSystem : EntitySystem
{
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private static readonly EntProtoId SeismicBulletProto = "BulletSeismicCharge";
    private static readonly EntProtoId SeismicChargeProto = "SeismicCharge";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SeismicChargeBallisticComponent, TakeAmmoEvent>(OnTakeAmmo, after: [typeof(SharedGunSystem)]);
        SubscribeLocalEvent<SeismicChargeBallisticComponent, BallisticBeforeCycleEvent>(OnBeforeCycle);
        SubscribeLocalEvent<SeismicChargeBallisticComponent, GunCycledEvent>(OnGunCycled);
    }

    private void OnTakeAmmo(EntityUid uid, SeismicChargeBallisticComponent component, TakeAmmoEvent args)
    {
        if (!args.WillBeFired || !TryComp<BallisticAmmoProviderComponent>(uid, out var ballistic))
            return;

        PruneInvalidEntities(uid, ballistic);

        for (var i = 0; i < args.Ammo.Count; i++)
        {
            var (ent, _) = args.Ammo[i];
            if (ent == null || !Exists(ent.Value) || !_tag.HasTag(ent.Value, "SeismicCharge"))
                continue;

            var bullet = ToFlyingBullet(ent.Value, ballistic);
            args.Ammo[i] = (bullet, EnsureComp<AmmoComponent>(bullet));
        }
    }

    private void OnBeforeCycle(EntityUid uid, SeismicChargeBallisticComponent component, ref BallisticBeforeCycleEvent args)
    {
        if (!TryComp<BallisticAmmoProviderComponent>(uid, out var ballistic))
            return;

        SanitizeTube(uid, ballistic);
    }

    private void OnGunCycled(EntityUid uid, SeismicChargeBallisticComponent component, ref GunCycledEvent args)
    {
        if (!TryComp<BallisticAmmoProviderComponent>(uid, out var ballistic))
            return;

        PruneInvalidEntities(uid, ballistic);
    }

    private EntityUid ToFlyingBullet(EntityUid charge, BallisticAmmoProviderComponent ballistic)
    {
        var coords = Transform(charge).Coordinates;

        ballistic.Entities.Remove(charge);
        _containers.Remove(charge, ballistic.Container);

        Del(charge);

        return Spawn(SeismicBulletProto, coords);
    }

    private void SanitizeTube(EntityUid uid, BallisticAmmoProviderComponent ballistic)
    {
        PruneInvalidEntities(uid, ballistic);

        var changed = false;

        for (var i = 0; i < ballistic.Entities.Count; i++)
        {
            var ent = ballistic.Entities[i];
            if (!Exists(ent))
                continue;

            if (_tag.HasTag(ent, "SeismicCharge"))
                continue;

            if (!IsBulletProto(ent))
                continue;

            var coords = Transform(ent).Coordinates;
            _containers.Remove(ent, ballistic.Container);
            Del(ent);

            var charge = Spawn(SeismicChargeProto, coords);
            ballistic.Entities[i] = charge;
            _containers.Insert(charge, ballistic.Container);
            changed = true;
        }

        if (changed)
            Dirty(uid, ballistic);
    }

    private bool IsBulletProto(EntityUid uid)
    {
        return TryPrototype(uid, out var proto) && proto.ID == SeismicBulletProto;
    }

    private void PruneInvalidEntities(EntityUid uid, BallisticAmmoProviderComponent ballistic)
    {
        var changed = false;

        for (var i = ballistic.Entities.Count - 1; i >= 0; i--)
        {
            if (Exists(ballistic.Entities[i]))
                continue;

            ballistic.Entities.RemoveAt(i);
            changed = true;
        }

        if (changed)
            Dirty(uid, ballistic);
    }
}
