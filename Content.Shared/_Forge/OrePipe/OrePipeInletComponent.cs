namespace Content.Shared._Forge.OrePipe;

/// <summary>
/// Marks a drill (or hopper) as an ore-pipe network source. Requires a connected
/// <see cref="OrePipeOutletComponent"/> before the machine may run.
/// </summary>
[RegisterComponent]
public sealed partial class OrePipeInletComponent : Component
{
    /// <summary>
    /// Node name on <see cref="Content.Shared.NodeContainer.NodeContainerComponent"/> used for the ore pipe graph.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string NodeName = "ore";
}
