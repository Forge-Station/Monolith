using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Marks a wound as an arterial bleed: a bleed that a dressing only slows, a tourniquet stops while it is
/// on, and only surgery or a suture over a stopped bleed actually closes. Read by
/// <see cref="WolfmedWoundTraitSystem"/> and by the Wolfmed halves of the bleeding and healing systems.
/// </summary>
/// <remarks>
/// A behavior rather than a component so it can be declared per stage: a wound may become arterial only
/// once it is bad enough. <see cref="WoundPrototype.TryGetBehavior{T}"/> resolves it at the wound's
/// current severity.
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedArterialBleedBehavior : WoundBehavior
{
    /// <summary>
    /// Replaces the shared bleeding-rate multiplier table for the treatments listed here. The default says
    /// a dressing buys a little time and nothing more; anything absent keeps the ordinary multiplier, so a
    /// tourniquet still stops the flow outright.
    /// </summary>
    [DataField]
    public Dictionary<BleedingTreatment, float> TreatmentMultipliers = new()
    {
        [BleedingTreatment.Bandaged] = 0.6f,
    };

    /// <summary>
    /// Whether a topical's bloodloss modifier may eat the wound's bleeding severity. False keeps gauze from
    /// whittling an artery shut one roll at a time.
    /// </summary>
    [DataField]
    public bool TopicalsReduceBleeding;

    /// <summary>
    /// Fraction of the wound's severity a coagulant in the blood (tranexamic acid and the like) cannot clot its
    /// bleeding below. A pumping artery slows under the drug and never stops for it; 0 lets a drug close it.
    /// </summary>
    [DataField]
    public float CoagulantFloor = 0.5f;

    /// <summary>
    /// Whether the wound refuses every treatment while it is still losing blood. The bleed has to be
    /// stopped first, by a tourniquet or by the clamping step of the surgery.
    /// </summary>
    [DataField]
    public bool RequiresStoppedBleedToTreat = true;

    /// <summary>Part types a tourniquet can tie off. An artery in the torso or the neck has nowhere to tie.</summary>
    [DataField]
    public HashSet<BodyPartType> TourniquetableParts =
    [
        BodyPartType.Arm,
        BodyPartType.Hand,
        BodyPartType.Leg,
        BodyPartType.Foot,
    ];
}

/// <summary>
/// A mechanical penalty to the limb the wound is on, applied through the same two multipliers a fracture
/// uses: movement speed on a mobility part, hand-work duration on a manipulation part.
/// </summary>
/// <remarks>
/// Onyx's own route for this is <see cref="WoundFunctionalityBehavior"/>, which is dead here:
/// <c>wounds.body_part_functionality_enabled</c> ships false and stays false (P2-3), so
/// <see cref="BodyPartFunctionalitySystem"/> reports every part functional. This behavior hooks the
/// fracture branch of <see cref="FractureEffectSystem"/> instead, which is not gated on that CVar.
/// A fracture on the same part shadows it, as it already shadows the functionality fallback.
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedLimbPenaltyBehavior : WoundBehavior
{
    /// <summary>Speed multiplier while the wound sits on a leg or a foot. 1 is no limp.</summary>
    [DataField]
    public float MovementModifier = 1f;

    /// <summary>Duration multiplier for what the matching hand does. 1 is no penalty.</summary>
    [DataField]
    public float ManipulationModifier = 1f;
}

/// <summary>
/// How much more likely than an ordinary wound this one is to go septic. Carries no effect by itself;
/// W5's infection timer reads it through <see cref="WolfmedWoundTraitSystem.GetInfectionRisk"/>.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedInfectionRiskBehavior : WoundBehavior
{
    [DataField]
    public float RiskMultiplier = 1f;

    /// <summary>
    /// Playtest 5: the wound carries its own contamination and goes bad whatever covers it: a bite, an open abdomen,
    /// a lodged round or shrapnel, dead tissue. Every other open wound needs a reason (WolfmedInfectionSystem.HasReason).
    /// </summary>
    [DataField]
    public bool Dirty;
}

/// <summary>
/// A head injury that muddles the patient rather than damaging the part: blurred sight, slurred speech and
/// a moment on the floor when it lands. Read by <see cref="WolfmedConcussionSystem"/>.
/// </summary>
/// <remarks>
/// Declared per stage, so a harder knock blurs more. Nothing treats it: the wound carries no damage types,
/// so no topical reaches it, and it fades on its own time (faster while the patient rests).
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedConcussionBehavior : WoundBehavior
{
    /// <summary>Blur added to the patient's vision, on the same scale as eye damage (capped at 6).</summary>
    [DataField]
    public float Blur = 2f;

    /// <summary>Whether speech comes out slurred.</summary>
    [DataField]
    public bool Stutter = true;

    /// <summary>How long the patient is off their feet when the wound lands or worsens.</summary>
    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(2);

    /// <summary>Severity lost per minute while the patient is up and about.</summary>
    [DataField]
    public FixedPoint2 RecoveryPerMinute = FixedPoint2.New(2);

    /// <summary>Recovery multiplier while asleep or buckled to a medical bed.</summary>
    [DataField]
    public float RestMultiplier = 4f;
}

