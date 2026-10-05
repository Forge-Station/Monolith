namespace Content.Server._WF.Planets;

/// <summary>On a biome entity its chunk's unload kept: the tile the biome grew it on.</summary>
[RegisterComponent]
public sealed partial class WFBiomeGrownComponent : Component
{
    [DataField]
    public Vector2i Tile;
}
