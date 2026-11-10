namespace Content.Server._WF.ShipShields;

/// <summary>Limits impact audio independently for each protected hull.</summary>
[RegisterComponent]
public sealed partial class WFShipShieldImpactAudioComponent : Component
{
    /// <summary>Shared startup/shutdown cooldown, retained across generator or field replacement.</summary>
    public TimeSpan NextPowerSound;

    /// <summary>Earliest time another impact sound may start.</summary>
    public TimeSpan NextImpactSound;

    /// <summary>Current echo, retained across shield replacement.</summary>
    public EntityUid? ActiveImpactSound;

    /// <summary>Older echo, stopped when the next impact starts to bound overlapping audio.</summary>
    public EntityUid? PreviousImpactSound;
}
