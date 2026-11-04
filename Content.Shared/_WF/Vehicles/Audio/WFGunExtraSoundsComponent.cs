using Robust.Shared.Audio;

namespace Content.Shared._WF.Vehicles.Audio;

/// <summary>Extra sounds for a vehicle gun: a far boom for distant listeners and a timed loading sequence after each shot.</summary>
[RegisterComponent]
public sealed partial class WFGunExtraSoundsComponent : Component
{
    /// <summary>Heard by players between <see cref="DistantMinRange"/> and <see cref="DistantRange"/> tiles away.</summary>
    [DataField]
    public SoundSpecifier? DistantSound;

    [DataField]
    public float DistantMinRange = 12f;

    [DataField]
    public float DistantRange = 64f;

    /// <summary>Sounds played after each shot, each at its delay; a new shot restarts the sequence.</summary>
    [DataField]
    public List<WFTimedSound> LoadingSequence = new();

    [ViewVariables]
    public TimeSpan? LastShot;

    [ViewVariables]
    public int NextStep;
}

/// <summary>A chance of a ricochet whine where a round hits something that is not a living creature.</summary>
[RegisterComponent]
public sealed partial class WFImpactRicochetComponent : Component
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField]
    public float Chance = 0.4f;
}

/// <summary>Sounds played in order once a vehicle's frame is destroyed: the blast, debris, cook-off and fire.</summary>
[RegisterComponent]
public sealed partial class WFVehicleDestroyedSoundComponent : Component
{
    [DataField]
    public List<WFTimedSound> Sequence = new();

    [ViewVariables]
    public TimeSpan? DestroyedAt;

    [ViewVariables]
    public int NextStep;
}

/// <summary>A sound played a set time after some moment.</summary>
[DataDefinition]
public sealed partial class WFTimedSound
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    /// <summary>Seconds after the moment.</summary>
    [DataField]
    public float Delay;
}

/// <summary>Hull hit sounds for a vehicle, picked by the damage of the hit, with a chance of a ricochet on top.</summary>
[RegisterComponent]
public sealed partial class WFVehicleHitSoundComponent : Component
{
    [DataField]
    public SoundSpecifier? LightHitSound;

    [DataField]
    public SoundSpecifier? HeavyHitSound;

    [DataField]
    public SoundSpecifier? LightRicochetSound;

    [DataField]
    public SoundSpecifier? HeavyRicochetSound;

    [DataField]
    public float LightRicochetChance = 0.5f;

    [DataField]
    public float HeavyRicochetChance = 0.3f;

    /// <summary>Damage from one hit at or above which the heavy sounds play.</summary>
    [DataField]
    public float HeavyThreshold = 40f;

    [DataField]
    public float Cooldown = 0.08f;

    [ViewVariables]
    public TimeSpan NextHit;
}
