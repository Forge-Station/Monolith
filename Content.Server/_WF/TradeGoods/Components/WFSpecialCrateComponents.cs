namespace Content.Server._WF.TradeGoods.Components;

/// <summary>A priority consignment: pays well at its destination before its deadline, less after, little elsewhere.</summary>
[RegisterComponent]
public sealed partial class WFConsignmentComponent : Component
{
    [DataField]
    public int Value = 20000;

    [DataField]
    public int OnTimeBonus = 10000;

    [DataField]
    public int LatePenalty = 10000;

    /// <summary>What any other chute pays for it.</summary>
    [DataField]
    public int Elsewhere = 2000;

    [DataField]
    public TimeSpan Window = TimeSpan.FromMinutes(25);

    [ViewVariables]
    public EntityUid? Origin;

    [ViewVariables]
    public EntityUid? Destination;

    [ViewVariables]
    public TimeSpan Deadline;

    /// <summary>The last ship announced as carrying it.</summary>
    [ViewVariables]
    public EntityUid? Announced;
}