/// <summary>
/// A joint knocked out of place. The penalty itself is <see cref="WolfmedLimbPenaltyBehavior"/>; this says
/// what it takes to put the joint back, which is the only thing that clears the wound.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedDislocationBehavior : WoundBehavior
{
    /// <summary>Do-after for someone else setting the joint.</summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    /// <summary>Doing it to your own arm takes longer, because you flinch.</summary>
    [DataField]
    public float SelfMultiplier = 2.5f;

    /// <summary>Pain the wrench itself causes, on top of what the joint already hurts.</summary>
    [DataField]
    public FixedPoint2 Pain = FixedPoint2.New(15);

    /// <summary>Pain multiplier when the patient is their own medic.</summary>
    [DataField]
    public float SelfPainMultiplier = 2f;

    /// <summary>Played at the patient when someone takes hold of the joint.</summary>
    [DataField]
    public SoundSpecifier? BeginSound = new SoundCollectionSpecifier("WFWolfmedJointStrain");

    /// <summary>Played when the joint goes back in.</summary>
    [DataField]
    public SoundSpecifier? EndSound = new SoundCollectionSpecifier("WFWolfmedWoundBone");
}

/// <summary>
/// A bruised organ under the wound: the blow reached something inside the part without rupturing it.
/// Applied by <see cref="Content.Server._WF.Wolfmed.Wounds.WolfmedOrganContusionSystem"/>.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedOrganContusionBehavior : WoundBehavior
{
    /// <summary>Organ health taken by the blow.</summary>
    [DataField]
    public FixedPoint2 Damage = FixedPoint2.New(4);

    /// <summary>Health the organ is never taken below, so a contusion bruises and never destroys.</summary>
    [DataField]
    public FixedPoint2 MinRemainingHealth = FixedPoint2.New(1);
}

/// <summary>
/// Declared on the top stage of a burn: tissue this badly cooked is dead, and a wound of its own is left
/// behind the moment the burn reaches that stage. Read by
/// <see cref="Content.Server._WF.Wolfmed.Wounds.WolfmedCharringSystem"/>.
/// </summary>
/// <remarks>
/// Putting the trigger on a stage rather than on a hit is what makes charring a *top-stage* wound: a part
/// chars because it has been burned that far, not because one shot was that big.
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedCharringBehavior : WoundBehavior
{
    /// <summary>The wound the dead tissue becomes.</summary>
    [DataField]
    public ProtoId<WoundPrototype> Wound = "WFWolfmedCharringWound";

    /// <summary>Severity of the charring left each time the burn crosses into this stage.</summary>
    [DataField]
    public FixedPoint2 Severity = FixedPoint2.New(20);
}

/// <summary>
/// How well this wound resists being burned shut. A wound that declares nothing is sealed by any heat that
/// reaches it; declaring this behavior makes searing it harder or impossible.
/// </summary>
/// <remarks>
/// The arterial bleed carries it: a stray laser closes a nicked artery, a severed one has to be held
/// against something hot on purpose. Read by
/// <see cref="Content.Server._WF.Wolfmed.Wounds.WolfmedCauterySystem"/>.
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedCauteryResistBehavior : WoundBehavior
{
    /// <summary>
    /// Severity above which incidental heat (a laser hit, a fire, a welder swung as a weapon) no longer
    /// seals the wound. A deliberate cautery ignores it.
    /// </summary>
    [DataField]
    public FixedPoint2 MaxIncidentalSeverity = FixedPoint2.New(12);

    /// <summary>Whether nothing but a deliberate cautery can ever seal it.</summary>
    [DataField]
    public bool DeliberateOnly;
}

/// <summary>
/// Feeling draining out of the part the wound is on: frostbite dulls what it freezes. Expressed as pain
/// suppression on the part, the same mechanism phase 5's painkillers use, so an analyzer reads a numb limb
/// as a quiet one and the patient stops noticing what else is wrong with it.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedNumbnessBehavior : WoundBehavior
{
    /// <summary>Pain suppressed on the part, topped up every tick while the wound is open.</summary>
    [DataField]
    public FixedPoint2 Suppression = FixedPoint2.New(6);

    /// <summary>How long the suppression takes to fade once the wound is gone.</summary>
    [DataField]
    public TimeSpan Decay = TimeSpan.FromSeconds(20);
}

