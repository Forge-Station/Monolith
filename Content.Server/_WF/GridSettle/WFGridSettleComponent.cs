namespace Content.Server._WF.GridSettle;

/// <summary>Marks a grid nobody is flying or aboard, so space friction brings it to rest and it stops networking.</summary>
[RegisterComponent, Access(typeof(WFGridSettleSystem))]
public sealed partial class WFGridSettleComponent : Component;
