using System.Numerics;
using Content.Shared.Decals;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.BloodTrail;

/// <summary>A mob that tracks what it touched: footprints while walking, drag marks while down.</summary>
[RegisterComponent]
public sealed partial class FootPrintsComponent : Component
{
    [DataField]
    public ProtoId<DecalPrototype> LeftBareDecal = "FootprintBareLeft";

    [DataField]
    public ProtoId<DecalPrototype> RightBareDecal = "FootprintBareRight";

    [DataField]
    public ProtoId<DecalPrototype> ShoesDecal = "FootprintShoes";

    [DataField]
    public ProtoId<DecalPrototype> SuitDecal = "FootprintSuit";

    [DataField]
    public List<ProtoId<DecalPrototype>> DraggingDecals = new()
    {
        "FootprintDragging1",
        "FootprintDragging2",
        "FootprintDragging3",
        "FootprintDragging4",
        "FootprintDragging5",
    };

    /// <summary>What the next print is drawn in. Nothing is left once its alpha runs out.</summary>
    [DataField]
    public Color PrintsColor = Color.Transparent;

    /// <summary>Distance between footprints.</summary>
    [DataField]
    public float StepSize = 0.7f;

    /// <summary>Distance between drag marks.</summary>
    [DataField]
    public float DragSize = 0.5f;

    /// <summary>Alpha each print takes off <see cref="PrintsColor"/>.</summary>
    [DataField]
    public float ColorReduceAlpha = 0.2f;

    /// <summary>How far a new stain pulls the hue of an old one towards its own.</summary>
    [DataField]
    public float ColorInterpolationFactor = 0.2f;

    /// <summary>Sideways offset of a footprint from the mob's center.</summary>
    [DataField]
    public Vector2 OffsetPrint = new(0.1f, 0f);

    [ViewVariables]
    public bool RightStep = true;

    /// <summary>Grid position of the last print.</summary>
    [ViewVariables]
    public Vector2 StepPos;
}
