namespace Content.Server._Forge.Genetics.Components;

/// <summary>
/// Grid-local record of genes this station has expressed.
/// Consoles only see discoveries stored on servers that share their grid.
/// </summary>
[RegisterComponent]
public sealed partial class GeneticsServerComponent : Component
{
    [DataField]
    public HashSet<string> Discovered = new();
}
