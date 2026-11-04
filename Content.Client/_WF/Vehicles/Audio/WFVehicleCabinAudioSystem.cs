using System.Numerics;
using Content.Goobstation.Client.Audio;
using Content.Shared._WF.Vehicles.Core;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._WF.Vehicles.Audio;

/// <summary>
/// Inside a vehicle's cabin you still hear the world outside, through the hull: every sound near the vehicle plays to
/// you again, quieter and dulled, its volume following how far it is from the hull. Someone looking out (the driver,
/// a gunner, a viewport) already hears the outside as it is.
/// </summary>
public sealed class WFVehicleCabinAudioSystem : EntitySystem
{
    [Dependency] private AudioEffectSystem _effects = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How much quieter the outside is through the hull, in decibels.</summary>
    private const float HullDampening = -9f;

    /// <summary>The dulling laid over what comes through the hull.</summary>
    private const string Muffle = "Underwater";

    /// <summary>The furthest a sound can be from the hull and still be heard inside, in metres.</summary>
    private const float Reach = 24f;

    private readonly Dictionary<EntityUid, EntityUid> _mirrors = new();
    private readonly List<EntityUid> _gone = new();
    private readonly List<(EntityUid, AudioComponent, TransformComponent)> _heard = new();

    public override void Shutdown()
    {
        base.Shutdown();
        StopAll();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } local
            || !TryComp(local, out VehicleInteriorOccupantComponent? occupant)
            || !TryComp(occupant.Vehicle, out TransformComponent? vehicleXform)
            || vehicleXform.MapID == _eye.CurrentEye.Position.MapId)
        {
            StopAll();
            return;
        }

        var hull = _transform.GetWorldPosition(vehicleXform);
        // Gathered first: starting a copy adds an audio entity, which the query can't take mid-walk.
        _heard.Clear();
        var sources = EntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (sources.MoveNext(out var uid, out var audio, out var xform))
        {
            if (!audio.Global && xform.MapID == vehicleXform.MapID && audio.State == AudioState.Playing
                && !string.IsNullOrEmpty(audio.FileName))
                _heard.Add((uid, audio, xform));
        }

        foreach (var (uid, audio, xform) in _heard)
        {

            var range = MathF.Min(Reach, audio.Params.MaxDistance);
            var distance = Vector2.Distance(_transform.GetWorldPosition(xform), hull);
            if (distance > range)
                continue;

            var volume = audio.Params.Volume + HullDampening + SharedAudioSystem.GainToVolume(MathF.Max(0.05f, 1f - distance / range));
            if (_mirrors.TryGetValue(uid, out var mirror) && Exists(mirror))
            {
                _audio.SetVolume(mirror, volume);
                continue;
            }

            var sourceParams = audio.Params;
            var copy = _audio.PlayGlobal(audio.FileName, Filter.Local(), false,
                sourceParams.WithVolume(volume).WithPlayOffset(audio.PlaybackPosition));
            if (copy is not { } played)
                continue;
            _effects.TryAddEffect((played.Entity, played.Component), Muffle);
            _mirrors[uid] = played.Entity;
        }

        // A copy goes when its sound stops, drifts out of earshot or leaves the vehicle's map.
        _gone.Clear();
        foreach (var (source, mirror) in _mirrors)
        {
            if (!TryComp(source, out AudioComponent? audio) || audio.State != AudioState.Playing
                || !TryComp(source, out TransformComponent? xform) || xform.MapID != vehicleXform.MapID
                || Vector2.Distance(_transform.GetWorldPosition(xform), hull) > MathF.Min(Reach, audio.Params.MaxDistance)
                || !Exists(mirror))
                _gone.Add(source);
        }
        foreach (var source in _gone)
        {
            if (_mirrors.Remove(source, out var mirror))
                _audio.Stop(mirror);
        }
    }

    private void StopAll()
    {
        foreach (var mirror in _mirrors.Values)
            _audio.Stop(mirror);
        _mirrors.Clear();
    }
}
