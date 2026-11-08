namespace Content.Client.IconSmoothing;

/// <summary>
/// Sprite layer maps still referenced by wall prototypes as enum.CornerLayers.*.
/// </summary>
public enum CornerLayers : byte
{
    SE,
    NE,
    NW,
    SW,
}

/// <summary>
/// Sprite layer maps still referenced by rock prototypes as enum.EdgeLayer.*.
/// </summary>
public enum EdgeLayer : byte
{
    South,
    East,
    North,
    West,
}
