using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// A torso has taken damage past its structural cap. D9 never severs a torso, so the tear-off pressure a
/// limb would spend on dismemberment is offered here instead.
/// </summary>
/// <remarks>
/// Broadcast from the one place that pressure is measured, <c>AmputationSystem</c>'s overflow handler, so
/// the vendored file carries a raise and no evisceration logic. <paramref name="Damage"/> is the overflow
/// only: what the cap refused, which is the whole hit once the torso is already at the cap.
/// </remarks>
[ByRefEvent]
public readonly record struct WolfmedTorsoOverflowEvent(
    EntityUid Body,
    EntityUid Part,
    DamageSpecifier Damage,
    bool IsExplosion);

/// <summary>Sits on a torso that has been laid open, naming the wound holding it that way.</summary>
/// <remarks>
/// Also the "one at a time" lock: the system refuses a second evisceration while it is here. It goes away
/// with the wound, whether the closing surgery or a rejuvenate took it.
/// </remarks>
[RegisterComponent]
public sealed partial class WolfmedEviscerationComponent : Component
{
    [DataField]
    public ProtoId<WoundPrototype> Wound;

    /// <summary>
    /// Whether the open-belly state was this system's doing. A medic who had already cut the patient open
    /// keeps their incision when the tear is closed.
    /// </summary>
    [DataField]
    public bool GrantedIncision;

    [DataField]
    public bool GrantedRetraction;
}

/// <summary>
/// What it takes to tear a part open, and what comes out when it happens. Named by
/// <see cref="Content.Shared._WF.Wolfmed.Body.WolfmedBodyPartComponent.EviscerationProfile"/>, so a part
/// that names no profile can never be eviscerated and every number is retuned in YAML.
/// </summary>
[Prototype("wolfmedEviscerationProfile")]
public sealed partial class WolfmedEviscerationProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Per damage type, the overflow one hit has to carry to tear the part open. A type absent from this
    /// table can never do it, which is what keeps Blunt, Piercing and Heat out of it.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> FinishingDamage = new();

    /// <summary>The same table for a hit the routing marked as an explosion. Empty reuses the one above.</summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> ExplosionFinishingDamage = new();

    /// <summary>What a body of flesh loses.</summary>
    [DataField]
    public WolfmedEviscerationSpec Organic = new();

    /// <summary>What a chassis loses: a different wound, different contents and a different noise.</summary>
    [DataField]
    public WolfmedEviscerationSpec Mechanical = new();

    public WolfmedEviscerationSpec Get(bool mechanical) => mechanical ? Mechanical : Organic;

    /// <summary>Whether one hit's overflow clears the bar for the kind of damage it carries.</summary>
    public bool IsFinishingHit(DamageSpecifier overflow, bool explosion)
    {
        var thresholds = explosion && ExplosionFinishingDamage.Count > 0
            ? ExplosionFinishingDamage
            : FinishingDamage;

        foreach (var (type, amount) in overflow.DamageDict)
        {
            if (amount > FixedPoint2.Zero &&
                thresholds.TryGetValue(type, out var minimum) &&
                minimum > FixedPoint2.Zero && amount >= minimum)
                return true;
        }

        return false;
    }
}

/// <summary>One material's half of a <see cref="WolfmedEviscerationProfilePrototype"/>.</summary>
[DataDefinition]
public sealed partial class WolfmedEviscerationSpec
{
    /// <summary>The wound left behind. Null means this material is never torn open.</summary>
    [DataField]
    public ProtoId<WoundPrototype>? Wound;

    /// <summary>
    /// Severity it lands at. Kept past the degradation profile's stage 2 so the torso shows its worst
    /// overlay for as long as the wound is there, which is until surgery: healingMultiplier is 0.
    /// </summary>
    [DataField]
    public FixedPoint2 Severity = FixedPoint2.New(70);

    /// <summary>Organ slots that always come out. The abdomen, for a body of flesh.</summary>
    [DataField]
    public List<string> Organs = new();

    /// <summary>
    /// Organ slots that only come out to a blast, or on <see cref="VitalChance"/>: a cut opens the belly,
    /// a detonation empties the chest.
    /// </summary>
    [DataField]
    public List<string> VitalOrgans = new();

    /// <summary>Chance one of the vital slots comes out anyway when the hit was not an explosion.</summary>
    [DataField]
    public float VitalChance = 0.15f;

    /// <summary>How hard the contents scatter across the deck.</summary>
    [DataField]
    public float ScatterSpeed = 2f;

    /// <summary>Blood or oil put down on top of the tear's own spill.</summary>
    [DataField]
    public FixedPoint2 SpillVolume = FixedPoint2.New(20);

    /// <summary>Played over the tear. A chassis hisses as well as shearing.</summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>What everyone in range is told.</summary>
    [DataField]
    public LocId Popup = "wolfmed-evisceration-popup";
}
