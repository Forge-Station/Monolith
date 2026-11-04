using Content.Shared._WF.Vehicles.Audio;
using Content.Shared.Damage;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Audio;

/// <summary>Plays vehicle combat sounds: a gun's distant boom and loading sequence, hull hits, round ricochets and the vehicle's destruction.</summary>
public sealed partial class WFVehicleCombatAudioSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFGunExtraSoundsComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<WFVehicleHitSoundComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<WFImpactRicochetComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<WFVehicleDestroyedSoundComponent, VehicleFrameIntegrityChangedEvent>(OnFrameIntegrityChanged);
    }

    private void OnProjectileHit(Entity<WFImpactRicochetComponent> ent, ref ProjectileHitEvent args)
    {
        if (HasComp<MobStateComponent>(args.Target) || !_random.Prob(ent.Comp.Chance))
            return;

        _audio.PlayPvs(ent.Comp.Sound, Transform(ent).Coordinates);
    }

    private void OnFrameIntegrityChanged(Entity<WFVehicleDestroyedSoundComponent> ent, ref VehicleFrameIntegrityChangedEvent args)
    {
        if (args.Intact || ent.Comp.Sequence.Count == 0)
            return;

        ent.Comp.DestroyedAt = _timing.CurTime;
        ent.Comp.NextStep = 0;
    }

    private void OnGunShot(Entity<WFGunExtraSoundsComponent> ent, ref GunShotEvent args)
    {
        if (ent.Comp.LoadingSequence.Count > 0)
        {
            ent.Comp.LastShot = _timing.CurTime;
            ent.Comp.NextStep = 0;
        }

        if (ent.Comp.DistantSound == null)
            return;

        var coords = _transform.GetMapCoordinates(ent.Owner);
        var filter = Filter.Empty().AddInRange(coords, ent.Comp.DistantRange);
        filter.RemoveInRange(coords, ent.Comp.DistantMinRange);
        if (filter.Count == 0)
            return;

        var param = AudioParams.Default.WithMaxDistance(ent.Comp.DistantRange).WithRolloffFactor(0.3f);
        _audio.PlayStatic(ent.Comp.DistantSound, filter, _transform.GetMoverCoordinates(ent.Owner), true, param);
    }

    // Read before the hardpoints take their share, so a hit soaked by armour still sounds.
    private void OnDamageModify(Entity<WFVehicleHitSoundComponent> ent, ref DamageModifyEvent args)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextHit)
            return;

        var total = args.OriginalDamage.GetTotal();
        if (total <= FixedPoint2.Zero)
            return;

        var heavy = total >= ent.Comp.HeavyThreshold;
        var sound = heavy ? ent.Comp.HeavyHitSound : ent.Comp.LightHitSound;
        var ricochet = heavy ? ent.Comp.HeavyRicochetSound : ent.Comp.LightRicochetSound;
        var chance = heavy ? ent.Comp.HeavyRicochetChance : ent.Comp.LightRicochetChance;
        if (ricochet == null || !_random.Prob(chance))
            ricochet = null;

        if (sound == null && ricochet == null)
            return;

        ent.Comp.NextHit = now + TimeSpan.FromSeconds(ent.Comp.Cooldown);
        _audio.PlayPvs(sound, ent.Owner);
        _audio.PlayPvs(ricochet, ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFGunExtraSoundsComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.LastShot is not { } shot)
                continue;

            var elapsed = (float) (now - shot).TotalSeconds;
            while (comp.NextStep < comp.LoadingSequence.Count && comp.LoadingSequence[comp.NextStep].Delay <= elapsed)
            {
                _audio.PlayPvs(comp.LoadingSequence[comp.NextStep].Sound, uid);
                comp.NextStep++;
            }

            if (comp.NextStep >= comp.LoadingSequence.Count)
                comp.LastShot = null;
        }

        var wrecks = EntityQueryEnumerator<WFVehicleDestroyedSoundComponent>();
        while (wrecks.MoveNext(out var uid, out var comp))
        {
            if (comp.DestroyedAt is not { } at)
                continue;

            var elapsed = (float) (now - at).TotalSeconds;
            while (comp.NextStep < comp.Sequence.Count && comp.Sequence[comp.NextStep].Delay <= elapsed)
            {
                _audio.PlayPvs(comp.Sequence[comp.NextStep].Sound, uid);
                comp.NextStep++;
            }

            if (comp.NextStep >= comp.Sequence.Count)
                comp.DestroyedAt = null;
        }
    }
}
