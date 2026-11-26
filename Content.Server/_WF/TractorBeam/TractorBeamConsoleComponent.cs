namespace Content.Server._WF.TractorBeam;

[RegisterComponent]
public sealed partial class TractorBeamConsoleComponent : Component
{
    [DataField] public float Range = 200f;
    public TimeSpan NextCommand;
}
