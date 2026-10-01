// WOLFGATE: P3-D5 (Option B). Onyx's ArmorComponent.Locational.cs datafields, added through a partial so
// Content.Shared/Armor/ArmorComponent.cs keeps zero edits. Read only by WolfmedPartArmorSystem; prototype-static
// and never mutated at runtime, so no [AutoNetworkedField] (ArmorPartModifier is a [DataDefinition], not
// NetSerializable). Onyx's `traumaDeductions` key is deliberately not ported (P3-D20: no C# datafield exists in
// Onyx either) and neither is `ShowArmorOnExamine`.

using Content.Shared._WF.Wolfmed.Armor;
using Content.Shared.Body.Part;

namespace Content.Shared.Armor;

public sealed partial class ArmorComponent
{
    /// <summary>Body part types this armour protects. Null or empty protects every part; the worn slot is irrelevant.</summary>
    [DataField] public HashSet<BodyPartType>? Coverage;

    /// <summary>Sides this armour protects. Null or empty protects every symmetry.</summary>
    [DataField] public HashSet<BodyPartSymmetry>? CoverageSymmetry;

    /// <summary>Ordered per-location modifier overrides; the first matching entry wins and skips the coverage gate.</summary>
    [DataField] public List<ArmorPartModifier> PartModifiers = new();
}
