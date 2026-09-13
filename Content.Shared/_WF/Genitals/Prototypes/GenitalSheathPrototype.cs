using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Genitals.Prototypes;

/// <summary>Sheath or slit art. Outer states take the sheath colour, inner states the penis colour.</summary>
[Prototype]
public sealed partial class GenitalSheathPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Which sheath type this art is for. YAML key sheathType, because "type" is the prototype kind.</summary>
    [DataField("sheathType", required: true)]
    public SheathType Type;

    /// <summary>RSI path, relative to /Textures.</summary>
    [DataField(required: true)]
    public ResPath Sprite;

    /// <summary>Outer state at arousal None.</summary>
    [DataField(required: true)]
    public string RetractedOuter = "";

    /// <summary>Inner state at arousal None.</summary>
    [DataField(required: true)]
    public string RetractedInner = "";

    /// <summary>Outer state at arousal Partial.</summary>
    [DataField(required: true)]
    public string EmergingOuter = "";

    /// <summary>Inner state at arousal Partial.</summary>
    [DataField(required: true)]
    public string EmergingInner = "";

    /// <summary>Outer state drawn under the erect shaft at Full arousal; null hides the sheath then.</summary>
    [DataField]
    public string? ErectOuter;
}
