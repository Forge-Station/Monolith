using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Spawners.Components;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.Planets;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Dormancy;

/// <summary>Cuts a formed world into stash regions: one per site, wreck and camp, and a block per city level.</summary>
public sealed partial class WFSiteStashSystem
{
    /// <summary>Side of a city block, in metres; each level of a block sleeps on its own.</summary>
    public const float CellSize = 16f;

    /// <summary>
    /// Metres round a spawn point within which a city block never sleeps, on the spawn point's level and the one under
    /// it, which shows through the stairwells.
    /// </summary>
    public const float SpawnClearance = 16f;

    /// <summary>Side of a square of open world, in metres, whose roaming mobs sleep together.</summary>
    public const float WildCellSize = 64f;

    private readonly HashSet<EntityUid> _registered = new();

    private void InitializeRegions()
    {
        SubscribeLocalEvent<WFPlanetPoiComponent, ComponentShutdown>(OnPoiShutdown);
    }

    private void OnPoiShutdown(Entity<WFPlanetPoiComponent> ent, ref ComponentShutdown args)
    {
        _registered.Remove(ent.Owner);
        _regions.RemoveAll(region => region.Ground == ent.Owner);
    }

    /// <summary>Starts watching every world that has finished forming.</summary>
    private void RegisterPreloaded()
    {
        var query = EntityQueryEnumerator<WFPlanetPoiComponent, WFPlanetPreloadedComponent>();
        while (query.MoveNext(out var uid, out var record, out _))
        {
            if (record.NetworkReady && !_registered.Contains(uid))
            {
                _tickWork += $" register {ToPrettyString(uid)};";
                Register(uid);
            }
        }
    }

    /// <summary>
    /// Cuts a ground's sites into regions and pauses each at the next look if nobody is near. Done once a world has
    /// formed; a test may call it on a world built without the preload.
    /// </summary>
    public void Register(EntityUid ground)
    {
        if (!_enabled
            || !_registered.Add(ground)
            || !TryComp<WFPlanetPoiComponent>(ground, out var record)
            || _dormancy.PlanetOf(ground) is not { } network
            || !TryComp<WFPlanetNetworkComponent>(network, out var planet))
            return;

        // Every layer by its depth: the ground 0, the air above it up, the cavern and below down.
        var depths = new Dictionary<EntityUid, int>();
        for (var i = 0; i < planet.Layers.Count; i++)
            depths[planet.Layers[i]] = i;
        for (var i = 0; i < planet.LowerLayers.Count; i++)
            depths[planet.LowerLayers[i]] = -i - 1;

        var layers = depths.Keys
            .Where(layer => !TerminatingOrDeleted(layer) && HasComp<MapGridComponent>(layer) && !HasComp<WFOrbitLayerComponent>(layer))
            .ToList();

        // A city's levels: the layers it was merged into, and its own grids on the layers with no ground.
        var levels = layers.Select(layer => (Parent: layer, Depth: depths[layer])).ToList();
        var cityGrids = new List<EntityUid>();
        var cities = EntityQueryEnumerator<WFPlanetCityGridComponent, TransformComponent>();
        while (cities.MoveNext(out var uid, out var city, out var xform))
        {
            if (string.IsNullOrEmpty(city.City.Id) || xform.MapUid is not { } map || !depths.TryGetValue(map, out var depth))
                continue;

            cityGrids.Add(uid);
            levels.Add((uid, depth));
        }

        var spawns = new List<(Vector2 Position, int Depth)>();
        AddSpawns<SpawnPointComponent>(depths, spawns);
        AddSpawns<ContainerSpawnPointComponent>(depths, spawns);

        foreach (var site in record.Sites)
        {
            var box = new Box2(site.Bounds.Left, site.Bounds.Bottom, site.Bounds.Right, site.Bounds.Top);
            var label = site.MapPath ?? site.Dungeon?.Id ?? site.Name.Id;
            if (!site.City)
            {
                Add(new WFStashRegion
                {
                    Network = network,
                    Ground = ground,
                    Label = label,
                    Box = box,
                    Parents = layers,
                    Hulls = HullsIn(ground, box, cityGrids),
                    Protected = spawns.Any(spawn => box.Enlarged(SpawnClearance).Contains(spawn.Position)),
                });
                continue;
            }

            foreach (var (parent, depth) in levels)
            for (var x = box.Left; x < box.Right; x += CellSize)
            for (var y = box.Bottom; y < box.Top; y += CellSize)
            {
                var cell = new Box2(x, y, MathF.Min(x + CellSize, box.Right), MathF.Min(y + CellSize, box.Top));
                var near = cell.Enlarged(SpawnClearance);
                Add(new WFStashRegion
                {
                    Network = network,
                    Ground = ground,
                    Label = $"{label} ({x}, {y}) level {depth}",
                    Box = cell,
                    Parents = new List<EntityUid> { parent },
                    Protected = spawns.Any(spawn => (spawn.Depth == depth || spawn.Depth == depth + 1) && near.Contains(spawn.Position)),
                });
            }
        }

        AddTunnels(network, ground, record);
        AddWild(network, ground, record, layers, spawns);
    }

