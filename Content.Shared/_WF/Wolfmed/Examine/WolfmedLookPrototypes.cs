using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Examine;

/// <summary>How far a finding carries when somebody looks the patient over.</summary>
[Serializable, NetSerializable]
public enum WolfmedLookVisibility : byte
{
    /// <summary>Nothing to see. The analyzer still reports it.</summary>
    None,

    /// <summary>On bare skin or bare plating only; anything worn over the part hides it.</summary>
    Skin,

    /// <summary>Gross enough to show through clothing: deformity, a stump, sparks, a leak.</summary>
    Clothed,
}

/// <summary>What one wound looks like, resolved for the severity band it is currently in.</summary>
public readonly struct WolfmedLookFinding
{
    public readonly LocId? Description;
    public readonly WolfmedLookVisibility Visibility;

    /// <summary>Carries across a room. Everything else needs the examiner to be close.</summary>
    public readonly bool Distant;

    /// <summary>What the patient notices instead when the finding itself is not visible.</summary>
    public readonly LocId? SelfHint;

    /// <summary>LOOK2: the two-to-four word form of <see cref="Description"/> for the row.</summary>
    public readonly LocId? Label;

    /// <summary>LOOK2: the short form of <see cref="SelfHint"/>.</summary>
    public readonly LocId? HintLabel;

    /// <summary>LOOK2: glyph in analyzer_icons.rsi, or null for the wound's own category glyph.</summary>
    public readonly string? Icon;

    /// <summary>LOOK2: palette key, or null for the wound's own category colour.</summary>
    public readonly string? Colour;

    public WolfmedLookFinding(
        LocId? description,
        WolfmedLookVisibility visibility,
        bool distant,
        LocId? selfHint,
        LocId? label,
        LocId? hintLabel,
        string? icon,
        string? colour)
    {
        Description = description;
        Visibility = visibility;
        Distant = distant;
        SelfHint = selfHint;
        Label = label;
        HintLabel = hintLabel;
        Icon = icon;
        Colour = colour;
    }
}

/// <summary>One severity band's appearance. Every field falls back to the wound's own when unset.</summary>
[DataDefinition]
public sealed partial class WolfmedLookStage
{
    [DataField]
    public LocId? Description;

    [DataField]
    public WolfmedLookVisibility? Visibility;

    [DataField]
    public bool? Distant;

    [DataField]
    public LocId? SelfHint;

    /// <summary>LOOK2: the row label for this stage.</summary>
    [DataField]
    public LocId? Label;

    /// <summary>LOOK2: the row label for this stage's self hint.</summary>
    [DataField]
    public LocId? HintLabel;

    [DataField]
    public string? Icon;

    [DataField]
    public string? Colour;
}

/// <summary>
/// What one wound prototype looks like from the outside. The health examine reads nothing else: a wound
/// without one of these is invisible, which is what the coverage test in
/// <c>WolfmedVisualInspectionTest</c> refuses to let happen by accident.
/// </summary>
/// <remarks>
/// Keyed by wound rather than carried on <see cref="WoundPrototype"/> so the vendored Onyx wounds need no
/// edit, and stage-keyed so a cut reads shallow, deep or gaping off its own severity bands rather than
/// off a number the examiner could not possibly judge.
/// </remarks>
[Prototype("wolfmedWoundLook")]
public sealed partial class WolfmedWoundLookPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<WoundPrototype> Wound;

    /// <summary>Fallback for stages the <see cref="Stages"/> table does not name. Null means invisible.</summary>
    [DataField]
    public LocId? Description;

    [DataField]
    public WolfmedLookVisibility Visibility = WolfmedLookVisibility.Skin;

    [DataField]
    public bool Distant;

    [DataField]
    public LocId? SelfHint;

    /// <summary>LOOK2: the row label for <see cref="Description"/>, two to four words.</summary>
    [DataField]
    public LocId? Label;

    /// <summary>LOOK2: the row label for <see cref="SelfHint"/>.</summary>
    [DataField]
    public LocId? HintLabel;

    /// <summary>
    /// LOOK2: glyph in analyzer_icons.rsi. Unset means the wound's own category glyph, which is what nearly
    /// every wound wants; a fracture or a dead limb reads better as its condition.
    /// </summary>
    [DataField]
    public string? Icon;

    /// <summary>LOOK2: palette key. Unset means the wound's category colour.</summary>
    [DataField]
    public string? Colour;

    /// <summary>Per wound-stage overrides, keyed by the stage ids in the wound prototype's own table.</summary>
    [DataField]
    public Dictionary<string, WolfmedLookStage> Stages = new();

    /// <summary>The appearance for a wound sitting in the named stage.</summary>
    public WolfmedLookFinding Resolve(string? stage)
    {
        if (stage == null || !Stages.TryGetValue(stage, out var definition))
            return new WolfmedLookFinding(
                Description, Visibility, Distant, SelfHint, Label, HintLabel, Icon, Colour);

        return new WolfmedLookFinding(
            definition.Description ?? Description,
            definition.Visibility ?? Visibility,
            definition.Distant ?? Distant,
            definition.SelfHint ?? SelfHint,
            definition.Label ?? Label,
            definition.HintLabel ?? HintLabel,
            definition.Icon ?? Icon,
            definition.Colour ?? Colour);
    }
}

