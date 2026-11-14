namespace Content.Shared._WF.Caverns;

/// <summary>Stairs built on a cavern floor: they open the ground tile above them and are walked up through it.</summary>
[RegisterComponent]
public sealed partial class WFCavernStairsComponent : Component
{
    /// <summary>Whether the stairs have opened the ground above; until then the server keeps trying.</summary>
    [ViewVariables]
    public bool Open;
}
