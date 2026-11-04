using System.Numerics;
using Content.Shared._WF.Vehicles.Audio;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Turret;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Client._WF.Vehicles.Audio;

/// <summary>Plays a vehicle's engine, track and turret traverse sounds locally from its networked state.</summary>
public sealed partial class WFVehicleAudioSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const float SilentGain = 0.0001f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFVehicleEngineAudioComponent, ComponentStartup>(OnEngineStartup);
        SubscribeLocalEvent<WFVehicleEngineAudioComponent, ComponentShutdown>(OnEngineShutdown);
        SubscribeLocalEvent<WFTurretTraverseAudioComponent, ComponentStartup>(OnTurretStartup);
        SubscribeLocalEvent<WFTurretTraverseAudioComponent, ComponentShutdown>(OnTurretShutdown);
    }

    private void OnTurretStartup(Entity<WFTurretTraverseAudioComponent> ent, ref ComponentStartup args)
    {
        if (TryComp(ent, out VehicleTurretComponent? turret))
            ent.Comp.LastRotation = turret.WorldRotation;
    }

    private void OnEngineStartup(Entity<WFVehicleEngineAudioComponent> ent, ref ComponentStartup args)
    {
        // A vehicle that comes into view already driven skips the start-up sound.
        if (TryComp(ent, out WFVehicleEngineComponent? ignition) && ignition.Running)
        {
            ent.Comp.EngineOn = true;
            ent.Comp.Warmup = 1f;
        }
    }

    private void OnEngineShutdown(Entity<WFVehicleEngineAudioComponent> ent, ref ComponentShutdown args)
    {
        StopLoops(ent.Comp);
    }

    private void OnTurretShutdown(Entity<WFTurretTraverseAudioComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.LoopStream = Silence(ent.Comp.LoopStream);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // A vehicle out of view is parked in nullspace, where the lobby's listener is too: it would be heard there.
        var engines = EntityQueryEnumerator<WFVehicleEngineAudioComponent, RMCVehicleComponent, TransformComponent>();
        while (engines.MoveNext(out var uid, out var engine, out var vehicle, out var xform))
        {
            if (xform.MapID == MapId.Nullspace)
                StopLoops(engine);
            else
                UpdateEngine(uid, engine, vehicle, frameTime);
        }

        var turrets = EntityQueryEnumerator<WFTurretTraverseAudioComponent, VehicleTurretComponent, TransformComponent>();
        while (turrets.MoveNext(out var uid, out var audio, out var turret, out var xform))
        {
            if (xform.MapID == MapId.Nullspace)
                audio.LoopStream = Silence(audio.LoopStream);
            else
                UpdateTurret(uid, audio, turret, frameTime);
        }
    }

    private void UpdateEngine(EntityUid uid, WFVehicleEngineAudioComponent engine, RMCVehicleComponent vehicle, float frameTime)
    {
        var running = TryComp(uid, out WFVehicleEngineComponent? ignition) ? ignition.Running : vehicle.Operator != null;
        if (running != engine.EngineOn)
        {
            engine.EngineOn = running;
            if (running)
            {
                engine.Warmup = 0f;
                PlayOnce(engine.StartSound, uid, engine.LoopParams);
            }
            else
            {
                StopLoops(engine);
                PlayOnce(engine.StopSound, uid, engine.LoopParams);
                engine.Throttle = 0f;
                engine.PeakSpeed = 0f;
                engine.Load = 0f;
                engine.Gear = 0;
            }
        }

        var speed = 0f;
        var pivot = 0f;
        var acceleration = 1f;
        var fullSpeed = engine.FullSpeed;
        if (TryComp(uid, out GridVehicleMoverComponent? mover))
        {
            speed = MathF.Abs(mover.CurrentSpeed);
            acceleration = MathF.Max(mover.Acceleration, 0.1f);
            if (fullSpeed <= 0f)
                fullSpeed = mover.MaxSpeed;

            if (mover.MaxRotationSpeedDegrees > 0f)
                pivot = Math.Clamp(MathF.Abs(mover.AngularVelocityDegrees) / mover.MaxRotationSpeedDegrees, 0f, 1f);
        }

        fullSpeed = MathF.Max(fullSpeed, 0.1f);
        var speedFraction = Math.Clamp(speed / fullSpeed, 0f, 1f);
        // Turning in place counts only while the hull is barely rolling; tanks pivot at up to 1 tile/s.
        pivot *= 1f - Math.Clamp((speed - 1f) / 1.5f, 0f, 1f);

        // Load: how hard the engine is pulling, from the change in speed.
        var accelerating = frameTime > 0f ? (speed - engine.LastSpeed) / frameTime : 0f;
        engine.BrakeCooldown -= frameTime;
        engine.SkidCooldown -= frameTime;
        if (accelerating < -5f && speed > 4f && engine.BrakeCooldown <= 0f)
        {
            PlayOnce(engine.BrakeSound, uid, engine.LoopParams.WithVolume(-10f));
            engine.BrakeCooldown = 10f;
        }
        var heading = mover?.Heading ?? default;
        if (mover != null && !mover.TurnInPlace && speed > 6f && engine.SkidCooldown <= 0f &&
            (accelerating < -8f || mover.FreeSteering &&
                Vector2.Distance(mover.TravelVelocity, heading.ToWorldVec() * mover.CurrentSpeed) > 3f))
        {
            PlayOnce(engine.SkidSound, uid, engine.LoopParams.WithVolume(-8f));
            engine.SkidCooldown = 10f;
        }
        engine.LastSpeed = speed;
        if (!engine.EngineOn)
        {
            engine.TracksStream = UpdateLoop(engine.TracksStream, engine.TracksLoop, uid, engine.LoopParams,
                MathF.Min(1f, speedFraction * 2.5f), Lerp(engine.TracksPitch, speedFraction));
            return;
        }
        var load = Math.Clamp(accelerating / acceleration, 0f, 1f);
        engine.Load += (load - engine.Load) * Math.Clamp(6f * frameTime, 0f, 1f);

        var revs = speedFraction;
        var shifted = false;
        if (engine.Gears > 0 && speed > 0.3f)
        {
            // The gear the speed calls for; the box steps toward it one gear per ShiftTime, so a lagging gear revs out.
            var position = speedFraction * engine.Gears;
            var wanted = Math.Min((int) position, engine.Gears - 1);
            engine.SinceShift += frameTime;
            if (engine.SinceShift >= engine.ShiftTime && wanted != engine.Gear)
            {
                if (wanted > engine.Gear)
                {
                    PlayOnce(engine.ShiftSound, uid, engine.LoopParams);
                    shifted = true;
                }

                engine.Gear += Math.Sign(wanted - engine.Gear);
                engine.SinceShift = 0f;
            }

            // Revs run from 0.3 at the bottom of the current gear to 1 at its top. A gear the speed has outrun climbs from
            // 0.55 to the redline over ShiftTime instead, so each shift on hard acceleration drops and climbs again.
            revs = wanted > engine.Gear
                ? 0.55f + 0.45f * Math.Clamp(engine.SinceShift / engine.ShiftTime, 0f, 1f)
                : 0.3f + 0.7f * Math.Clamp(position - engine.Gear, 0f, 1f);
        }
        else
        {
            engine.Gear = 0;
            engine.SinceShift = engine.ShiftTime;
        }

        if (speed < 0.5f && engine.Load > 0.5f && engine.NextRev <= 0f)
        {
            PlayOnce(engine.RevSound, uid, engine.LoopParams);
            engine.NextRev = engine.RevCooldown;
        }

        engine.NextRev -= frameTime;

        var target = Math.Clamp(MathF.Max(revs, pivot * engine.PivotThrottle) + engine.Load * engine.LoadRevs, 0f, 1f);
        // An upshift drops the revs at once rather than easing down, so the change is heard.
        engine.Throttle = shifted
            ? MathF.Min(engine.Throttle, MathF.Min(target, 0.35f))
            : engine.Throttle + (target - engine.Throttle) * Math.Clamp(engine.ThrottleResponse * frameTime, 0f, 1f);
        engine.Warmup = engine.StartDelay <= 0f ? 1f : MathF.Min(1f, engine.Warmup + frameTime / engine.StartDelay);

        if (speed >= engine.TracksStopMinSpeed)
            engine.PeakSpeed = MathF.Max(engine.PeakSpeed, speed);
        else if (speed < 0.2f && engine.PeakSpeed > 0f)
        {
            engine.PeakSpeed = 0f;
            PlayOnce(engine.TracksStopSound, uid, engine.LoopParams);
        }

        var t = engine.Throttle;
        var idleGain = engine.Warmup * (1f - (1f - engine.IdleFloor) * t);
        var high = engine.HighLoop == null ? 0f : Math.Clamp((t - 0.55f) / 0.45f, 0f, 1f);
        var driveGain = engine.Warmup * MathF.Min(1f, t * 2f) * (1f - 0.7f * high);
        var highGain = engine.Warmup * high;
        var tracksGain = MathF.Max(MathF.Min(1f, speedFraction * 2.5f), pivot * 0.8f);
        var pivotGain = engine.Warmup * pivot;

        if (pivot > 0.3f)
        {
            engine.NextScreech -= frameTime;
            if (engine.NextScreech <= 0f)
            {
                PlayOnce(engine.PivotScreechSound, uid, engine.LoopParams);
                var range = engine.PivotScreechInterval;
                engine.NextScreech = range.X + _random.NextFloat() * (range.Y - range.X);
            }
        }
        else
        {
            engine.NextScreech = 0.4f;
        }

        engine.IdleStream = UpdateLoop(engine.IdleStream, engine.IdleLoop, uid, engine.LoopParams, idleGain, Lerp(engine.IdlePitch, t));
        engine.DriveStream = UpdateLoop(engine.DriveStream, engine.DriveLoop, uid, engine.LoopParams, driveGain, Lerp(engine.DrivePitch, t));
        engine.TracksStream = UpdateLoop(engine.TracksStream, engine.TracksLoop, uid, engine.LoopParams, tracksGain, Lerp(engine.TracksPitch, MathF.Max(speedFraction, pivot * 0.4f)));
        engine.PivotStream = UpdateLoop(engine.PivotStream, engine.PivotLoop, uid, engine.LoopParams, pivotGain, 1f);
        engine.HighStream = UpdateLoop(engine.HighStream, engine.HighLoop, uid, engine.LoopParams, highGain, Lerp(engine.HighPitch, high));
    }

    private void UpdateTurret(EntityUid uid, WFTurretTraverseAudioComponent audio, VehicleTurretComponent turret, float frameTime)
    {
        var rotation = turret.WorldRotation;
        var moved = !rotation.EqualsApprox(audio.LastRotation, 0.001);
        audio.LastRotation = rotation;

        if (moved)
        {
            audio.StillTime = 0f;
            if (!audio.Turning)
            {
                audio.Turning = true;
                PlayOnce(audio.StartSound, uid, audio.Params);
            }
        }
        else if (audio.Turning)
        {
            audio.StillTime += frameTime;
            if (audio.StillTime >= audio.StopGrace)
            {
                audio.Turning = false;
                audio.LoopStream = Silence(audio.LoopStream);
                PlayOnce(audio.StopSound, uid, audio.Params);
            }
        }

        if (audio.Turning)
            audio.LoopStream = UpdateLoop(audio.LoopStream, audio.LoopSound, uid, audio.Params, 1f, 1f);
    }

    /// <summary>Keeps a looping stream alive on the entity at the given gain and pitch, starting it again if it was lost.</summary>
    private EntityUid? UpdateLoop(EntityUid? stream, SoundSpecifier? sound, EntityUid uid, AudioParams baseParams, float gain, float pitch)
    {
        if (sound == null)
            return null;

        if (stream == null || TerminatingOrDeleted(stream.Value))
        {
            var param = baseParams.WithLoop(true);
            stream = _audio.PlayEntity(sound, Filter.Local(), uid, false, param)?.Entity;
            if (stream == null)
                return null;
        }

        _audio.SetVolume(stream, baseParams.Volume + SharedAudioSystem.GainToVolume(MathF.Max(gain, SilentGain)));

        // The engine applies pitch only when a source starts and never resets it, so a live change holds. The
        // component's own setter is locked to SharedAudioSystem; the source interface it implements is not.
        if (TryComp(stream, out AudioComponent? audio))
            ((IAudioSource) audio).Pitch = pitch * baseParams.Pitch;

        return stream;
    }

    private static float Lerp(Vector2 range, float t)
    {
        return range.X + (range.Y - range.X) * t;
    }

    private void PlayOnce(SoundSpecifier? sound, EntityUid uid, AudioParams param)
    {
        if (sound != null)
            _audio.PlayEntity(sound, Filter.Local(), uid, false, param.WithLoop(false));
    }

    private void StopLoops(WFVehicleEngineAudioComponent engine)
    {
        engine.IdleStream = Silence(engine.IdleStream);
        engine.DriveStream = Silence(engine.DriveStream);
        engine.TracksStream = Silence(engine.TracksStream);
        engine.PivotStream = Silence(engine.PivotStream);
        engine.HighStream = Silence(engine.HighStream);
    }

    /// <summary>
    /// Deletes a local loop outright. The audio system's Stop is refused while server states apply, and a
    /// server delete of the vehicle first parks its loops in nullspace, where they would play on over the lobby.
    /// </summary>
    private EntityUid? Silence(EntityUid? stream)
    {
        if (stream is { } uid && !TerminatingOrDeleted(uid))
            QueueDel(uid);
        return null;
    }
}
