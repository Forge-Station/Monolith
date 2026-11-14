using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Plays speed-scaled wind and fall rumble to everyone aboard a hull flying in atmosphere.</summary>
public sealed partial class WFFlightAmbienceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFGridAudienceSystem _audience = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;

    /// <summary>Wind volume (dB) on a hull that is barely moving.</summary>
    public const float WindMinVolume = -9f;

    /// <summary>Wind volume (dB) at <see cref="WindMaxSpeed"/> and above.</summary>
    public const float WindMaxVolume = 1f;

    /// <summary>Planar speed (m/s) at which the wind is loudest and highest.</summary>
    public const float WindMaxSpeed = 14f;

    /// <summary>Rumble volume (dB) at free-fall landing speed.</summary>
    public const float RumbleMaxVolume = -3f;

    // dB, at the moment lift is lost.
    private const float RumbleMinVolume = -20f;

    private const float WindMinPitch = 0.8f;
    private const float WindMaxPitch = 1.25f;

    // Quantised so a hull under thrust isn't restarting its loop every sweep.
    private const float WindPitchStep = 0.05f;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ReissueInterval = TimeSpan.FromSeconds(30);

    /// <summary>Looping wind heard while flying in atmosphere.</summary>
    public static readonly SoundSpecifier WindSound =
        new SoundPathSpecifier("/Audio/_WF/Planets/Flight/atmo_wind.ogg");

    /// <summary>Looping airframe rumble heard while falling.</summary>
    public static readonly SoundSpecifier RumbleSound =
        new SoundPathSpecifier("/Audio/_WF/Planets/Flight/fall_rumble.ogg");

    private readonly List<EntityUid> _scan = new();

    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFFlightAmbienceComponent, ComponentShutdown>(OnShutdown);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;

        _scan.Clear();

        var grids = EntityQueryEnumerator<MapGridComponent>();
        while (grids.MoveNext(out var uid, out _))
        {
            // A z-layer map is itself a grid, never a hull in flight.
            if (HasComp<MapComponent>(uid))
                continue;

            _scan.Add(uid);
        }

        foreach (var grid in _scan)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            if (InAtmosphere(grid))
                UpdateAmbience(grid);
            else
                StopAmbience(grid);
        }
    }

    /// <summary>True for a grid on any layer or gap below orbit, excluding the ground layer.</summary>
    public bool InAtmosphere(EntityUid grid)
    {
        if (Transform(grid).MapUid is not { } map)
            return false;

        if (HasComp<WFOrbitLayerComponent>(map))
            return false;

        // A gap is always flight.
        if (!HasComp<CEZTransitMapComponent>(map) && IsGroundLayer(map))
            return false;

        return _zLevels.WfIsPlanetFlight(grid);
    }

    private bool IsGroundLayer(EntityUid map)
    {
        return HasComp<CEZGroundLayerComponent>(map)
               || (TryComp<CEZMapComponent>(map, out var zMap) && zMap.Depth == 0);
    }

    /// <summary>Updates one hull's wind and rumble to its speed and fall.</summary>
    private void UpdateAmbience(EntityUid grid)
    {
        var comp = EnsureComp<WFFlightAmbienceComponent>(grid);

        var reissue = _timing.CurTime >= comp.NextReissue;

        if (reissue)
            comp.NextReissue = _timing.CurTime + ReissueInterval;

        var speed = TryComp<PhysicsComponent>(grid, out var body) ? body.LinearVelocity.Length() : 0f;
        var wind = Math.Clamp(speed / WindMaxSpeed, 0f, 1f);
        var pitch = MathF.Round((WindMinPitch + (WindMaxPitch - WindMinPitch) * wind) / WindPitchStep) * WindPitchStep;
        var volume = WindMinVolume + (WindMaxVolume - WindMinVolume) * wind;

        if (reissue || !Alive(comp.Wind) || !MathHelper.CloseTo(pitch, comp.WindPitch, 0.001f))
        {
            comp.WindPitch = pitch;
            comp.Wind = Replay(comp.Wind, WindSound, grid, volume, pitch);
        }
        else
        {
            _audio.SetVolume(comp.Wind, volume);
        }

        if (!HasComp<WFLiftLostComponent>(grid))
        {
            comp.Rumble = _audio.Stop(comp.Rumble);
            return;
        }

        var fall = 0f;

        if (TryComp<CEZGridFallerComponent>(grid, out var faller))
        {
            var reference = _zLevels.WfGetFreeFallSpeed(faller);

            if (reference > 0f)
                fall = Math.Clamp(faller.Velocity / reference, 0f, 1f);
        }

        var rumble = RumbleMinVolume + (RumbleMaxVolume - RumbleMinVolume) * fall;

        if (reissue || !Alive(comp.Rumble))
            comp.Rumble = Replay(comp.Rumble, RumbleSound, grid, rumble, 1f);
        else
            _audio.SetVolume(comp.Rumble, rumble);
    }

    /// <summary>Stops both loops by removing the component.</summary>
    public void StopAmbience(EntityUid grid)
    {
        if (HasComp<WFFlightAmbienceComponent>(grid))
            RemComp<WFFlightAmbienceComponent>(grid);
    }

    private void OnShutdown(Entity<WFFlightAmbienceComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Wind = _audio.Stop(ent.Comp.Wind);
        ent.Comp.Rumble = _audio.Stop(ent.Comp.Rumble);
    }

    /// <summary>Restarts a loop for everyone aboard now.</summary>
    // PlayGlobal to the crew, not PlayPvs: PVS audio sits at the grid origin with a 15-tile range.
    private EntityUid? Replay(EntityUid? stream, SoundSpecifier sound, EntityUid grid, float volume, float pitch)
    {
        _audio.Stop(stream);

        return _audio.PlayGlobal(
            sound,
            _audience.Aboard(grid),
            true,
            AudioParams.Default.WithLoop(true).WithVolume(volume).WithPitchScale(pitch))?.Entity;
    }

    private bool Alive(EntityUid? stream)
    {
        return stream is { } uid && !TerminatingOrDeleted(uid);
    }
}
