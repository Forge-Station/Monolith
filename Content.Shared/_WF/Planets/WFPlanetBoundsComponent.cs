using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>
/// The circle a planet layer's terrain is confined to: chunks wholly outside it never load, and nothing on the layer
/// is meant to be out there. On every layer of a bounded network but the orbit layer, which carries it on
/// <see cref="WFOrbitLayerComponent"/> for the radar.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFPlanetBoundsComponent : Component
{
    /// <summary>The planet centre in the layer's frame; layers share the ground's frame.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 Centre;

    /// <summary>Radius of the circle in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float Radius;

    /// <summary>Whether a world position lies inside the circle.</summary>
    public bool Contains(Vector2 position)
    {
        return Vector2.DistanceSquared(position, Centre) <= Radius * Radius;
    }

    /// <summary>Whether a tile's centre lies inside the circle.</summary>
    public bool ContainsTile(Vector2i tile)
    {
        return Contains(new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
    }

    /// <summary>Whether any part of a square of tiles, such as a biome chunk, lies inside the circle.</summary>
    public bool MeetsSquare(Vector2i origin, int size)
    {
        var nearest = Vector2.Clamp(Centre, new Vector2(origin.X, origin.Y), new Vector2(origin.X + size, origin.Y + size));
        return Contains(nearest);
    }

    /// <summary>The nearest point inside the circle, <paramref name="margin"/> in from its edge, to a position.</summary>
    public Vector2 Clamp(Vector2 position, float margin = 0f)
    {
        var reach = Math.Max(Radius - margin, 0f);
        var offset = position - Centre;
        var distance = offset.Length();

        return distance <= reach || distance <= 0f ? position : Centre + offset * (reach / distance);
    }
}
