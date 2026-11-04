using Robust.Shared.Audio;

namespace Content.Shared._WF.Vehicles.Audio;

/// <summary>Client-side traverse sounds for a vehicle turret: a start clunk, a motor loop while it turns and a brake.</summary>
[RegisterComponent]
public sealed partial class WFTurretTraverseAudioComponent : Component
{
    [DataField]
    public SoundSpecifier? StartSound;

    [DataField]
    public SoundSpecifier? LoopSound;

    [DataField]
    public SoundSpecifier? StopSound;

    /// <summary>Seconds without movement before the turret counts as stopped.</summary>
    [DataField]
    public float StopGrace = 0.15f;

    [DataField]
    public AudioParams Params = AudioParams.Default.WithVolume(4f).WithMaxDistance(16f);

    [ViewVariables]
    public Angle LastRotation;

    [ViewVariables]
    public bool Turning;

    [ViewVariables]
    public float StillTime;

    [ViewVariables]
    public EntityUid? LoopStream;
}
