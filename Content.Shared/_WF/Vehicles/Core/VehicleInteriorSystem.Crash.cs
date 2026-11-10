using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared.Buckle.Components;
using Content.Shared.Camera;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>The crash effect on whoever is inside.</summary>
public sealed partial class VehicleInteriorSystem
{
    private const float CrashMinSpeedFraction = 0.15f;
    private const float CrashThrowSpeed = 10f;
    private const float CrashCameraKick = 0.2f;

    [Dependency] private SharedCameraRecoilSystem _cameraRecoil = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    private void InitializeCrash()
    {
        SubscribeLocalEvent<VehicleEnterComponent, VehicleCollisionEvent>(OnVehicleCollision);
    }

    private void OnVehicleCollision(Entity<VehicleEnterComponent> ent, ref VehicleCollisionEvent args)
    {
        if (args.Kind != VehicleCollisionKind.Crash)
            return;

        DoInteriorCrashEffect(ent.Owner, args.Speed, args.MaxSpeed);
    }

    /// <summary>Jolts everyone inside after a dead stop; anyone not strapped in is knocked down and thrown.</summary>
    public void DoInteriorCrashEffect(EntityUid vehicle, float speed, float maxSpeed)
    {
        if (_net.IsClient || maxSpeed <= 0f)
            return;

        var ratio = MathF.Abs(speed) / maxSpeed;
        if (ratio < CrashMinSpeedFraction)
            return;

        PlayCrashSound(vehicle);

        if (!TryComp(vehicle, out VehicleInteriorComponent? interior))
            return;

        var strength = Math.Clamp((int) MathF.Ceiling(ratio * 3f), 1, 3);
        var flingDistance = strength * 2;
        var forward = speed >= 0f ? 1f : -1f;
        var offset = new Vector2(forward * flingDistance, 0f);

        foreach (var occupant in new List<EntityUid>(interior.Passengers))
        {
            if (TerminatingOrDeleted(occupant))
                continue;

            var parent = Transform(occupant).ParentUid;
            var kickDirection = parent.IsValid()
                ? _transform.GetWorldRotation(parent).RotateVec(new Vector2(forward, 0f))
                : new Vector2(forward, 0f);
            _cameraRecoil.KickCamera(occupant, kickDirection * (CrashCameraKick * strength));

            var buckled = TryComp(occupant, out BuckleComponent? buckle) && buckle.BuckledTo != null;
            if (buckled)
                continue;

            _stun.TryParalyze(occupant, TimeSpan.FromSeconds(1), true);
            _stun.TryKnockdown(occupant, TimeSpan.FromSeconds(2), true);

            var target = Transform(occupant).Coordinates.Offset(offset);
            _throwing.TryThrow(occupant, target, CrashThrowSpeed, user: null, pushbackRatio: 0f, compensateFriction: false);
        }
    }

    private void PlayCrashSound(EntityUid vehicle)
    {
        if (!TryComp(vehicle, out VehicleSoundComponent? sound) || sound.CrashSound == null)
            return;

        var now = _timing.CurTime;
        if (sound.NextCrashSound > now)
            return;

        _audio.PlayPvs(sound.CrashSound, vehicle);
        sound.NextCrashSound = now + TimeSpan.FromSeconds(sound.CrashSoundCooldown);
        Dirty(vehicle, sound);
    }
}
