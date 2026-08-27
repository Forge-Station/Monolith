namespace Content.Server._Forge.Botany.HydroponicsConsole;

[RegisterComponent]
public sealed partial class HydroponicsConsoleComponent : Component
{
    [DataField]
    public float UpdateInterval = 1f;

    public TimeSpan NextUpdate;
}
