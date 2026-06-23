namespace Content.Shared._Forge.Trade;


/// <summary>
///     Marks a physical container as a Retrieval delivery destination.
///     ncRetrievalDestinationPreset.target.type: ContainerGroup resolves against these groups.
/// </summary>
[RegisterComponent]
public sealed partial class NcContractTurnInContainerComponent : Component
{
    [DataField("groups")]
    public HashSet<string> Groups { get; set; } = new();
}
