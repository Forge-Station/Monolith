using Content.Shared._WF.Lattice;
using Content.Shared.EntityEffects;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>Drug families. Dependence, withdrawal and the screen overlay key on these.</summary>
[Serializable, NetSerializable]
public enum WFDrugFamily : byte
{
    Stimulant,
    Opioid,
    Psychedelic,
    Empathogen,
    Cannabinoid,
    Combat,
    Nicotine,
}

/// <summary>Special behaviours a tier can switch on, handled by the systems that own them.</summary>
[Serializable, NetSerializable]
public enum WFDrugTrait : byte
{
    /// <summary>Damage doesn't slow movement.</summary>
    IgnoreDamageSlowdown,
    /// <summary>Stuns and knockdowns last half as long.</summary>
    StunResistance,
    /// <summary>Stamina damage can't put the user into stamina crit.</summary>
    StaminaCritResistance,
    /// <summary>The user's own damage readout is hidden.</summary>
    HideDamage,
    /// <summary>Incoming Brute and Burn is reduced and the avoided damage is repaid during the crash.</summary>
    Damper,
    /// <summary>A light on the screen points to the nearest rock holding exotic matter, and Drift builds faster.</summary>
    ExoticSense,
}

/// <summary>
/// One drug's effect profile. Reagent doses and neuroware programs both raise its level, and
/// <c>WFIntoxicationSystem</c> applies the tier that level reaches.
/// </summary>
[Prototype("wfDrugProfile")]
public sealed partial class WFDrugProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public WFDrugFamily Family;

    /// <summary>Level lost per second, for level a neuroware program feeds. Level from the blood follows the blood.</summary>
    [DataField]
    public float Decay = 0.1f;

    /// <summary>
    /// Seconds for the drug in the blood above the overdose tier to halve, as the body flushes it, so a big overdose
    /// doesn't last as long as its units would at the reagent's metabolism rate. Zero disables it.
    /// </summary>
    [DataField]
    public float OverdoseHalfLife;

    /// <summary>
    /// Extra level per level a fresh dose adds to the blood, which fades at <see cref="RushHalfLife"/>: the peak
    /// before the high settles. It can deepen a high up to the overdose tier but never into it. Zero gives no rush.
    /// </summary>
    [DataField]
    public float RushGain;

    /// <summary>Seconds for the rush to halve.</summary>
    [DataField]
    public float RushHalfLife = 45f;

    /// <summary>Tiers in ascending <see cref="WFDrugTier.Min"/>. The last tier is the overdose.</summary>
    [DataField(required: true)]
    public List<WFDrugTier> Tiers = new();

    /// <summary>Applied when the level falls below the first tier after enough time above it.</summary>
    [DataField]
    public WFDrugCrash? Crash;

    /// <summary>Dependence added per unit of level gained.</summary>
    [DataField]
    public float DependenceGain;

    /// <summary>The drug is legal, so a stick written with its twin carries no narcotic marks.</summary>
    [DataField]
    public bool Legal;

    /// <summary>The neuroware twin of this drug: its display strings and the size of a load.</summary>
    [DataField]
    public WFNeurowareStrings? Neuroware;

    /// <summary>The index of the tier a level reaches, or -1 below the first.</summary>
    public int TierAt(float level)
    {
        for (var i = Tiers.Count - 1; i >= 0; i--)
        {
            if (level >= Tiers[i].Min)
                return i;
        }

        return -1;
    }

    /// <summary>The phase a tier belongs to: the first is light, the last is the overdose, any between are heavy.</summary>
    public WFDrugPhase PhaseOf(int tier)
    {
        if (tier < 0)
            return WFDrugPhase.None;

        if (Tiers.Count > 1 && tier == Tiers.Count - 1)
            return WFDrugPhase.Overdose;

        return tier == 0 ? WFDrugPhase.Light : WFDrugPhase.Heavy;
    }
}

/// <summary>How deep a high is, for the screen look and the popups that mark each step.</summary>
public enum WFDrugPhase : byte
{
    None,
    Light,
    Heavy,
    Overdose,
}

/// <summary>What a drug does while its level is in one band.</summary>
[DataDefinition]
public sealed partial class WFDrugTier
{
    /// <summary>The lowest level of this tier.</summary>
    [DataField(required: true)]
    public float Min;

    /// <summary>Effects run once a second, with base args. They must not use reagent-only conditions.</summary>
    [DataField(serverOnly: true)]
    public List<EntityEffect> Effects = new();

    /// <summary>Movement speed multiplier.</summary>
    [DataField]
    public float Speed = 1f;

    /// <summary>Status effects kept up while in this tier.</summary>
    [DataField]
    public List<WFDrugStatusEffect> StatusEffects = new();

    /// <summary>Overlay intensity, 0-1.</summary>
    [DataField]
    public float Overlay;

    [DataField]
    public List<WFDrugTrait> Traits = new();

    /// <summary>Chance a second of a Lattice hallucination, for psychedelics.</summary>
    [DataField]
    public float HallucinationChance;

    /// <summary>The Lattice hallucination kinds this tier draws from.</summary>
    [DataField]
    public List<WFLatticeHallucinationKind> Hallucinations = new();
}

/// <summary>The come-down after a high.</summary>
[DataDefinition]
public sealed partial class WFDrugCrash
{
    /// <summary>Seconds above the first tier needed for a crash to follow.</summary>
    [DataField]
    public float After = 30f;

    /// <summary>Base crash length.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(60);

    /// <summary>Extra crash length per second spent above the first tier, so redosing enlarges it.</summary>
    [DataField]
    public float PerSecondAbove;

    /// <summary>Longest crash.</summary>
    [DataField]
    public TimeSpan MaxDuration = TimeSpan.FromMinutes(5);

    [DataField(serverOnly: true)]
    public List<EntityEffect> Effects = new();

    /// <summary>Stamina damage a second, held at <see cref="StaminaCap"/> so the crash wears the body down without knocking it over.</summary>
    [DataField]
    public float Stamina;

    /// <summary>Share of the crit threshold the crash's stamina damage stops at; below half it doesn't jitter.</summary>
    [DataField]
    public float StaminaCap = 0.45f;

    /// <summary>A single knockdown as the crash starts.</summary>
    [DataField]
    public TimeSpan Collapse;

    [DataField]
    public float Speed = 1f;

    [DataField]
    public List<WFDrugStatusEffect> StatusEffects = new();

    [DataField]
    public float Overlay = 0.5f;
}

/// <summary>A status effect kept up for as long as a tier or crash lasts.</summary>
[DataDefinition]
public sealed partial class WFDrugStatusEffect
{
    [DataField(required: true)]
    public ProtoId<StatusEffectPrototype> Key;

    /// <summary>The component the status effect adds, if any.</summary>
    [DataField]
    public string? Component;
}

/// <summary>How a drug's neuroware twin is named, dosed and shown on a synthetic's HUD.</summary>
[DataDefinition]
public sealed partial class WFNeurowareStrings
{
    /// <summary>The program's name.</summary>
    [DataField(required: true)]
    public LocId Program;

    /// <summary>Fault lines shown while the program runs.</summary>
    [DataField]
    public List<LocId> Faults = new();

    /// <summary>Shown when the program loads.</summary>
    [DataField]
    public LocId? Banner;

    /// <summary>Level a full load delivers.</summary>
    [DataField]
    public float Amount = 8f;
}
