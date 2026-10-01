using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Gore;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Which noise a wound makes, what a limb coming off sounds and looks like, and what a hit throws off the
/// body. One shipped profile (<see cref="WolfmedWoundSfxSystem.DefaultProfile"/>); the prototype exists so
/// the whole feedback layer can be retuned or swapped for other audio without a code change.
/// </summary>
[Prototype("wolfmedSfxProfile")]
public sealed partial class WolfmedSfxProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Wound sounds, most specific first. The first entry whose filters match the wound wins, so a
    /// fracture entry listed above the blunt entry is what a breaking bone plays.
    /// </summary>
    [DataField]
    public List<WolfmedWoundSoundEntry> WoundSounds = new();

    /// <summary>Severity a wound has to reach before it is worth a sound at all.</summary>
    [DataField]
    public FixedPoint2 MinSeverity = FixedPoint2.New(5);

    /// <summary>
    /// Shortest gap between two wound sounds on one body. Buckshot lands nine pellets in one tick and a
    /// ticking wound worsens every few seconds; this is what keeps either from turning into a drum roll.
    /// </summary>
    [DataField]
    public TimeSpan SoundInterval = TimeSpan.FromSeconds(0.4);

    /// <summary>Shortest gap between two debris effects on one body.</summary>
    [DataField]
    public TimeSpan DebrisInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>A limb torn off a body of flesh.</summary>
    [DataField]
    public WolfmedDismembermentSpec OrganicDismemberment = new();

    /// <summary>A limb sheared off a chassis.</summary>
    [DataField]
    public WolfmedDismembermentSpec MechanicalDismemberment = new();

    /// <summary>Blood mist tiers, coarsest first. The highest tier the hit clears is spawned.</summary>
    [DataField]
    public List<WolfmedDebrisTier> OrganicDebris = new();

    /// <summary>Spark tiers for a chassis.</summary>
    [DataField]
    public List<WolfmedDebrisTier> MechanicalDebris = new();

    /// <summary>G1: the travelling spray and the splat it leaves. Organic bodies only.</summary>
    [DataField]
    public WolfmedHitSplatterSpec HitSplatter = new();

    /// <summary>G2: what an arterial bleed or an open stump throws, and how often.</summary>
    [DataField]
    public WolfmedBleedSpurtSpec BleedSpurt = new();

    /// <summary>Sparks off damaged or disabled machine parts.</summary>
    [DataField]
    public WolfmedMachineSparkSpec MachineSparks = new();

    /// <summary>The sound this wound makes, or null when the profile has nothing to say about it.</summary>
    public SoundSpecifier? GetWoundSound(
        ProtoId<WoundPrototype> wound,
        WoundPrototype? prototype,
        bool organic,
        FixedPoint2 severity)
    {
        foreach (var entry in WoundSounds)
        {
            if (entry.Matches(wound, prototype, organic, severity))
                return entry.Sound;
        }

        return null;
    }

    /// <summary>The debris effect a hit of this severity throws, or null below the smallest tier.</summary>
    public EntProtoId? GetDebris(bool organic, FixedPoint2 severity)
    {
        foreach (var tier in organic ? OrganicDebris : MechanicalDebris)
        {
            if (severity >= tier.MinSeverity)
                return tier.Effect;
        }

        return null;
    }
}

/// <summary>Which kind of body part an entry answers for.</summary>
public enum WolfmedSfxTissue : byte
{
    Any,
    Organic,
    Mechanical,
}

/// <summary>
/// One wound-sound rule. Every filter left empty matches everything, so a bare entry with a sound is the
/// fallback for whatever reaches it.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedWoundSoundEntry
{
    /// <summary>
    /// The sound, or none: an entry without one claims its wounds and keeps them silent, for a wound another system
    /// voices (a fracture's crack comes from <c>WolfmedBodySoundSystem</c>).
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>Wound prototypes this entry answers for.</summary>
    [DataField]
    public HashSet<ProtoId<WoundPrototype>> Wounds = new();

    /// <summary>
    /// Damage types this entry answers for, matched against the wound prototype's own
    /// <see cref="WoundPrototype.DamageTypes"/>. Keys the entry to a cause without naming every wound.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<DamageTypePrototype>> DamageTypes = new();

    /// <summary>Flesh, chassis, or either.</summary>
    [DataField]
    public WolfmedSfxTissue Tissue = WolfmedSfxTissue.Any;

    /// <summary>Severity floor on top of the profile's own.</summary>
    [DataField]
    public FixedPoint2 MinSeverity = FixedPoint2.Zero;

    /// <summary>Whether this entry's filters accept the wound.</summary>
    public bool Matches(
        ProtoId<WoundPrototype> wound,
        WoundPrototype? prototype,
        bool organic,
        FixedPoint2 severity)
    {
        if (severity < MinSeverity)
            return false;

        if (Tissue != WolfmedSfxTissue.Any && organic != (Tissue == WolfmedSfxTissue.Organic))
            return false;

        if (Wounds.Count > 0 && !Wounds.Contains(wound))
            return false;

        if (DamageTypes.Count == 0)
            return true;

        if (prototype == null)
            return false;

        foreach (var type in DamageTypes)
        {
            if (prototype.DamageTypes.ContainsKey(type))
                return true;
        }

        return false;
    }
}

/// <summary>What a limb coming off sounds and looks like.</summary>
[DataDefinition]
public sealed partial class WolfmedDismembermentSpec
{
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>Spawned at the body. Sparks for a chassis; flesh gets the puddle instead.</summary>
    [DataField]
    public EntProtoId? Effect;

    /// <summary>
    /// Units of the body's own blood reagent spilled where the limb was. Hydraulic fluid for an IPC, because the
    /// reagent is read off the bloodstream rather than named here.
    /// </summary>
    [DataField]
    public FixedPoint2 SpillVolume = FixedPoint2.Zero;
}

/// <summary>One severity band of hit debris.</summary>
[DataDefinition]
public sealed partial class WolfmedDebrisTier
{
    [DataField(required: true)]
    public EntProtoId Effect;

    [DataField]
    public FixedPoint2 MinSeverity = FixedPoint2.Zero;
}
