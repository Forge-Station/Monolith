using Content.Shared.Body.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Biotype;

/// <summary>
/// What turns a mob of the other biotype into this one: the body it gets, the components it loses and the ones it
/// gets or has replaced. One per biotype.
/// </summary>
[Prototype("wfBiotype")]
public sealed partial class WFBiotypePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public WFBiotype Biotype;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>The icon on the biotype's box in the character creator.</summary>
    [DataField]
    public SpriteSpecifier? Icon;

    /// <summary>The body a mob of the other biotype is rebuilt as.</summary>
    [DataField(required: true)]
    public ProtoId<BodyPrototype> Body;

    /// <summary>Component names taken off the mob.</summary>
    [DataField]
    public List<string> Remove = new();

    /// <summary>Item slots taken off the mob, with whatever is in them.</summary>
    [DataField]
    public List<string> RemoveItemSlots = new();

    /// <summary>Components added, replacing any the mob already has.</summary>
    [DataField]
    public ComponentRegistry Components = new();

    /// <summary>Components added only where the mob has none. An ItemSlots entry adds its slots to the mob's own.</summary>
    [DataField]
    public ComponentRegistry Ensure = new();
}
