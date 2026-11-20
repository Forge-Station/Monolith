using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Planets;

/// <summary>
/// On a planet, a floor nobody built that breaks, pried up or blown apart, gives the ground back instead of station
/// plating and lattice. What a player built over the ground keeps its own layers (<see cref="WFPlanetBuiltTilesComponent"/>),
/// and natural ground that breaks down through them on purpose (Carcinoma's flesh, opening into the caverns) is left be.
/// A blast never leaves plating or lattice on the ground at all (<see cref="BlastTile"/>).
/// </summary>
public sealed class WFPlanetTileBreakSystem : EntitySystem
{
    [Dependency] private SharedBiomeSystem _biome = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    /// <summary>The station subfloors a broken planet floor must never show.</summary>
    private static readonly string[] Subfloors = { "Plating", "Lattice" };

    /// <summary>How many steps down a tile's base turfs an explosion may break it in one go.</summary>
    private const int MaxBreakSteps = 6;

    private readonly HashSet<int> _subfloorIds = new();
    /// <summary>Each layer's floors that broke to a subfloor this tick, with the tile they were.</summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, Tile>> _broken = new();
    private readonly List<(Vector2i, Tile)> _ground = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetLayerComponent, TileChangedEvent>(OnTileChanged);
    }

    /// <summary>Notes floors that broke down to a subfloor; it runs inside SetTiles, so the work waits for the update.</summary>
    private void OnTileChanged(Entity<WFPlanetLayerComponent> ent, ref TileChangedEvent args)
    {
        EnsureSubfloorIds();

        Dictionary<Vector2i, Tile>? broken = null;
        foreach (var change in args.Changes)
        {
            if (change.OldTile.IsEmpty || !_subfloorIds.Contains(change.NewTile.TypeId) || !BrokeDownTo(change.OldTile, change.NewTile))
                continue;

            if (broken == null && !_broken.TryGetValue(ent, out broken))
                _broken[ent] = broken = new Dictionary<Vector2i, Tile>();

            broken.TryAdd(change.GridIndices, change.OldTile);
        }
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_broken.Count == 0)
            return;

        foreach (var (layer, tiles) in _broken)
        {
            if (TerminatingOrDeleted(layer)
                || !TryComp<MapGridComponent>(layer, out var grid)
                || !TryComp<BiomeComponent>(layer, out var biome))
                continue;

            var built = CompOrNull<WFPlanetBuiltTilesComponent>(layer);
            _ground.Clear();
            foreach (var (index, was) in tiles)
            {
                // A player's own stack over the ground breaks the station way, down to the ground they built on.
                if (built != null && built.Underlay.ContainsKey(index))
                    continue;

                if (!_map.TryGetTileRef(layer, grid, index, out var current) || !_subfloorIds.Contains(current.Tile.TypeId))
                    continue;

                if (!_biome.TryGetBiomeTile(index, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var natural)
                    || natural.Value.IsEmpty || _subfloorIds.Contains(natural.Value.TypeId))
                    continue;

                // The ground itself breaking down, step by step, is the world working as made.
                if (natural.Value.TypeId == was.TypeId || BrokeDownTo(natural.Value, was))
                    continue;

                _ground.Add((index, natural.Value));
            }

            if (_ground.Count > 0)
                _map.SetTiles(layer, grid, _ground);
        }

        _broken.Clear();
    }

    /// <summary>
    /// What a blast that would break a planet tile to <paramref name="result"/> leaves instead, or null to let it.
    /// A player's floor breaks the station way but never through the ground it was built on; anything else that would
    /// show plating or lattice, or a hole through a mapped floor, gives the natural ground back. Natural ground blown
    /// open is the cavern way down, and stays a hole.
    /// </summary>
    public Tile? BlastTile(TileRef tileRef, ContentTileDefinition result)
    {
        var layer = tileRef.GridUid;
        if (!HasComp<WFPlanetLayerComponent>(layer))
            return null;

        var hole = result.ID == ContentTileDefinition.SpaceID;
        if (TryComp<WFPlanetBuiltTilesComponent>(layer, out var built) && built.Underlay.TryGetValue(tileRef.GridIndices, out var under))
        {
            if (!hole)
                return null;

            built.Underlay.Remove(tileRef.GridIndices);
            return under;
        }

        EnsureSubfloorIds();
        if (!hole && !_subfloorIds.Contains(result.TileId)
            || !TryComp<BiomeComponent>(layer, out var biome)
            || !_biome.TryGetBiomeTile(tileRef.GridIndices, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var natural)
            || natural.Value.IsEmpty || _subfloorIds.Contains(natural.Value.TypeId))
            return null;

        // The ground itself, or what is left of it after digging, opens; a mapped floor or a half-pried one does not.
        var current = tileRef.Tile;
        if (hole && (current.TypeId == natural.Value.TypeId
                     || !_subfloorIds.Contains(current.TypeId) && BrokeDownTo(natural.Value, current)))
            return null;

        return natural.Value;
    }

    /// <summary>Tile ids are only known once the tile prototypes have loaded.</summary>
    private void EnsureSubfloorIds()
    {
        if (_subfloorIds.Count > 0)
            return;

        foreach (var id in Subfloors)
        {
            if (_tileDefs.TryGetDefinition(id, out var def))
                _subfloorIds.Add(def.TileId);
        }
    }

    /// <summary>Whether <paramref name="to"/> lies down <paramref name="from"/>'s chain of base turfs: a break, not a build.</summary>
    private bool BrokeDownTo(Tile from, Tile to)
    {
        var def = (ContentTileDefinition) _tileDefs[from.TypeId];
        for (var step = 0; step < MaxBreakSteps && !string.IsNullOrEmpty(def.BaseTurf); step++)
        {
            if (!_tileDefs.TryGetDefinition(def.BaseTurf, out var next))
                return false;

            if (next.TileId == to.TypeId)
                return true;

            def = (ContentTileDefinition) next;
        }

        return false;
    }
}
