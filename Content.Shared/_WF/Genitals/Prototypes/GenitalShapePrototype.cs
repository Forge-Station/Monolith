using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Genitals.Prototypes;

/// <summary>One selectable genital shape: its art, available sizes and biological options.</summary>
[Prototype]
public sealed partial class GenitalShapePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public GenitalSlot Slot;

    /// <summary>Creator tile label.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Shape word for examine text; null omits it (nondescript).</summary>
    [DataField]
    public LocId? ExamineName;

    /// <summary>RSI path, relative to /Textures.</summary>
    [DataField(required: true)]
    public ResPath Sprite;

    /// <summary>State template with {size}, {aroused} and {layer} tokens; may be a literal state.</summary>
    [DataField(required: true)]
    public string State = "";

    /// <summary>Tokens substituted for {size} by art step (breasts: a..o, huge, massive, giga, impossible); null uses the number.</summary>
    [DataField]
    public List<string>? SizeTokens;

    /// <summary>Art steps with FRONT _0 art.</summary>
    [DataField]
    public List<int> Sizes = new();

    /// <summary>Art steps with FRONT _1 art.</summary>
    [DataField]
    public List<int> ArousedSizes = new();

    /// <summary>Art steps with BEHIND _0 art.</summary>
    [DataField]
    public List<int> BehindSizes = new();

    /// <summary>Art steps with BEHIND _1 art.</summary>
    [DataField]
    public List<int> BehindArousedSizes = new();

    /// <summary>Optional map from logical step N to art step ArtSteps[N-1]; identity when null.</summary>
    [DataField]
    public List<int>? ArtSteps;

    /// <summary>Formatted state to real state, for typos in the source art.</summary>
    [DataField]
    public Dictionary<string, string> StateOverrides = new();

    /// <summary>Penis: sheath types biology allows for this shape. Empty means no sheath or slit.</summary>
    [DataField]
    public List<SheathType> AllowedSheaths = new();

    [DataField]
    public GenitalRegion Region = GenitalRegion.Groin;

    [DataField]
    public int SortOrder;

    /// <summary>Art type token, used by the legacy migration table generator.</summary>
    [DataField]
    public string? LegacyToken;

    /// <summary>Optional shape whose art is used for this shape's creator tile.</summary>
    [DataField]
    public ProtoId<GenitalShapePrototype>? Preview;
}
