namespace Content.Server._WF.Planets.Flight;

/// <summary>Wind and fall-rumble loops played to everyone aboard a hull flying below orbit.</summary>
[RegisterComponent]
public sealed partial class WFFlightAmbienceComponent : Component
{
    /// <summary>Wind loop, playing while the hull is airborne below orbit.</summary>
    [DataField]
    public EntityUid? Wind;

    /// <summary>Airframe rumble loop, playing only while the hull is falling.</summary>
    [DataField]
    public EntityUid? Rumble;

    /// <summary>Pitch the wind was last started at; a live stream's pitch can't change, only restart.</summary>
    [DataField]
    public float WindPitch;

    /// <summary>When both loops are restarted so late boarders hear them.</summary>
    [DataField]
    public TimeSpan NextReissue;
}
