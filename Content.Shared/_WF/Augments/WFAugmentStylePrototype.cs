using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Augments;

/// <summary>A look for cybernetic limbs and plating: the base sprite each body layer takes.</summary>
[Prototype("wfAugmentStyle")]
public sealed partial class WFAugmentStylePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public Dictionary<HumanoidVisualLayers, ProtoId<HumanoidSpeciesSpriteLayer>> Sprites = new();

    /// <summary>Leg and foot sprites worn on digitigrade legs.</summary>
    [DataField]
    public Dictionary<HumanoidVisualLayers, ProtoId<HumanoidSpeciesSpriteLayer>> Digitigrade = new();

    /// <summary>The style's limbs redrawn onto bodies whose limbs differ from the human's, by the limb's shape.</summary>
    [DataField]
    public Dictionary<ProtoId<WFAugmentShapePrototype>, ProtoId<HumanoidSpeciesSpriteLayer>> Fitted = new();
}
