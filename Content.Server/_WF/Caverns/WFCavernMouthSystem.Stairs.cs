using Content.Shared._WF.Caverns;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Caverns;

public sealed partial class WFCavernMouthSystem
{
    [Dependency] private WFCavernDigSystem _dig = default!;

    /// <summary>Whether stairs in the cavern under a ground tile could open it and come out onto the exit tile beside it.</summary>
    public WFCavernStairsRefusal CheckStairs(Entity<WFCavernGroundComponent> ground, Vector2i index, Vector2i exit)
    {
        return TryGetContext(ground, out var context)
            ? CheckStairs(ground, context, index, exit)
            : WFCavernStairsRefusal.Cavern;
    }

    /// <summary>
    /// Opens the ground tile over stairs and keeps the exit tile beside it as solid, clear ground: what the biome grew
    /// on either goes, and both are pinned. A tile that is already a hole is kept.
    /// </summary>
    // The hole queue fits the hole out, without the landing and climb point that stairs stand in for.
    public WFCavernStairsRefusal TryOpenStairs(Entity<WFCavernGroundComponent> ground, Vector2i index, Vector2i exit)
    {
        if (!TryGetContext(ground, out var context))
            return WFCavernStairsRefusal.Cavern;

        var refusal = CheckStairs(ground, context, index, exit);
        if (refusal != WFCavernStairsRefusal.None)
            return refusal;

        var groundBiome = context.Ground.Comp1;
        var both = new[] { index, exit };
        var tiles = new List<(Vector2i, Tile)>();

        ClearBiomeEntities(context.Ground, both);

        if (!IsEmpty(context.Ground, index))
            tiles.Add((index, Tile.Empty));

        // Ground that isn't loaded: the exit is laid now, so it is there whoever comes up first.
        if (IsEmpty(context.Ground, exit) && _biome.TryGetTile(exit, groundBiome.Layers, groundBiome.Seed, NoGrid, out var natural))
            tiles.Add((exit, natural.Value));

        // Pinned first, so the hole queue takes the hole on ground that isn't loaded too.
        _biome.WfPinTiles((context.Ground.Owner, groundBiome), both);

        if (tiles.Count > 0)
            _map.SetTiles(context.Ground.Owner, context.Ground.Comp2, tiles);

        ground.Comp.Opened.Add(index);
        return WFCavernStairsRefusal.None;
    }

    /// <summary>Whether the ground tile over stairs is an open hole with no hull parked on it.</summary>
    public bool IsOpenAbove(Entity<WFCavernGroundComponent> ground, Vector2i index)
    {
        return ground.Comp.Shades.ContainsKey(index) && !UnderGrid(Comp<MapComponent>(ground).MapId, index);
    }

    /// <summary>Has the hole queue give a hole the landing and climb point it went without while stairs stood under it.</summary>
    public void RefitHole(Entity<WFCavernGroundComponent> ground, Vector2i index)
    {
        if (ground.Comp.Shades.ContainsKey(index))
            ground.Comp.Refit.Add(index);
    }

    private WFCavernStairsRefusal CheckStairs(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i index, Vector2i exit)
    {
        if (UnderGrid(Comp<MapComponent>(ground).MapId, index))
            return WFCavernStairsRefusal.Hull;

        // Solid ground opens only where it is still the world's own, with nothing standing on it.
        if (!IsEmpty(context.Ground, index) && (!IsBareGround(context.Ground, index) || HasBuilt(context.Ground, index)))
            return WFCavernStairsRefusal.Built;

        if (ground.Comp.Shades.ContainsKey(exit))
            return WFCavernStairsRefusal.Exit;

        if (!IsEmpty(context.Ground, exit))
            return HasBuilt(context.Ground, exit) ? WFCavernStairsRefusal.Exit : WFCavernStairsRefusal.None;

        // Empty: a hole not yet fitted out, ground the biome never lays, or ground that isn't loaded and is laid on opening.
        var groundBiome = (context.Ground.Owner, context.Ground.Comp1);
        return _biome.WfIsChunkLoaded(groundBiome, exit)
               || _biome.WfIsPinned(groundBiome, exit)
               || !_biome.TryGetTile(exit, context.Ground.Comp1.Layers, context.Ground.Comp1.Seed, NoGrid, out _)
            ? WFCavernStairsRefusal.Exit
            : WFCavernStairsRefusal.None;
    }

    /// <summary>Gives holes that lost their stairs the landing and climb point every other hole has.</summary>
    private void RefitHoles(Entity<WFCavernGroundComponent> ground, MouthContext context)
    {
        if (ground.Comp.Refit.Count == 0)
            return;

        Entity<MapGridComponent> levelGrid = (context.Level.Owner, context.Level.Comp2);
        var holes = new List<Vector2i>();

        foreach (var index in ground.Comp.Refit)
        {
            if (ground.Comp.Shades.ContainsKey(index) && IsEmpty(context.Ground, index) && !HasStairs(levelGrid, index))
                holes.Add(index);
        }

        ground.Comp.Refit.Clear();

        if (holes.Count == 0)
            return;

        holes.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        PrepareLandings(context, holes);
        EnsureClimbs(ground, context, holes);
    }

    /// <summary>Whether stairs stand on a cavern tile.</summary>
    private bool HasStairs(Entity<MapGridComponent> level, Vector2i index)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(level, level.Comp, index);

        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<WFCavernStairsComponent>(uid) && !TerminatingOrDeleted(uid))
                return true;
        }

        return false;
    }

    /// <summary>Whether a map's tile is its natural one or what digging that down leaves, not something laid on it.</summary>
    private bool IsBareGround(Entity<BiomeComponent, MapGridComponent> map, Vector2i index)
    {
        return _map.TryGetTileRef(map.Owner, map.Comp2, index, out var tile)
               && _biome.TryGetTile(index, map.Comp1.Layers, map.Comp1.Seed, NoGrid, out var natural)
               && _dig.DugFrom((ContentTileDefinition) _tileDefs[natural.Value.TypeId], (ContentTileDefinition) _tileDefs[tile.Tile.TypeId]);
    }

    /// <summary>Whether something the biome didn't grow is anchored on a tile and would stop a mob standing there.</summary>
    private bool HasBuilt(Entity<BiomeComponent, MapGridComponent> map, Vector2i index)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(map.Owner, map.Comp2, index);

        while (anchored.MoveNext(out var uid))
        {
            if (!_biome.WfIsBiomeSpawned((map.Owner, map.Comp1), uid.Value, index)
                && TryComp<PhysicsComponent>(uid, out var body)
                && body.CanCollide
                && body.Hard)
                return true;
        }

        return false;
    }
}

/// <summary>Why stairs can't open the ground above them.</summary>
public enum WFCavernStairsRefusal : byte
{
    /// <summary>They can.</summary>
    None,

    /// <summary>The ground or its cavern is gone.</summary>
    Cavern,

    /// <summary>A hull is parked on the tile above.</summary>
    Hull,

    /// <summary>The tile above is laid floor, or something built stands on it.</summary>
    Built,

    /// <summary>The tile at the top of the stairs is a hole, or something built stands on it.</summary>
    Exit,
}
