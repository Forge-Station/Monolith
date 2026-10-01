using Content.Shared.Body.Part;
using Content.Shared.Damage;

namespace Content.Shared._WF.Wolfmed.Armor;

/// <summary>A location-specific armour modifier set, matched by part type and symmetry.</summary>
[DataDefinition]
public sealed partial class ArmorPartModifier
{
    /// <summary>Matching body part types. Empty matches every type.</summary>
    [DataField] public HashSet<BodyPartType> Parts = [];

    /// <summary>Matching sides. Empty matches every symmetry.</summary>
    [DataField] public HashSet<BodyPartSymmetry> Symmetry = [];

    /// <summary>Modifiers applied to the struck part's share of the damage.</summary>
    [DataField(required: true)] public DamageModifierSet Modifiers = default!;
}
