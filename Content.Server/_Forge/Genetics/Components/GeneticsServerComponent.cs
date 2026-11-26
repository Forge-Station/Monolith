namespace Content.Server._Forge.Genetics.Components;

/// <summary>
/// Gene journal of one DNA server.
/// A console reads it only after the two are linked.
/// </summary>
[RegisterComponent]
public sealed partial class GeneticsServerComponent : Component
{
    public const string Port = "GeneticsServerReceiver";

    [DataField]
    public HashSet<string> Discovered = new();
}
