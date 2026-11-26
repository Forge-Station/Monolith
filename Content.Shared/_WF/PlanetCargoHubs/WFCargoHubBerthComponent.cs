namespace Content.Shared._WF.PlanetCargoHubs;

/// <summary>Marks the south-west tile of a hub's landing berth; the berth is the pad of <see cref="Size"/> tiles from it.</summary>
[RegisterComponent]
public sealed partial class WFCargoHubBerthComponent : Component
{
    /// <summary>The pad's width and depth in tiles, unturned.</summary>
    [DataField]
    public Vector2i Size = new(24, 36);
}
