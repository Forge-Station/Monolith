using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetMap;

/// <summary>On a world's orbit layer: the stylised map its radars draw, with ground, water, roads and sites.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true), UnsavedComponent]
public sealed partial class WFPlanetMapComponent : Component
{
    /// <summary>The middle of the world.</summary>
    [ViewVariables, AutoNetworkedField]
    public Vector2 Centre;

    /// <summary>The world's edge; zero for an unbounded world.</summary>
    [ViewVariables, AutoNetworkedField]
    public float Radius;

    /// <summary>The clear ground round the middle where ships set down; zero when there is none.</summary>
    [ViewVariables, AutoNetworkedField]
    public float LandingRadius;

    /// <summary>Road and shoulder widths in tiles, for drawing the roads to scale.</summary>
    [ViewVariables, AutoNetworkedField]
    public float RoadWidth = 3f;

    [ViewVariables, AutoNetworkedField]
    public float ShoulderWidth;

    [ViewVariables, AutoNetworkedField]
    public WFPlanetMapTerrain? Terrain;

    [ViewVariables, AutoNetworkedField]
    public List<WFPlanetMapLine> Lines = new();

    [ViewVariables, AutoNetworkedField]
    public List<WFPlanetMapSite> Sites = new();

    /// <summary>Server only: the ground the roads and sites were read from, once they were.</summary>
    [ViewVariables]
    public EntityUid? RoadsFrom;

    /// <summary>Server only: the world radius the terrain was sampled for.</summary>
    [ViewVariables]
    public float TerrainRadius = -1f;
}

/// <summary>The ground of a world in coarse square cells, run-length encoded (<see cref="WFPlanetMapEncoding"/>).</summary>
[Serializable, NetSerializable]
public sealed class WFPlanetMapTerrain
{
    /// <summary>The world position of the bottom-left cell's corner.</summary>
    public Vector2i Origin;

    /// <summary>The side of a cell in tiles.</summary>
    public int Cell;

    public int Width;
    public int Height;

    /// <summary>The most common ground, drawn as the world's base colour.</summary>
    public WFPlanetMapGround Base;

    /// <summary>Pairs of (ground, run length), row by row from the bottom.</summary>
    public byte[] Runs = Array.Empty<byte>();
}

/// <summary>What a map cell shows.</summary>
public enum WFPlanetMapGround : byte
{
    Outside,
    Void,
    Grass,
    Dry,
    Snow,
    Rock,
    Vegetation,
    Formation,
    Water,
    Lava,
    Plasma,
    Flesh,
}

/// <summary>A road, footpath or tunnel as a polyline of tile positions.</summary>
[Serializable, NetSerializable]
public readonly record struct WFPlanetMapLine(WFPlanetMapLineKind Kind, Vector2i[] Points);

public enum WFPlanetMapLineKind : byte { Road, Trail, Tunnel }

/// <summary>
/// One site on the map. Its kind and name are hidden until the site is identified, except a city's or a cargo depot's
/// kind, which shows from orbit. <paramref name="Name"/> is a Fluent id.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WFPlanetMapSite(WFPlanetMapSiteKind Kind, Vector2i Position, Box2i Bounds, string Name);

/// <summary>What a site is; <see cref="Depot"/> is a cargo depot built on the planet.</summary>
public enum WFPlanetMapSiteKind : byte { Unknown, Building, House, Fuel, Vault, Facility, City, Camp, Wreck, Depot }
