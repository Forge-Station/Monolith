using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;

namespace Content.Server._Forge.Genetics.Components;

/// <summary>
/// Tracks appearance this genome applied so it can be stripped on revert.
/// </summary>
[RegisterComponent]
public sealed partial class GeneticMorphComponent : Component
{
    [DataField]
    public Dictionary<string, string> Markings = new();

    [DataField]
    public Dictionary<string, GeneticSpeciesSnapshot> Species = new();

    [DataField]
    public Dictionary<string, Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo?>> LimbLayers = new();
}

[DataDefinition]
public sealed partial class GeneticSpeciesSnapshot
{
    [DataField]
    public string Species = string.Empty;

    [DataField]
    public Color SkinColor;

    [DataField]
    public MarkingSet Markings = new();

    /// <summary>
    /// Damage modifier the body had before this form. Restored when the gene drops.
    /// </summary>
    [DataField]
    public string? DamageModifierSet;

    [DataField]
    public bool RevertDamageModifier;

    /// <summary>
    /// The rewrite stuck. Deactivating the gene does not restore the old species.
    /// </summary>
    [DataField]
    public bool Locked;
}
