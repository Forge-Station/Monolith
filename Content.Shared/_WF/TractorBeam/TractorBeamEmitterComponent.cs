using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.TractorBeam;

/// <summary>A powered, ship-mounted tether. Forces act on the two grids, never on the anchored dish.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TractorBeamEmitterComponent : Component
{
    [DataField] public float MaxRange = 200f;
    /// <summary>Visual fan width relative to the target hull; does not change the operating cone.</summary>
    [DataField] public float VisualWidthScale = 1f;
    /// <summary>Electrical strain to sustain a stationary capture at maximum range.</summary>
    [DataField] public float RangeStrainAtMaxRange = 0.6f;
    /// <summary>Forward operating half-angle in radians, measured about the dish's local +Y axis.</summary>
    [DataField] public float ConeHalfAngle = TractorBeamOperatingCone.DefaultHalfAngle;
    [DataField] public float MaxForce = 10000f;
    [DataField] public float IdlePower = 5000f;
    [DataField] public float HoldingPower = 100000f;
    [DataField] public float MaxPower = 2000000f;
    /// <summary>Seconds continuously at maximum displayed strain before the capture fails.</summary>
    [DataField] public float OverloadDuration = 2f;
    [DataField] public float RestartCooldown = 12f;
    [DataField] public float Frequency = 0.7f;
    [DataField] public float DampingRatio = 1f;
    [DataField] public float ReelSpeed = 2f;
    [DataField] public float CollectionAcceleration = 2f;
    /// <summary>Extra field acceleration for free-floating items and mobs, excluding ship grids.</summary>
    [DataField] public float LooseCollectionMultiplier = 50f;
    [DataField] public float CollectionStandOff = 1f;
    // User-selected replacement clips retain their original mix, with encoding headroom in the assets.
    [DataField] public SoundSpecifier? EngageSound = new SoundPathSpecifier("/Audio/_WF/TractorBeam/tractorbeam_engage.ogg");
    // -6.0206 dB halves the loop's linear playback gain.
    [DataField] public SoundSpecifier? LoopSound = new SoundPathSpecifier("/Audio/_WF/TractorBeam/tractorbeam_loop.ogg", AudioParams.Default.WithVolume(-6.0206f));
    [DataField] public SoundSpecifier? DisengageSound = new SoundPathSpecifier("/Audio/_WF/TractorBeam/tractorbeam_disengage.ogg");
    [DataField] public SoundSpecifier[] CreakSounds =
    [
        new SoundPathSpecifier("/Audio/_WF/TractorBeam/creak1.ogg", AudioParams.Default.WithVolume(0f)),
        new SoundPathSpecifier("/Audio/_WF/TractorBeam/creak2.ogg", AudioParams.Default.WithVolume(-6f)),
        new SoundPathSpecifier("/Audio/_WF/TractorBeam/creak3.ogg", AudioParams.Default.WithVolume(4f)),
    ];
    /// <summary>Attempted translation or rotation must demand this fraction of the force budget before hull creaks play.</summary>
    [DataField] public float CreakForceFraction = 0.001f;
    [DataField] public float CreakInitialMinimumDelay = 2f;
    [DataField] public float CreakInitialMaximumDelay = 4f;
    [DataField] public float CreakMinimumInterval = 4f;
    [DataField] public float CreakMaximumInterval = 8f;

    [AutoNetworkedField] public EntityUid? Target;
    [AutoNetworkedField] public Vector2 TargetOffset;
    [AutoNetworkedField] public float Strain;
    [AutoNetworkedField] public bool Active;
    [AutoNetworkedField] public bool Pulling;
    [AutoNetworkedField] public bool LockedInPlace;

    // Runtime locks are deliberately not serialized into maps.
    public EntityUid? SourceGrid;
    public EntityUid? Controller;
    public EntityUid? Visual;
    public float HoldDistance;
    /// <summary>Requested center-to-center distance for a controlled inward reel; retained on arrival.</summary>
    public float? RequestedDistance;
    // Bearing at capture; rotate by the source's change in orientation to form the rigid arm.
    public Vector2 HoldDirection;
    public float? HoldSourceAngle;
    // Relative orientation captured once, shared by normal Hold and Pull modes.
    public float? HoldAngle;
    public Vector2 LockedSeparation;
    public float? LockedSourceAngle;
    public float LockedAngle;
    public float RequestedPower;
    public float RequiredForce;
    // Electrical field-maintenance load, separate from resistance so distance does not trigger creaks.
    public float DistanceStrain;
    public float OverloadTime;
    public float CooldownRemaining;
    public TimeSpan PowerGraceUntil;
}
