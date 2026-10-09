namespace Content.Shared._Forge.OrePipe;

/// <summary>
/// Marks a machine as an ore-pipe network endpoint that accepts buffered ore into its <c>Storage</c>.
/// </summary>
[RegisterComponent]
public sealed partial class OrePipeOutletComponent : Component
{
    /// <summary>
    /// Node name on <see cref="Content.Shared.NodeContainer.NodeContainerComponent"/> used for the ore pipe graph.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string NodeName = "ore";

    /// <summary>
    /// Soft cap of ore units inserted into this outlet per flush tick.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MaxInsertPerFlush = 60;
}