    /// <summary>
    /// The rest of the world in <see cref="WildCellSize"/> squares, for the mobs that roam it: only mobs, and none standing
    /// in a site or the city, whose own regions look after them.
    /// </summary>
    private void AddWild(EntityUid network, EntityUid ground, WFPlanetPoiComponent record, List<EntityUid> layers, List<(Vector2 Position, int Depth)> spawns)
    {
        if (!TryComp<WFPlanetBoundsComponent>(ground, out var bounds) || !float.IsFinite(bounds.Radius) || bounds.Radius <= 0f)
            return;

        var claimed = record.Sites.Select(site => new Box2(site.Bounds.Left, site.Bounds.Bottom, site.Bounds.Right, site.Bounds.Top)).ToList();
        var left = MathF.Floor((bounds.Centre.X - bounds.Radius) / WildCellSize) * WildCellSize;
        var bottom = MathF.Floor((bounds.Centre.Y - bounds.Radius) / WildCellSize) * WildCellSize;
        for (var x = left; x < bounds.Centre.X + bounds.Radius; x += WildCellSize)
        for (var y = bottom; y < bounds.Centre.Y + bounds.Radius; y += WildCellSize)
        {
            var cell = new Box2(x, y, x + WildCellSize, y + WildCellSize);
            var near = cell.Enlarged(SpawnClearance);
            Add(new WFStashRegion
            {
                Network = network,
                Ground = ground,
                Label = $"wild ({x}, {y})",
                Box = cell,
                Parents = layers,
                MobsOnly = true,
                Claimed = claimed,
                Protected = spawns.Any(spawn => near.Contains(spawn.Position) && !claimed.Any(box => box.Contains(spawn.Position))),
            });
        }
    }

    /// <summary>A region round each roofed stretch of road, for the lurkers in it.</summary>
    private void AddTunnels(EntityUid network, EntityUid ground, WFPlanetPoiComponent record)
    {
        for (var i = 0; i < record.Tunnels.Count; i++)
        {
            var tiles = record.Tunnels[i];
            if (tiles.Count == 0)
                continue;

            Add(new WFStashRegion
            {
                Network = network,
                Ground = ground,
                Label = $"tunnel {i}",
                Box = new Box2(tiles.Min(t => t.X) - 2, tiles.Min(t => t.Y) - 2, tiles.Max(t => t.X) + 3, tiles.Max(t => t.Y) + 3),
                Parents = new List<EntityUid> { ground },
            });
        }
    }

    private void Add(WFStashRegion region)
    {
        region.StashNow = true;
        _regions.Add(region);
    }

    /// <summary>The ships standing on a ground inside a box: a wreck's or a camp's, never a city level.</summary>
    private List<EntityUid> HullsIn(EntityUid ground, Box2 box, List<EntityUid> cityGrids)
    {
        var hulls = new List<EntityUid>();
        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.ParentUid == ground && uid != ground && !cityGrids.Contains(uid) && box.Contains(xform.LocalPosition))
                hulls.Add(uid);
        }

        return hulls;
    }

    /// <summary>Where, and on which level, players can spawn on a planet.</summary>
    private void AddSpawns<T>(Dictionary<EntityUid, int> depths, List<(Vector2, int)> spawns) where T : IComponent
    {
        var query = EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid is { } map && depths.TryGetValue(map, out var depth))
                spawns.Add((_transform.GetWorldPosition(xform), depth));
        }
    }
}
