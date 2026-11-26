using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>What the chest is doing, as examine and the analyzer read it. Set by the server's life tick.</summary>
[Serializable, NetSerializable]
public enum WolfmedBreathing : byte
{
    Normal = 0,

    /// <summary>Breathing, but slowed by sedation past its depression line.</summary>
    Depressed = 1,

    /// <summary>Trying to breathe and getting nothing: the respirator is suffocating.</summary>
    Gasping = 2,

    /// <summary>Not breathing at all: arrest, death, no brain, or no lungs.</summary>
    None = 3,

    /// <summary>M3: breathing, but damaged lungs cannot keep up (plan §3.3). "Short of breath".</summary>
    Laboured = 4,
}

/// <summary>Why breathing is not <see cref="WolfmedBreathing.Normal"/>.</summary>
[Serializable, NetSerializable]
public enum WolfmedBreathingSource : byte
{
    None = 0,

    /// <summary>Nothing breathable reaches the lungs: vacuum, a bad mix, an empty tank.</summary>
    NoAir = 1,

    /// <summary>No working lungs left in the body.</summary>
    Lungs = 2,

    Sedation = 3,

    /// <summary>The heart has stopped, and the chest with it.</summary>
    Arrest = 4,

    NoBrain = 5,

    Dead = 6,

    /// <summary>M3: the lungs are there but under their impaired line.</summary>
    LungsDamaged = 7,
}

/// <summary>Circulation in the words a medic uses, from blood volume. Set by the server's life tick.</summary>
[Serializable, NetSerializable]
public enum WolfmedBloodBand : byte
{
    /// <summary>Above wolfmed.blood_band_pale: pulse strong.</summary>
    Normal = 0,

    /// <summary>Down to the Downed line: pale.</summary>
    Low = 1,

    /// <summary>Down to the Unconscious line: pale and clammy, weak rapid pulse.</summary>
    Weak = 2,

    /// <summary>At or under the Unconscious line: barely palpable.</summary>
    Critical = 3,

    /// <summary>Arrest or dead: no pulse.</summary>
    None = 4,
}
