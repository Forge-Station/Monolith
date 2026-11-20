namespace Content.Server._WF.Remade.Counterplay;

/// <summary>Tiles of a grid where corruption was burned away. Corruption can't grow on them until their time runs out.</summary>
[RegisterComponent]
public sealed partial class WFScorchedGridComponent : Component
{
    /// <summary>Tile to the time it stops being scorched.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, TimeSpan> Tiles = new();
}
