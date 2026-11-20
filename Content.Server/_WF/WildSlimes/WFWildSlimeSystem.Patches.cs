using System.Linq;
using System.Numerics;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared._WF.WildSlimes;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.WildSlimes;

/// <summary>Finds a world's slime patches from its biome as the world is built, and opens a shaft from each to its grotto.</summary>
public sealed partial class WFWildSlimeSystem
{
    [Dependency] private BiomeSystem _biome = default!;

    /// <summary>How far round a patch the grotto under it is looked for.</summary>
    public const int GrottoMargin = 3;

    private void InitializePatches()
    {
        // After the cavern system, which links the ground to its cavern.
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt, after: new[] { typeof(WFCavernSystem) });
    }

    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        var surface = args.Surface.ID;
        var habitat = _proto.EnumeratePrototypes<WFWildSlimeHabitatPrototype>().FirstOrDefault(h => h.Surface == surface);
        if (habitat == null)
            return;

        var centre = CompOrNull<WFPlanetNetworkComponent>(args.Network)?.Centre ?? Vector2.Zero;
        SetUpGround(args.Ground, habitat, centre, args.Surface.Radius);
    }

    /// <summary>Finds a ground's patches, records them with their grottos and opens their shafts.</summary>
    public Entity<WFWildSlimeGroundComponent>? SetUpGround(EntityUid ground, WFWildSlimeHabitatPrototype habitat, Vector2 centre, int radius)
    {
        if (!TryComp<BiomeComponent>(ground, out var biome) || !TryComp<MapGridComponent>(ground, out var grid))
            return null;

        var comp = EnsureComp<WFWildSlimeGroundComponent>(ground);
        comp.Habitat = habitat.ID;
        comp.Patches.Clear();

        var cavernGround = CompOrNull<WFCavernGroundComponent>(ground);
        comp.Cavern = cavernGround != null && !TerminatingOrDeleted(cavernGround.Cavern) ? cavernGround.Cavern : null;

        var patches = FindPatches((ground, biome), habitat, centre, radius);
        foreach (var tiles in patches)
        {
            var patch = new WFWildSlimePatch
            {
                Tiles = tiles,
                Centre = PickCentre(tiles),
                Radius = MathF.Sqrt(tiles.Count / MathF.PI),
            };

            for (var i = 0; i < habitat.SurfaceSlimes; i++)
            {
                patch.Surface.Add(new WFWildSlimeSlot());
            }

            if (comp.Cavern is { } cavern && habitat.GrottoTile != null)
            {
                patch.Grotto = FindGrotto(cavern, habitat, tiles);
                for (var i = 0; i < habitat.GrottoSlimes && patch.Grotto.Count > 0; i++)
                {
                    patch.Below.Add(new WFWildSlimeSlot());
                }

                if (habitat.Entrance && cavernGround != null && patch.Grotto.Contains(patch.Centre))
                    OpenShaft((ground, biome, grid), cavernGround, patch.Centre);
            }

            comp.Patches.Add(patch);
        }

        Log.Info($"Found {comp.Patches.Count} {habitat.ID} slime patches on {ToPrettyString(ground)}.");
        return (ground, comp);
    }

    /// <summary>Every patch on a ground as its tiles, read from the biome recipe alone; patches too small are left out.</summary>
    public List<List<Vector2i>> FindPatches(Entity<BiomeComponent> ground, WFWildSlimeHabitatPrototype habitat, Vector2 centre, int radius)
    {
        var result = new List<List<Vector2i>>();
        var gates = ground.Comp.Layers.OfType<BiomeMetaLayer>().Where(layer => layer.Template == habitat.PatchTemplate).ToList<IBiomeLayer>();
        if (gates.Count == 0)
        {
            Log.Warning($"{habitat.ID}: the biome of {ToPrettyString(ground)} has no {habitat.PatchTemplate} layer.");
            return result;
        }

        var patchTile = _tileDefs[habitat.PatchTile].TileId;
        var origin = new Vector2i((int) MathF.Floor(centre.X), (int) MathF.Floor(centre.Y));
        var found = new HashSet<Vector2i>();
        for (var x = -radius; x <= radius; x++)
        for (var y = -radius; y <= radius; y++)
        {
            if (x * x + y * y > radius * radius)
                continue;

            var index = origin + new Vector2i(x, y);
            // The patch noise first: it spares the full lookup on nearly every tile.
            if (!gates.Any(gate => _biome.WfPassesLayer(gate, ground.Comp.Seed, index)))
                continue;

            if (_biome.TryGetBiomeTile(index, ground.Comp.Layers, ground.Comp.Seed, (Entity<MapGridComponent>?) null, out var tile)
                && tile.Value.TypeId == patchTile)
                found.Add(index);
        }

        var seen = new HashSet<Vector2i>();
        foreach (var start in found.OrderBy(t => t.X).ThenBy(t => t.Y))
        {
            if (!seen.Add(start))
                continue;

            var cluster = new List<Vector2i> { start };
            var queue = new Queue<Vector2i>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out var tile))
            {
                foreach (var step in Steps)
                {
                    var next = tile + step;
                    if (!found.Contains(next) || !seen.Add(next))
                        continue;

                    cluster.Add(next);
                    queue.Enqueue(next);
                }
            }

            if (cluster.Count >= habitat.MinPatchTiles)
                result.Add(cluster);
        }

        return result;
    }

    private static readonly Vector2i[] Steps = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>The grotto tiles of the cavern round a patch, read from the cavern's biome.</summary>
    private List<Vector2i> FindGrotto(EntityUid cavern, WFWildSlimeHabitatPrototype habitat, List<Vector2i> patch)
    {
        var grotto = new List<Vector2i>();
        if (habitat.GrottoTile is not { } grottoId || !TryComp<BiomeComponent>(cavern, out var biome))
            return grotto;

        var grottoTile = _tileDefs[grottoId].TileId;
        var min = new Vector2i(patch.Min(t => t.X), patch.Min(t => t.Y)) - new Vector2i(GrottoMargin, GrottoMargin);
        var max = new Vector2i(patch.Max(t => t.X), patch.Max(t => t.Y)) + new Vector2i(GrottoMargin, GrottoMargin);
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            var index = new Vector2i(x, y);
            if (_biome.TryGetBiomeTile(index, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile)
                && tile.Value.TypeId == grottoTile)
                grotto.Add(index);
        }

        return grotto;
    }

    /// <summary>The patch tile nearest the middle with the whole patch round it, or just the nearest.</summary>
    private static Vector2i PickCentre(List<Vector2i> tiles)
    {
        var set = tiles.ToHashSet();
        var middle = new Vector2((float) tiles.Average(t => t.X), (float) tiles.Average(t => t.Y));
        var inner = tiles.Where(t =>
        {
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (!set.Contains(t + new Vector2i(x, y)))
                    return false;
            }

            return true;
        }).ToList();

        var pool = inner.Count > 0 ? inner : tiles;
        return pool.MinBy(t => Vector2.DistanceSquared(new Vector2(t.X, t.Y), middle));
    }

    /// <summary>
    /// Opens one ground tile as a shaft into the grotto, as a shovel would: the cavern hole queue pins it and gives it
    /// its landing, shade and stairs, on ground that isn't loaded too.
    /// </summary>
    private void OpenShaft(Entity<BiomeComponent, MapGridComponent> ground, WFCavernGroundComponent cavern, Vector2i index)
    {
        _biome.WfPinTiles((ground.Owner, ground.Comp1), new[] { index });
        if (_map.TryGetTileRef(ground.Owner, ground.Comp2, index, out var tile) && !tile.Tile.IsEmpty)
            _map.SetTile(ground.Owner, ground.Comp2, index, Tile.Empty);
        else
            cavern.Opened.Add(index);
    }
}
