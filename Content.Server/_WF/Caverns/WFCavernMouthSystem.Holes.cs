using System.Linq;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Caverns;

public sealed partial class WFCavernMouthSystem
{
    /// <summary>Most holes one ground fits out in a tick; the rest wait for the next.</summary>
    public const int OpenedPerTick = 256;

    /// <summary>How far a hole looks for a climb point, in steps across open cavern floor, before it gets one of its own.</summary>
    public const int ClimbReach = 8;

    /// <summary>How far past a hole's tile the landing below is cleared: a 3x3.</summary>
    public const int LandingReach = 1;

    private readonly HashSet<Entity<CEZPhysicsComponent>> _bodies = new();

    /// <summary>Steps from cavern tiles to the nearest climb point, up to <see cref="ClimbReach"/>, for one batch of holes.</summary>
    private readonly Dictionary<Vector2i, int> _climbSteps = new();

    private readonly Queue<Vector2i> _climbFrontier = new();

    private void InitializeHoles()
    {
        SubscribeLocalEvent<WFCavernGroundComponent, TileChangedEvent>(OnGroundTileChanged);
        SubscribeLocalEvent<WFCavernLayerComponent, TileChangedEvent>(OnCavernTileChanged);
        SubscribeLocalEvent<WFCavernShaftComponent, EntityTerminatingEvent>(OnShadeTerminating);
        SubscribeLocalEvent<WFCavernClimbComponent, EntityTerminatingEvent>(OnClimbTerminating);
    }

    /// <summary>Notes ground tiles that emptied or filled; it runs inside SetTiles, the biome loader's too, so the work waits for the update.</summary>
    private void OnGroundTileChanged(Entity<WFCavernGroundComponent> ent, ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
        {
            if (!change.EmptyChanged)
                continue;

            if (change.NewTile.IsEmpty)
                ent.Comp.Opened.Add(change.GridIndices);
            else if (ent.Comp.Shades.ContainsKey(change.GridIndices))
                ent.Comp.Closed.Add(change.GridIndices);
        }
    }

    /// <summary>Notes cavern tiles that emptied, so the update can close the bottom layer again.</summary>
    private void OnCavernTileChanged(Entity<WFCavernLayerComponent> ent, ref TileChangedEvent args)
    {
        if (!TryComp<WFCavernGroundComponent>(ent.Comp.Ground, out var ground))
            return;

        foreach (var change in args.Changes)
        {
            if (change.EmptyChanged && change.NewTile.IsEmpty)
                ground.FloorOpened.Add(change.GridIndices);
        }
    }

    /// <summary>Drops a shade deleted by anything else, such as an admin, from its ground's registry.</summary>
    // Not ComponentShutdown: a deleted entity is already in nullspace by then, so its tile is unknown.
    private void OnShadeTerminating(Entity<WFCavernShaftComponent> ent, ref EntityTerminatingEvent args)
    {
        var xform = Transform(ent);
        if (xform.MapUid is not { } map
            || !TryComp<WFCavernGroundComponent>(map, out var ground)
            || !TryComp<MapGridComponent>(map, out var grid))
            return;

        var index = _map.TileIndicesFor(map, grid, xform.Coordinates);
        if (!ground.Shades.TryGetValue(index, out var shade) || shade != ent.Owner)
            return;

        ground.Shades.Remove(index);
        _eyes.CheckSoon();
    }

    /// <summary>Drops a climb point deleted by anything else from its ground's registry.</summary>
    private void OnClimbTerminating(Entity<WFCavernClimbComponent> ent, ref EntityTerminatingEvent args)
    {
        var xform = Transform(ent);
        if (xform.MapUid is not { } map
            || !TryComp<WFCavernLayerComponent>(map, out var layer)
            || !TryComp<WFCavernGroundComponent>(layer.Ground, out var ground)
            || !TryComp<MapGridComponent>(map, out var grid))
            return;

        var index = _map.TileIndicesFor(map, grid, xform.Coordinates);
        if (ground.ClimbPoints.TryGetValue(index, out var climb) && climb == ent.Owner)
            ground.ClimbPoints.Remove(index);
    }

    /// <summary>
    /// Runs the hole queue on every ground: filled holes lose their shades, opened cavern floor closes, new holes are
    /// fitted out and holes that lost their stairs get what they went without.
    /// </summary>
    private void UpdateHoles()
    {
        var query = EntityQueryEnumerator<WFCavernGroundComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Opened.Count == 0 && comp.Closed.Count == 0 && comp.FloorOpened.Count == 0 && comp.Refit.Count == 0)
                continue;

            Entity<WFCavernGroundComponent> ground = (uid, comp);
            if (!TryGetContext(ground, out var context))
            {
                comp.Opened.Clear();
                comp.Closed.Clear();
                comp.FloorOpened.Clear();
                comp.Refit.Clear();
                continue;
            }

