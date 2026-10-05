using System.Numerics;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._WF.Planets;

/// <summary>Read-only terrain sampling. Never loads biome chunks, entities or decals.</summary>
public sealed partial class WFPlanetRadarSystem : EntitySystem
{
    [Dependency] private SharedBiomeSystem _biome = default!;
    [Dependency] private SharedMapSystem _map = default!;
    private List<IBiomeLayer>? _cachedLayers;
    private int _cachedSeed;
    private readonly Dictionary<Vector2i, Tile?> _samples = new();
    private readonly Dictionary<Vector2i, string?> _features = new();

    /// <summary>Samples static biome scenery, including lava/water that are entities rather than tiles.</summary>
    public string? SampleFeature(WFOrbitLayerComponent orbit, Vector2 world)
    {
        var tile = Sample(orbit, world);
        if (tile is not { IsEmpty: false } floor)
            return null;
        var indices = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        if (_features.TryGetValue(indices, out var feature))
            return feature;
        _biome.TryGetEntity(indices, orbit.RadarLayers, floor, orbit.RadarSeed,
            (Entity<MapGridComponent>?) null, out feature);
        _features[indices] = feature;
        return feature;
    }

    public Tile? Sample(WFOrbitLayerComponent orbit, Vector2 world)
    {
        foreach (var scar in orbit.RadarScars)
        {
            if (Vector2.DistanceSquared(world, new Vector2(scar.X, scar.Y)) <= scar.Z * scar.Z)
                return Tile.Empty;
        }

        var indices = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        if (orbit.RadarGround is { } groundId && TryGetEntity(groundId, out var ground)
            && TryComp<MapGridComponent>(ground, out var grid)
            && _map.TryGetTileRef(ground.Value, grid, indices, out var existing)
            && !existing.Tile.IsEmpty)
            return existing.Tile;

        if (!ReferenceEquals(_cachedLayers, orbit.RadarLayers) || _cachedSeed != orbit.RadarSeed)
        {
            _samples.Clear();
            _features.Clear();
            _cachedLayers = orbit.RadarLayers;
            _cachedSeed = orbit.RadarSeed;
        }
        if (_samples.TryGetValue(indices, out var cached))
            return cached;
        if (_samples.Count >= 8192)
        {
            _samples.Clear();
            _features.Clear();
        }
        // A null grid is deliberate: sample the recipe without consulting or generating ground chunks.
        var result = _biome.TryGetBiomeTile(indices, orbit.RadarLayers, orbit.RadarSeed,
            (Entity<MapGridComponent>?) null, out var tile) ? tile : null;
        _samples[indices] = result;
        return result;
    }
}
