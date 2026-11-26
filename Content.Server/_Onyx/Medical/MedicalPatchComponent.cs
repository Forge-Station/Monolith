// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Content.Shared.FixedPoint;

namespace Content.Server._Onyx.Medical;

[RegisterComponent]
public sealed partial class MedicalPatchComponent : Component
{
    [DataField] public string SolutionName = "drink";
    [DataField] public FixedPoint2 TransferAmount = FixedPoint2.New(1);
    [DataField] public bool SingleUse;
    [DataField] public string? TrashObject = "UsedMedicalPatch";
    [DataField] public float UpdateTime = 1f;
    [DataField] public TimeSpan NextUpdate;
    [DataField] public FixedPoint2 InjectAmmountOnAttatch;
    [DataField] public FixedPoint2 InjectPercentageOnAttatch;
}
