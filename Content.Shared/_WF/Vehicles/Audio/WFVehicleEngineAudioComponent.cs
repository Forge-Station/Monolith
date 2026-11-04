using System.Numerics;
using Robust.Shared.Audio;

namespace Content.Shared._WF.Vehicles.Audio;

/// <summary>Client-side engine and track loops for a driven vehicle: start, idle, drive and stop, crossfaded and pitched by speed.</summary>
[RegisterComponent]
public sealed partial class WFVehicleEngineAudioComponent : Component
{
    /// <summary>Played once when the ignition starts the engine.</summary>
    [DataField]
    public SoundSpecifier? StartSound;

    /// <summary>Played once when the engine stops.</summary>
    [DataField]
    public SoundSpecifier? StopSound;

    /// <summary>Seconds after the start sound before the loops reach full volume.</summary>
    [DataField]
    public float StartDelay = 4f;

    [DataField]
    public SoundSpecifier? IdleLoop;

    [DataField]
    public SoundSpecifier? DriveLoop;

    /// <summary>Loop that takes over near full revs.</summary>
    [DataField]
    public SoundSpecifier? HighLoop;

    [DataField]
    public SoundSpecifier? TracksLoop;

    /// <summary>Played when pulling away hard from a standstill.</summary>
    [DataField]
    public SoundSpecifier? RevSound;

    [DataField]
    public float RevCooldown = 4f;

    /// <summary>Played on each upshift; see <see cref="Gears"/>.</summary>
    [DataField]
    public SoundSpecifier? ShiftSound;

    [DataField]
    public SoundSpecifier BrakeSound = new SoundPathSpecifier("/Audio/_WF/Vehicles/RCSM/wheels/brake.ogg");

    [DataField]
    public SoundSpecifier SkidSound = new SoundPathSpecifier("/Audio/_WF/Vehicles/RCSM/wheels/skid.ogg");

    public float BrakeCooldown;
    public float SkidCooldown;

    /// <summary>Gears across the speed range; revs climb through each and drop on a shift. Zero holds revs to speed.</summary>
    [DataField]
    public int Gears;

    /// <summary>Least time in a gear before the next shift, so quick acceleration still revs out each gear.</summary>
    [DataField]
    public float ShiftTime = 0.8f;

    /// <summary>Extra revs at full acceleration.</summary>
    [DataField]
    public float LoadRevs = 0.25f;

    /// <summary>Played when the vehicle comes to rest from at least <see cref="TracksStopMinSpeed"/>.</summary>
    [DataField]
    public SoundSpecifier? TracksStopSound;

    [DataField]
    public float TracksStopMinSpeed = 2f;

    /// <summary>Suspension loop while the hull turns in place.</summary>
    [DataField]
    public SoundSpecifier? PivotLoop;

    /// <summary>Track squeals played now and then while the hull turns in place.</summary>
    [DataField]
    public SoundSpecifier? PivotScreechSound;

    /// <summary>Seconds between squeals, picked at random in this range.</summary>
    [DataField]
    public Vector2 PivotScreechInterval = new(1.2f, 3f);

    /// <summary>Throttle the engine revs to while turning in place at full rate.</summary>
    [DataField]
    public float PivotThrottle = 0.55f;

    /// <summary>Speed treated as full throttle; zero uses the mover's top speed.</summary>
    [DataField]
    public float FullSpeed;

    /// <summary>Idle loop gain left at full throttle.</summary>
    [DataField]
    public float IdleFloor = 0.35f;

    /// <summary>Idle loop pitch at rest and at full throttle.</summary>
    [DataField]
    public Vector2 IdlePitch = new(1f, 1.15f);

    /// <summary>Drive loop pitch at rest and at full throttle.</summary>
    [DataField]
    public Vector2 DrivePitch = new(0.85f, 1.2f);

    /// <summary>High loop pitch from where it fades in to full revs.</summary>
    [DataField]
    public Vector2 HighPitch = new(0.9f, 1.1f);

    /// <summary>Track loop pitch at rest and at full throttle.</summary>
    [DataField]
    public Vector2 TracksPitch = new(0.8f, 1.15f);

    /// <summary>How fast the throttle value follows the real speed, per second.</summary>
    [DataField]
    public float ThrottleResponse = 3f;

    [DataField]
    public AudioParams LoopParams = AudioParams.Default.WithVolume(-2f).WithMaxDistance(20f);

    [ViewVariables]
    public bool EngineOn;

    [ViewVariables]
    public float Throttle;

    [ViewVariables]
    public float Warmup;

    [ViewVariables]
    public float PeakSpeed;

    [ViewVariables]
    public EntityUid? IdleStream;

    [ViewVariables]
    public EntityUid? DriveStream;

    [ViewVariables]
    public EntityUid? TracksStream;

    [ViewVariables]
    public EntityUid? PivotStream;

    [ViewVariables]
    public EntityUid? HighStream;

    [ViewVariables]
    public float LastSpeed;

    [ViewVariables]
    public float Load;

    [ViewVariables]
    public int Gear;

    [ViewVariables]
    public float NextRev;

    [ViewVariables]
    public float SinceShift;

    [ViewVariables]
    public float NextScreech;
}
