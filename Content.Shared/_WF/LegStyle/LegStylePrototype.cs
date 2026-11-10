using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.LegStyle;

/// <summary>
/// Legs a species can pick instead of its own, or (with <see cref="Default"/>) the clothing fit of the legs it has.
/// </summary>
[Prototype("wfLegStyle")]
public sealed partial class LegStylePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Species that wear these legs.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<SpeciesPrototype>> Species = new();

    /// <summary>
    /// The stance these legs give.
    /// </summary>
    [DataField(required: true)]
    public LegStance Stance;

    /// <summary>
    /// The legs the listed species draw by themselves. Such a style swaps no sprites and only fits clothing to them.
    /// </summary>
    [DataField]
    public bool Default;

    /// <summary>
    /// Base sprites drawn in place of the species' own, by layer.
    /// </summary>
    [DataField]
    public Dictionary<HumanoidVisualLayers, ProtoId<HumanoidSpeciesSpriteLayer>> Sprites = new();

    /// <summary>
    /// Clothing slots these legs reshape. Such a slot uses the map below, or none, instead of the species' map.
    /// </summary>
    [DataField]
    public HashSet<string> DisplacementSlots = new();

    /// <summary>
    /// Displacement maps for the reshaped slots.
    /// </summary>
    [DataField]
    public Dictionary<string, DisplacementData> Displacements = new();

    /// <summary>
    /// Maps used instead on a female body.
    /// </summary>
    [DataField]
    public Dictionary<string, DisplacementData> FemaleDisplacements = new();

    /// <summary>
    /// Sprite folders, under /Textures, whose leg and foot markings are drawn for these legs, and so are not reshaped. All other leg
    /// markings are drawn for plantigrade legs and are reshaped on digitigrade ones.
    /// </summary>
    [DataField]
    public List<string> FittedMarkings = new();
}