/// <summary>
/// The part is far enough gone that tissue in it may start dying. Carries no effect of its own: it is the
/// flag W5's necrosis timer reads, through
/// <see cref="WolfmedWoundTraitSystem.GetNecrosisRisk"/>.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedNecrosisRiskBehavior : WoundBehavior
{
    /// <summary>
    /// How much faster than a baseline at-risk part this one goes: the onset is divided by it (M6). 0 is no risk.
    /// </summary>
    [DataField]
    public float RiskMultiplier = 1f;

    /// <summary>How long the part has to stay in this state at a risk of 1 before W5 calls it necrotic.</summary>
    [DataField]
    public TimeSpan Onset = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Something corrosive still on the skin. The wound keeps eating the part on a slow tick until the patient
/// is washed off; see <see cref="Content.Shared._WF.Wolfmed.Wounds.WolfmedChemicalBurnSystem"/>.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedCausticResidueBehavior : WoundBehavior
{
    /// <summary>Damage dealt to the part on each tick. Kept under the wound's own rule threshold so the
    /// residue cannot deepen itself into a fresh chemical burn.</summary>
    [DataField]
    public DamageSpecifier Damage = new();

    /// <summary>Seconds between ticks.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(4);
}

/// <summary>
/// What a shock does past the burn: the muscles it crosses lock and let go of whatever they were holding. M3
/// (OD15): the heart damage is no longer a roll here but a band on each hit's Shock, in
/// <c>WolfmedElectricalBurnSystem</c>.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedElectricalShockBehavior : WoundBehavior
{
    /// <summary>How long the patient's muscles lock up. Zero skips the spasm.</summary>
    [DataField]
    public TimeSpan Spasm = TimeSpan.FromSeconds(1.5);

    /// <summary>Whether the spasm also empties the patient's hands.</summary>
    [DataField]
    public bool DropHeld = true;
}

/// <summary>
/// Current that found a path it should not have inside a chassis: the part arcs, the frame locks up for a
/// moment and something sparks. Read by
/// <see cref="Content.Server._WF.Wolfmed.Wounds.WolfmedShortCircuitSystem"/>.
/// </summary>
/// <remarks>
/// The mechanical counterpart of <see cref="WolfmedElectricalShockBehavior"/>: no heart to stop and no
/// hands to open, so this is a shorter lock-up plus visible feedback that tells the player what happened.
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedShortCircuitBehavior : WoundBehavior
{
    /// <summary>How long the chassis is locked up each time the wound lands or worsens.</summary>
    [DataField]
    public TimeSpan Stun = TimeSpan.FromSeconds(1);

    /// <summary>Effect spawned at the body. Null skips the visual.</summary>
    [DataField]
    public EntProtoId? Effect = "EffectSparks";

    /// <summary>Sound collection played with it. Null skips the sound.</summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundCollectionSpecifier("sparks");
}

/// <summary>
/// A part running too hot. The wound cools on its own and far faster when something cold reaches it, so
/// unlike every other wound in the system the passage of time is the treatment. Read by
/// <see cref="WolfmedOverheatingSystem"/>.
/// </summary>
/// <remarks>
/// The slowdown itself is <see cref="WolfmedLimbPenaltyBehavior"/> on the same stage; this only says how
/// quickly the part sheds severity. Declared per stage so a badly overheated part cools slowly at first.
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedOverheatBehavior : WoundBehavior
{
    /// <summary>Severity shed per minute with nothing helping.</summary>
    [DataField]
    public FixedPoint2 CoolingPerMinute = FixedPoint2.New(6);

    /// <summary>Severity shed per point of Cold damage that reaches the part.</summary>
    [DataField]
    public float CoolingPerCold = 1.5f;

    /// <summary>Severity shed by one dousing, which is any water reaching the body.</summary>
    [DataField]
    public FixedPoint2 CoolingPerDousing = FixedPoint2.New(15);
}

/// <summary>
/// What an embedded-object wound becomes once the last object is out. A lodged round with nothing lodged in it
/// is an ordinary hole: it turns into that wound, at the same severity, and is treated like one.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedClearedWoundBehavior : WoundBehavior
{
    [DataField(required: true)]
    public ProtoId<WoundPrototype> Wound;

    /// <summary>Severity the replacement lands at. Null carries the cleared wound's own severity over.</summary>
    [DataField]
    public FixedPoint2? Severity;
}