/// <summary>LOOK2: the glyph and colour one finding class draws with.</summary>
[DataDefinition]
public sealed partial class WolfmedLookGlyph
{
    /// <summary>State in analyzer_icons.rsi.</summary>
    [DataField(required: true)]
    public string Icon = string.Empty;

    /// <summary>Palette key; see <see cref="WolfmedLookPalette"/>.</summary>
    [DataField(required: true)]
    public string Colour = string.Empty;
}

/// <summary>Which body parts an item worn in these slots hides, for clothing with no armour coverage.</summary>
[DataDefinition]
public sealed partial class WolfmedLookCoverage
{
    [DataField(required: true)]
    public List<SlotFlags> Slots = new();

    [DataField]
    public HashSet<BodyPartType> Parts = new();
}

/// <summary>
/// The tuning the visual inspection reads: what clothing hides, where the bleeding bands sit and how far
/// a sepsis flush has to go before it shows on the face.
/// </summary>
[Prototype("wolfmedLookProfile")]
public sealed partial class WolfmedLookProfilePrototype : IPrototype
{
    public const string Default = "WFWolfmedLookDefault";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Slot defaults for ordinary clothing. An item declaring armour <c>coverage</c> uses that instead;
    /// armour that declares none falls back here, because unset coverage means "everything" to the damage
    /// side and would otherwise let a pair of gloves hide a chest wound.
    /// </summary>
    [DataField]
    public List<WolfmedLookCoverage> Coverage = new();

    /// <summary>Summed bleeding rate on a part before any blood shows at all.</summary>
    [DataField]
    public float BleedOozing = 0.01f;

    /// <summary>Rate at which the part reads as bleeding freely rather than oozing.</summary>
    [DataField]
    public float BleedFlowing = 0.18f;

    /// <summary>Rate at which it reads as spurting. An arterial bleed runs at 0.8.</summary>
    [DataField]
    public float BleedSpurting = 0.5f;

    /// <summary>Rate at which blood soaks through what is worn over the part.</summary>
    [DataField]
    public float SoakThrough = 0.18f;

    /// <summary>Rate at which the bleeding is visible from across the room.</summary>
    [DataField]
    public float DistantBleed = 0.18f;

    /// <summary>Sepsis progress before the patient is visibly flushed and sweating.</summary>
    [DataField]
    public float SepsisVisibleAt = 40f;

    /// <summary>
    /// LOOK2: the glyph and colour of every finding the inspection produces itself, keyed by the class names
    /// in <see cref="WolfmedLookClasses"/>. Wound findings take their own category's glyph instead.
    /// </summary>
    [DataField]
    public Dictionary<string, WolfmedLookGlyph> Classes = new();

    /// <summary>
    /// LOOK2: palette keys worst first. A part's row marker takes the first of these that any finding on it
    /// carries, so a bleeding, bruised, splinted leg reads as bleeding.
    /// </summary>
    [DataField]
    public List<string> AccentPriority = new();

    /// <summary>The glyph declared for a finding class, if the data declares one.</summary>
    public WolfmedLookGlyph? Glyph(string cls) => Classes.GetValueOrDefault(cls);
}
