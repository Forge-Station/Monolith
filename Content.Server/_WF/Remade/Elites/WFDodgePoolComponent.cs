using Robust.Shared.Audio;

namespace Content.Server._WF.Remade.Elites;

/// <summary>
/// The twitcher's evasion: each bullet that would hit it slips past with a chance equal to the pool, and some slips cost
/// the pool and throw the twitcher a step aside. The pool refills slowly. Numbers from DS13's twitcher.
/// </summary>
[RegisterComponent]
public sealed partial class WFDodgePoolComponent : Component
{
    [DataField]
    public float Pool = 100f;

    [DataField]
    public float Max = 100f;

    /// <summary>The chance a dodge costs the pool.</summary>
    [DataField]
    public float DrainChance = 0.55f;

    /// <summary>What a costly dodge takes off the pool.</summary>
    [DataField]
    public float Usage = 8f;

    /// <summary>Pool it gets back each second.</summary>
    [DataField]
    public float Regen = 0.6f;

    /// <summary>Tiles a costly dodge throws it aside.</summary>
    [DataField]
    public float Blink = 1.6f;

    [DataField]
    public SoundSpecifier? Sound = new SoundCollectionSpecifier("BulletMiss");

    /// <summary>Bullets it has dodged.</summary>
    [ViewVariables]
    public int Dodged;
}
