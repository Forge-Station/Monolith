using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Bench;

/// <summary>How hard a charge falls, from a fine to a warrant. A ticket takes its highest charge's level.</summary>
[Serializable, NetSerializable]
public enum WFPenaltyLevel : byte
{
    Citation,
    Impound,
    Banishment,
    Outlawry,
}

/// <summary>An article of the Common Writ, grouping its laws in the book and the Bench app.</summary>
[Prototype("wfLawChapter")]
public sealed partial class WFLawChapterPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>Position among the articles, lowest first.</summary>
    [DataField]
    public int Order;
}

/// <summary>One law of the Common Writ: its text, penalty level and the fine a judge may set.</summary>
[Prototype("wfLaw")]
public sealed partial class WFLawPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<WFLawChapterPrototype> Chapter;

    /// <summary>Position within its article, lowest first.</summary>
    [DataField]
    public int Order;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Text;

    [DataField(required: true)]
    public WFPenaltyLevel Level;

    /// <summary>The fine a new ticket starts at.</summary>
    [DataField]
    public int Fine;

    [DataField]
    public int MinFine;

    [DataField]
    public int MaxFine;
}
