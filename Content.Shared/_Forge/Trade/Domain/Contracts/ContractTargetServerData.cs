using Content.Shared.FixedPoint;


namespace Content.Shared._Forge.Trade;


[Serializable]
public sealed class ContractTargetServerData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public string TargetItem { get; set; } = string.Empty;
    public string Solution { get; set; } = "drink";
    public FixedPoint2 ReagentAmount { get; set; } = FixedPoint2.New(1);
    public int Required { get; set; }
    public int Progress { get; set; }
    public bool BodyRequired { get; set; }
}
