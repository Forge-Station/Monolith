using Robust.Shared.Audio;

namespace Content.Server._WF.Remade.Elites;

/// <summary>A Remade that heals as it walks and grows its lost limbs back, one at a time. The hunter and the Undying.</summary>
[RegisterComponent]
public sealed partial class WFRegenComponent : Component
{
    /// <summary>Health it heals each second.</summary>
    [DataField]
    public float HealPerSecond = 2f;

    /// <summary>Burns heal at this share of the rate, so fire stays the answer.</summary>
    [DataField]
    public float HeatFactor = 0.33f;

    /// <summary>Seconds between one limb growing back and the next.</summary>
    [DataField]
    public float LimbInterval = 25f;

    /// <summary>Seconds a limb shows growing back before it is whole, the length of its regrowth art. 0 means it just appears.</summary>
    [DataField]
    public float GrowSeconds = 8.6f;

    [DataField]
    public SoundSpecifier? Sound;

    [ViewVariables]
    public TimeSpan NextLimb;

    /// <summary>The limb growing back now, if one is.</summary>
    [ViewVariables]
    public string? Sprouting;

    [ViewVariables]
    public TimeSpan NextHeal;

    /// <summary>Limbs it has grown back.</summary>
    [ViewVariables]
    public int Grown;
}