            CloseHoles(ground, context);
            CloseFloor(ground, context);
            OpenHoles(ground, context);
            RefitHoles(ground, context);
        }
    }

    /// <summary>Deletes the shades of holes something filled, such as lattice or a floor laid over them.</summary>
    // The ground stays pinned and the landing and climb point stay, so opening the hole again brings back only its shade.
    private void CloseHoles(Entity<WFCavernGroundComponent> ground, MouthContext context)
    {
        if (ground.Comp.Closed.Count == 0)
            return;

        var closed = false;
        foreach (var index in ground.Comp.Closed)
        {
            if (!ground.Comp.Shades.TryGetValue(index, out var shade) || IsEmpty(context.Ground, index))
                continue;

            ground.Comp.Shades.Remove(index);
            Del(shade);
            closed = true;
        }

        ground.Comp.Closed.Clear();

        if (closed)
            _eyes.CheckSoon();
    }

    /// <summary>
    /// Fills any cavern tile that was opened, such as lattice laid on the floor and then taken by an RCD or a blast,
    /// with the ground under the lattice or the natural floor: nothing opens the bottom layer. An unload's empty tiles
    /// are left alone.
    /// </summary>
    private void CloseFloor(Entity<WFCavernGroundComponent> ground, MouthContext context)
    {
        if (ground.Comp.FloorOpened.Count == 0)
            return;

        var level = context.Level;
        var levelBiome = (level.Owner, level.Comp1);
        var built = CompOrNull<WFPlanetBuiltTilesComponent>(level);
        var landing = new Tile(_tileDefs[context.Spec.LandingTile].TileId);
        var tiles = new List<(Vector2i, Tile)>();

        foreach (var index in ground.Comp.FloorOpened)
        {
            if (!IsEmpty(level, index)
                || !_biome.WfIsChunkLoaded(levelBiome, index) && !_biome.WfIsPinned(levelBiome, index))
                continue;

            if (built != null && built.Underlay.Remove(index, out var under))
                tiles.Add((index, under));
            else
                tiles.Add((index, NaturalOr(level, index, landing)));
        }

        ground.Comp.FloorOpened.Clear();

        if (tiles.Count == 0)
            return;

        _map.SetTiles(level.Owner, level.Comp2, tiles);
        _biome.WfPinTiles(levelBiome, tiles.Select(tile => tile.Item1).ToList());
    }

    /// <summary>
    /// Fits out every ground tile that became a real hole: pinned, with a cleared landing below, a shade, and a climb
    /// point near it. Tiles an unload emptied are dropped: they are unpinned, on a chunk no longer loaded.
    /// </summary>
    // A faller takes about 0.45 s to sink through the ground, so the landing is always there first. Every queued tile is
    // checked each tick, as the checks are cheap, so an unload's thousands of tiles never hold back a real hole.
    private void OpenHoles(Entity<WFCavernGroundComponent> ground, MouthContext context)
    {
        if (ground.Comp.Opened.Count == 0)
            return;

        var groundBiome = (context.Ground.Owner, context.Ground.Comp1);
        var holes = new List<Vector2i>();

        foreach (var index in ground.Comp.Opened)
        {
            // A mouth's own hole: Stamp registers its shades as it cuts it.
            if (ground.Comp.Shades.ContainsKey(index) || !IsEmpty(context.Ground, index))
                continue;

            // The unloader empties only natural tiles and never pins them; a hole whose chunk unloads first is pinned.
            if (!_biome.WfIsChunkLoaded(groundBiome, index) && !_biome.WfIsPinned(groundBiome, index))
                continue;

            holes.Add(index);
        }

        ground.Comp.Opened.Clear();

        if (holes.Count == 0)
            return;

        holes.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

        if (holes.Count > OpenedPerTick)
        {
            ground.Comp.Opened.UnionWith(holes.Skip(OpenedPerTick));
            holes.RemoveRange(OpenedPerTick, holes.Count - OpenedPerTick);
        }

        EnsureHoles(ground, context, holes);
    }

    /// <summary>
    /// Pins holes, lays their landings, spawns their shades, wakes what stood on them and gives each a climb point it
    /// can reach. A hole over stairs gets no landing or climb point: the stairs are both.
    /// </summary>
    private void EnsureHoles(Entity<WFCavernGroundComponent> ground, MouthContext context, List<Vector2i> holes)
    {
        _biome.WfPinTiles((context.Ground.Owner, context.Ground.Comp1), holes);

        // Lattice's record of the ground under it: cutting a later lattice here must reopen the hole, not plug it.
        if (TryComp<WFPlanetBuiltTilesComponent>(ground, out var built))
        {
            foreach (var index in holes)
            {
                built.Underlay.Remove(index);
            }
        }

        Entity<MapGridComponent> levelGrid = (context.Level.Owner, context.Level.Comp2);
        var drops = holes.Where(hole => !HasStairs(levelGrid, hole)).ToList();

        if (drops.Count > 0)
            PrepareLandings(context, drops);

        foreach (var index in holes)
        {
            var multiplier = _map.TryGetTileRef(context.Level.Owner, context.Level.Comp2, index, out var below) && !below.Tile.IsEmpty
                ? ((ContentTileDefinition) _tileDefs[below.Tile.TypeId]).FallDamageMultiplier
                : 1f;

            SpawnShade(ground, context, index, multiplier);
            WakeBodiesOn(context.Ground, index);
        }

        if (drops.Count > 0)
            EnsureClimbs(ground, context, drops);

        // Open the view below now rather than at the next check.
        _eyes.CheckSoon();
    }

    /// <summary>
    /// Lays the world's landing tile under each hole and clears the natural floor a tile around it of rock and
    /// anything else the biome grew, then pins it all, so nobody lands boxed in rock.
    /// </summary>
    // A tile keeps what is on it if it is pinned (a mouth's pad, an earlier landing) or is not the biome's own, which
    // is a floor someone built: laid on a loaded chunk, nothing has pinned that yet. Natural floor under the hole itself
    // takes the landing tile. Only entities the biome still tracks are cleared.
    private void PrepareLandings(MouthContext context, List<Vector2i> holes)
    {
        var level = context.Level;
        var levelBiome = (level.Owner, level.Comp1);
        var landing = new Tile(_tileDefs[context.Spec.LandingTile].TileId);
        var holeSet = holes.ToHashSet();
        var targets = new Dictionary<Vector2i, Tile>();
        var area = new HashSet<Vector2i>();

        foreach (var hole in holes)
        {
            if (IsEmpty(level, hole) || IsNatural(level, hole))
                targets[hole] = landing;

            for (var x = -LandingReach; x <= LandingReach; x++)
            for (var y = -LandingReach; y <= LandingReach; y++)
            {
                area.Add(hole + new Vector2i(x, y));
            }
        }

        foreach (var index in area)
        {
            if (holeSet.Contains(index) || !IsEmpty(level, index) && (_biome.WfIsPinned(levelBiome, index) || !IsNatural(level, index)))
                continue;

            targets[index] = NaturalOr(level, index, landing);
        }

        ClearBiomeEntities(level, area);

        var changes = new List<(Vector2i, Tile)>();
        foreach (var (index, tile) in targets)
        {
            if (!_map.TryGetTileRef(level.Owner, level.Comp2, index, out var current) || current.Tile.TypeId != tile.TypeId)
                changes.Add((index, tile));
        }

        if (changes.Count > 0)
            _map.SetTiles(level.Owner, level.Comp2, changes);

        _biome.WfPinTiles(levelBiome, area.ToList());
        LayLandingEntities(context, targets.Select(pair => (pair.Key, pair.Value)), landing);
    }

    /// <summary>
    /// Gives each hole a climb point unless one lies within <see cref="ClimbReach"/> steps of its landing across cavern
    /// floor free of hard anchored entities, so no hole drops anyone into a pocket with no way up.
    /// </summary>
    // Holes go in order and each new climb point spreads at once, so a crater's joined landings share one. A mouth's hole
    // has its own. On an unloaded chunk only pinned tiles exist, and the biome grows nothing on them when it loads.
    private void EnsureClimbs(Entity<WFCavernGroundComponent> ground, MouthContext context, List<Vector2i> holes)
    {
        _climbSteps.Clear();

        var min = holes[0];
        var max = holes[0];
        foreach (var hole in holes)
        {
            min = Vector2i.ComponentMin(min, hole);
            max = Vector2i.ComponentMax(max, hole);
        }

        foreach (var index in ground.Comp.ClimbPoints.Keys)
        {
            if (index.X >= min.X - ClimbReach && index.X <= max.X + ClimbReach
                && index.Y >= min.Y - ClimbReach && index.Y <= max.Y + ClimbReach)
                SpreadClimb(context.Level, index);
        }

        foreach (var hole in holes)
        {
            if (_climbSteps.ContainsKey(hole) || InMouthHole(ground, hole))
                continue;

            if (TrySpawnClimbNear(ground, context, hole, out var climb))
                SpreadClimb(context.Level, climb);
        }

        _climbSteps.Clear();
    }

    /// <summary>Walks out from a climb point across open cavern floor, noting each tile's steps to its nearest climb point.</summary>
    private void SpreadClimb(Entity<BiomeComponent, MapGridComponent> level, Vector2i climb)
    {
        if (_climbSteps.TryGetValue(climb, out var known) && known == 0)
            return;

        Entity<MapGridComponent> levelGrid = (level.Owner, level.Comp2);
        _climbSteps[climb] = 0;
        _climbFrontier.Enqueue(climb);

        while (_climbFrontier.TryDequeue(out var index))
        {
            var steps = _climbSteps[index] + 1;
            if (steps > ClimbReach)
                continue;

            foreach (var side in Cardinals)
            {
                var next = index + side;
                if (_climbSteps.TryGetValue(next, out var seen) && seen <= steps
                    || IsEmpty(level, next)
                    || HasHardAnchored(levelGrid, next))
                    continue;

                _climbSteps[next] = steps;
                _climbFrontier.Enqueue(next);
            }
        }
    }

    /// <summary>Whether a ground tile lies inside a mouth's hole.</summary>
    private static bool InMouthHole(Entity<WFCavernGroundComponent> ground, Vector2i index)
    {
        foreach (var mouth in ground.Comp.Mouths)
        {
            if (mouth.Contains(index))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Anchors a climb point on a hole's first solid neighbour: the four sides first, the climb side leading, then the
    /// corners. With none free the hole has no climb point of its own.
    /// </summary>
    // The cleared landing joins every neighbour to the tile below the hole.
    private bool TrySpawnClimbNear(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i hole, out Vector2i climb)
    {
        climb = default;
        var mapId = Comp<MapComponent>(ground).MapId;
        Entity<MapGridComponent> groundGrid = (context.Ground.Owner, context.Ground.Comp2);
        Entity<MapGridComponent> levelGrid = (context.Level.Owner, context.Level.Comp2);

        foreach (var offset in ClimbOffsets(context.Spec.ClimbSide.ToIntVec()))
        {
            var index = hole + offset;

            if (IsEmpty(context.Ground, index)
                || ground.Comp.Shades.ContainsKey(index)
                || HasHardAnchored(groundGrid, index)
                || UnderGrid(mapId, index)
                || IsEmpty(context.Level, index)
                || HasHardAnchored(levelGrid, index))
                continue;

            SpawnClimb(ground, context, levelGrid, index);
            climb = index;
            return true;
        }

        return false;
    }

    /// <summary>A hole's neighbours in the order its climb point tries them: the climb side, the two beside it, the far side, then the corners.</summary>
    private static IEnumerable<Vector2i> ClimbOffsets(Vector2i side)
    {
        var across = new Vector2i(-side.Y, side.X);

        yield return side;
        yield return across;
        yield return -across;
        yield return -side;
        yield return side + across;
        yield return side - across;
        yield return -side + across;
        yield return -side - across;
    }

    /// <summary>
    /// Wakes the z-physics bodies over a ground tile and has them read the ground again, so sleeping items and mobs
    /// fall through the new hole.
    /// </summary>
    // Never what is inside them: a woken limb would fall out of its body.
    private void WakeBodiesOn(Entity<BiomeComponent, MapGridComponent> ground, Vector2i index)
    {
        _bodies.Clear();
        _lookup.GetEntitiesIntersecting(Comp<MapComponent>(ground).MapId, TileBox(index), _bodies, LookupFlags.Uncontained);

        foreach (var body in _bodies)
        {
            _zLevels.WfRecacheGround(body);
        }
    }

    /// <summary>Whether a map has no tile at an index.</summary>
    private bool IsEmpty(Entity<BiomeComponent, MapGridComponent> map, Vector2i index)
    {
        return !_map.TryGetTileRef(map.Owner, map.Comp2, index, out var tile) || tile.Tile.IsEmpty;
    }

    /// <summary>Whether a map's tile at an index is the one its biome would put there.</summary>
    private bool IsNatural(Entity<BiomeComponent, MapGridComponent> map, Vector2i index)
    {
        return _map.TryGetTileRef(map.Owner, map.Comp2, index, out var tile)
               && _biome.TryGetTile(index, map.Comp1.Layers, map.Comp1.Seed, NoGrid, out var natural)
               && natural.Value.TypeId == tile.Tile.TypeId;
    }

    /// <summary>The tile a map's biome would put at an index, or a fallback where it puts none.</summary>
    private Tile NaturalOr(Entity<BiomeComponent, MapGridComponent> map, Vector2i index, Tile fallback)
    {
        return _biome.TryGetTile(index, map.Comp1.Layers, map.Comp1.Seed, NoGrid, out var natural) ? natural.Value : fallback;
    }

    /// <summary>Whether anything anchored on a tile would stop a mob standing there.</summary>
    private bool HasHardAnchored(Entity<MapGridComponent> grid, Vector2i index)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, index);

        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<PhysicsComponent>(uid, out var body) && body.CanCollide && body.Hard)
                return true;
        }

        return false;
    }

    /// <summary>Whether a grid other than the map covers a tile.</summary>
    private bool UnderGrid(MapId mapId, Vector2i index)
    {
        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(mapId, TileBox(index), ref grids, approx: true, includeMap: false);
        return grids.Count > 0;
    }
}
