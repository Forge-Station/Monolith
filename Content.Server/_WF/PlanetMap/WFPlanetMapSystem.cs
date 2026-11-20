using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetMap;

/// <summary>
/// Draws up each world's map on its orbit layer: the ground in coarse cells once the world exists, its roads, footpaths
/// and tunnels as polylines and its sites once they are placed, and a site's name once someone identifies it.
/// </summary>
public sealed class WFPlanetMapSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedBiomeSystem _biome = default!;
    [Dependency] private WFPlanetPreloadSystem _preload = default!;

    /// <summary>The most polyline points a world's roads, footpaths and tunnels may take in all.</summary>
    public const int MaxPoints = 4000;

    /// <summary>How far out an unbounded world's terrain is sampled.</summary>
    public const float UnboundedExtent = 384f;

    /// <summary>The fewest tiles a terrain cell spans.</summary>
    public const int MinCell = 8;

    private const string WreckName = "wf-poi-wreck";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private TimeSpan _next;
    private readonly Dictionary<int, WFPlanetMapGround> _tileGrounds = new();

    /// <summary>Terrain samples under way, by orbit layer: a world's whole map takes a few ticks to sample.</summary>
    private readonly Dictionary<EntityUid, TerrainSampling> _sampling = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetPreloadedEvent>(OnPreloaded, after: new[] { typeof(WFPlanetPoiSystem) });
    }

    private void OnPreloaded(ref WFPlanetPreloadedEvent args)
    {
        var query = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (query.MoveNext(out _, out var network))
        {
            if (network.GroundMap == args.Ground)
                Refresh(network);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A terrain sample under way carries on every tick.
        if (_timing.CurTime < _next && _sampling.Count == 0)
            return;
        if (_timing.CurTime >= _next)
            _next = _timing.CurTime + Interval;

        foreach (var orbit in _sampling.Keys.ToList())
        {
            if (TerminatingOrDeleted(orbit))
                _sampling.Remove(orbit);
        }

        var query = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (query.MoveNext(out _, out var network))
        {
            Refresh(network);
        }
    }

    /// <summary>Brings one world's map up to date; cheap once it is drawn up.</summary>
    public void Refresh(WFPlanetNetworkComponent network)
    {
        var orbitUid = network.OrbitMap;
        if (TerminatingOrDeleted(orbitUid) || !TryComp<WFOrbitLayerComponent>(orbitUid, out var orbit) || orbit.RadarLayers.Count == 0)
            return;

        var map = EnsureComp<WFPlanetMapComponent>(orbitUid);
        if (!MathHelper.CloseTo(map.TerrainRadius, orbit.BoundsRadius))
        {
            var centre = orbit.BoundsRadius > 0f ? orbit.BoundsCentre : network.Centre;
            if (!_sampling.TryGetValue(orbitUid, out var sampling) || !MathHelper.CloseTo(sampling.Radius, orbit.BoundsRadius))
                _sampling[orbitUid] = sampling = BeginSample(orbit, centre, orbit.BoundsRadius > 0f ? orbit.BoundsRadius : UnboundedExtent, orbit.BoundsRadius);

            // Sampled within the world-generation budget; the rest waits for the next tick.
            if (!SampleRows(sampling, () => _preload.GenerationSpent()))
                return;

            _sampling.Remove(orbitUid);
            map.Centre = centre;
            map.Radius = orbit.BoundsRadius;
            map.LandingRadius = LandingRadius(network);
            map.Terrain = EndSample(sampling);
            map.TerrainRadius = orbit.BoundsRadius;
            Dirty(orbitUid, map);
        }

        var ground = network.GroundMap;
        if (!TryComp<WFPlanetPoiComponent>(ground, out var record) || !record.NetworkReady)
            return;

        if (map.RoadsFrom != ground)
        {
            map.RoadsFrom = ground;
            DrawRoads(map, record);
            map.Sites = Sites(record);
            Dirty(orbitUid, map);
            return;
        }

        var sites = Sites(record);
        if (sites.SequenceEqual(map.Sites))
            return;
        map.Sites = sites;
        DirtyField(orbitUid, map, nameof(WFPlanetMapComponent.Sites));
    }

    /// <summary>The roads, footpaths and tunnels of a ground, within the point budget, roads first.</summary>
    private void DrawRoads(WFPlanetMapComponent map, WFPlanetPoiComponent record)
    {
        var centreline = record.Centreline;
        map.RoadWidth = centreline.Count > 0 ? MathF.Max(1f, (float) record.RoadTiles.Count / centreline.Count) : 3f;
        map.ShoulderWidth = centreline.Count > 0 ? (float) record.ShoulderTiles.Count / centreline.Count : 0f;

        var tunnels = new HashSet<Vector2i>();
        foreach (var tunnel in record.Tunnels)
            tunnels.UnionWith(tunnel);

        var lines = WFPlanetMapGeometry.Lines(centreline, WFPlanetMapLineKind.Road,
            tunnels.Count > 0 ? tunnels.Contains : null, MaxPoints);
        var used = lines.Sum(line => line.Points.Length);
        var trails = new HashSet<Vector2i>(record.TrailTiles);
        trails.ExceptWith(record.RoadTiles);
        lines.AddRange(WFPlanetMapGeometry.Lines(trails, WFPlanetMapLineKind.Trail, null, MaxPoints - used));
        map.Lines = lines;
    }

    /// <summary>
    /// The map's markers for a ground's sites, in the record's order. <paramref name="all"/> names every one, identified
    /// or not, for an admin's True IFF.
    /// </summary>
    public List<WFPlanetMapSite> Sites(WFPlanetPoiComponent record, bool all = false)
    {
        var sites = new List<WFPlanetMapSite>(record.Sites.Count);
        foreach (var site in record.Sites)
        {
            var revealed = TryComp<WFPoiSignalComponent>(site.Signal, out var signal) && signal.Revealed || all;
            var kind = KindOf(site);
            var position = signal?.Origin ?? site.Origin;
            if (!revealed)
            {
                // A city or a cargo depot shows from orbit; anything else is just a signal until someone gets there.
                sites.Add(new WFPlanetMapSite(kind is WFPlanetMapSiteKind.City or WFPlanetMapSiteKind.Depot ? kind : WFPlanetMapSiteKind.Unknown,
                    position, site.Bounds, string.Empty));
                continue;
            }

            sites.Add(new WFPlanetMapSite(kind, position, site.Bounds, site.Name.Id));
        }

        return sites;
    }

    /// <summary>What a site is, from the prototype it came from or the words in its name and map.</summary>
    public WFPlanetMapSiteKind KindOf(WFPlanetPoiSite site)
    {
        var name = site.Name.Id;
        if (name == WreckName)
            return WFPlanetMapSiteKind.Wreck;
        if (WFPlanetCargoHubSites.IsHub(name))
            return WFPlanetMapSiteKind.Depot;
        foreach (var city in _proto.EnumeratePrototypes<WFFixedCityPrototype>())
        {
            if (city.Name == site.Name)
                return WFPlanetMapSiteKind.City;
        }
        foreach (var camp in _proto.EnumeratePrototypes<WFPlanetCampPrototype>())
        {
            if (camp.Name == site.Name)
                return WFPlanetMapSiteKind.Camp;
        }

        var words = (name + " " + site.MapPath + " " + site.Dungeon).ToLowerInvariant();
        if (words.Contains("city") || words.Contains("town"))
            return WFPlanetMapSiteKind.City;
        if (words.Contains("vault"))
            return WFPlanetMapSiteKind.Vault;
        if (words.Contains("gas-station") || words.Contains("gas_station") || words.Contains("fuel"))
            return WFPlanetMapSiteKind.Fuel;
        if (words.Contains("house") || words.Contains("hideout") || words.Contains("dorms") || words.Contains("cabin"))
            return WFPlanetMapSiteKind.House;
        if (words.Contains("camp"))
            return WFPlanetMapSiteKind.Camp;
        return site.Dungeon != null ? WFPlanetMapSiteKind.Facility : WFPlanetMapSiteKind.Building;
    }

    /// <summary>The clear ground round the middle of the world, from the same POI table the sites came from.</summary>
    private float LandingRadius(WFPlanetNetworkComponent network)
    {
        var forced = _cfg.GetCVar(PlanetCVars.PoiTable);
        ProtoId<WFPlanetPoiTablePrototype>? id = forced.Length > 0 ? new ProtoId<WFPlanetPoiTablePrototype>(forced)
            : _proto.TryIndex(network.Surface, out var surface) ? surface.Pois : null;
        return id is { } tableId && _proto.TryIndex(tableId, out var table) ? table.CentreClear : 0f;
    }

    /// <summary>The world's terrain recipe sampled once a cell, never loading or reading ground chunks.</summary>
    public WFPlanetMapTerrain SampleTerrain(WFOrbitLayerComponent orbit, Vector2 centre, float extent)
    {
        var sampling = BeginSample(orbit, centre, extent, extent);
        SampleRows(sampling, () => false);
        return EndSample(sampling);
    }

    private TerrainSampling BeginSample(WFOrbitLayerComponent orbit, Vector2 centre, float extent, float radius)
    {
        var side = (int) MathF.Ceiling(extent * 2f);
        var cell = Math.Max(MinCell, (int) MathF.Ceiling(side / 128f));
        var size = Math.Min(128, (int) MathF.Ceiling(side / (float) cell) + 1);
        var origin = new Vector2i((int) MathF.Floor(centre.X - size * cell / 2f), (int) MathF.Floor(centre.Y - size * cell / 2f));
        return new TerrainSampling(orbit, centre, extent, radius, cell, size, origin, new byte[size * size],
            new int[Enum.GetValues<WFPlanetMapGround>().Length]);
    }

    /// <summary>Samples rows until <paramref name="spent"/> says stop, at least one; true once every row is done.</summary>
    private bool SampleRows(TerrainSampling s, Func<bool> spent)
    {
        while (s.Row < s.Size)
        {
            var y = s.Row++;
            for (var x = 0; x < s.Size; x++)
            {
                var world = new Vector2(s.Origin.X + (x + 0.5f) * s.Cell, s.Origin.Y + (y + 0.5f) * s.Cell);
                var ground = Classify(s.Orbit, world, s.Centre, s.Extent);
                s.Cells[y * s.Size + x] = (byte) ground;
                s.Counts[(int) ground]++;
            }

            if (s.Row < s.Size && spent())
                return false;
        }

        return true;
    }

    private static WFPlanetMapTerrain EndSample(TerrainSampling s)
    {
        var (origin, cell, size, cells, counts) = (s.Origin, s.Cell, s.Size, s.Cells, s.Counts);
        var dominant = WFPlanetMapGround.Grass;
        for (var i = (int) WFPlanetMapGround.Void; i < counts.Length; i++)
        {
            if (counts[i] > counts[(int) dominant])
                dominant = (WFPlanetMapGround) i;
        }

        return new WFPlanetMapTerrain
        {
            Origin = origin,
            Cell = cell,
            Width = size,
            Height = size,
            Base = dominant,
            Runs = WFPlanetMapEncoding.Encode(cells),
        };
    }

    private WFPlanetMapGround Classify(WFOrbitLayerComponent orbit, Vector2 world, Vector2 centre, float extent)
    {
        if (Vector2.DistanceSquared(world, centre) > extent * extent)
            return WFPlanetMapGround.Outside;

        foreach (var scar in orbit.RadarScars)
        {
            if (Vector2.DistanceSquared(world, new Vector2(scar.X, scar.Y)) <= scar.Z * scar.Z)
                return WFPlanetMapGround.Void;
        }

        var indices = world.Floored();
        if (!_biome.TryGetBiomeTile(indices, orbit.RadarLayers, orbit.RadarSeed, (Entity<MapGridComponent>?) null, out var tile)
            || tile.Value.IsEmpty)
            return WFPlanetMapGround.Void;

        if (_biome.TryGetEntity(indices, orbit.RadarLayers, tile.Value, orbit.RadarSeed, (Entity<MapGridComponent>?) null, out var feature)
            && FeatureGround(feature) is { } fromFeature)
            return fromFeature;

        return TileGround(tile.Value.TypeId);
    }

    /// <summary>Lava, water and the like are biome entities, not tiles.</summary>
    public static WFPlanetMapGround? FeatureGround(string? feature)
    {
        if (feature == null)
            return null;
        var id = feature.ToLowerInvariant();
        if (id.Contains("bloodriver") || id.Contains("fleshtree") || id.Contains("fleshpolyp") || id.Contains("wallmeat") || id.Contains("fleshblocker"))
            return WFPlanetMapGround.Flesh;
        if (id.Contains("lava"))
            return WFPlanetMapGround.Lava;
        if (id.Contains("water"))
            return WFPlanetMapGround.Water;
        if (id.Contains("plasma"))
            return WFPlanetMapGround.Plasma;
        if (id.Contains("rock") || id.Contains("planetmapore") || id.Contains("boulder") || id.Contains("crystal"))
            return WFPlanetMapGround.Formation;
        if (id.Contains("tree") || id.Contains("bush"))
            return WFPlanetMapGround.Vegetation;
        return null;
    }

    private WFPlanetMapGround TileGround(int tile)
    {
        if (_tileGrounds.TryGetValue(tile, out var ground))
            return ground;
        var id = _tiles[tile].ID.ToLowerInvariant();
        ground = id.Contains("flesh") ? WFPlanetMapGround.Flesh
            : id.Contains("water") || id.Contains("ocean") || id.Contains("riverbed") ? WFPlanetMapGround.Water
            : id.Contains("lava") ? WFPlanetMapGround.Lava
            : id.Contains("snow") || id.Contains("ice") ? WFPlanetMapGround.Snow
            : id.Contains("sand") || id.Contains("dirt") || id.Contains("mud") || id.Contains("desert") ? WFPlanetMapGround.Dry
            : id.Contains("grass") || id.Contains("jungle") ? WFPlanetMapGround.Grass
            : WFPlanetMapGround.Rock;
        _tileGrounds[tile] = ground;
        return ground;
    }

    /// <summary>A terrain sample part way through: the grid it fills and the next row to sample.</summary>
    private sealed record TerrainSampling(WFOrbitLayerComponent Orbit, Vector2 Centre, float Extent, float Radius, int Cell, int Size,
        Vector2i Origin, byte[] Cells, int[] Counts)
    {
        public int Row;
    }
}
