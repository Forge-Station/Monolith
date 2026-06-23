using Robust.Shared.Serialization;
using Content.Shared.FixedPoint;


namespace Content.Shared._Forge.Trade;


[Serializable, NetSerializable,]
public sealed class ContractTargetClientData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public ContractTargetClientData() { }

    public ContractTargetClientData(string targetItem, int required, int progress)
    {
        TargetItem = targetItem;
        Required = required;
        Progress = progress;
    }

    public string TargetItem { get; set; } = string.Empty;
    public string Solution { get; set; } = "drink";
    public FixedPoint2 ReagentAmount { get; set; } = FixedPoint2.New(1);
    public int Required { get; set; }
    public int Progress { get; set; }
}
