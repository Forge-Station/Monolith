using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.ShipPa;

/// <summary>
/// A physical PA loudspeaker. Every speaker anchored to a grid is part of that ship's PA network;
/// there is nothing to wire or configure.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipPaSpeakerComponent : Component
{
    /// <summary>Server-authoritative membership, anchoring, power and breakage combined.</summary>
    [DataField, AutoNetworkedField] public bool Enabled;

    /// <summary>Tiles a stream from this speaker is audible over (AudioParams.MaxDistance).</summary>
    [DataField] public float Range = 14f;

    /// <summary>dB added to every stream this speaker plays.</summary>
    [DataField] public float Volume = 0f;

    /// <summary>Total damage at which distortion reaches 1. Keep equal to the YAML Breakage threshold.</summary>
    [DataField] public FixedPoint2 DistortionDamage = 60;

    /// <summary>Looped quietly under alarms while the speaker is damaged.</summary>
    [DataField] public SoundSpecifier? StaticSound = new SoundPathSpecifier("/Audio/_WF/ShipPa/speaker_static.ogg");

    /// <summary>
    /// Only carries the PA while the ship has no dedicated speaker at all. Air alarms set this.
    /// </summary>
    [DataField] public bool Fallback;

    /// <summary>0..1 from damage. Server computed.</summary>
    [DataField, AutoNetworkedField] public float Distortion;

    /// <summary>Breakage threshold reached; silent until damage returns to 0.</summary>
    [DataField, AutoNetworkedField] public bool Broken;

    /// <summary>Server only: when the current one-shot broadcast on this speaker ends (for the visual state).</summary>
    [ViewVariables] public TimeSpan? BroadcastingUntil;
}
