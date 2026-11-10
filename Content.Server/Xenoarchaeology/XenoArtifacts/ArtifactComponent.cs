namespace Content.Server.Xenoarchaeology.XenoArtifacts;

/// <summary>
/// Graph used by the trade contracts that still describe an artifact as a list of nodes.
/// The live artifact system stores nodes as their own entities, so this component is only here until those contracts
/// read that graph.
/// </summary>
[RegisterComponent]
public sealed partial class ArtifactComponent : Component
{
    [ViewVariables]
    public List<ArtifactNode> NodeTree = new();

    [ViewVariables]
    public int CurrentNodeId;
}

public sealed class ArtifactNode
{
    public int Id;
    public int Depth;
    public string Trigger = string.Empty;
    public string Effect = string.Empty;
    public bool Triggered;
    public bool Discovered;
    public List<int> Edges = new();
}
