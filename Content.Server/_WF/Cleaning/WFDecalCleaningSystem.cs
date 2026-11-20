using System.Linq;
using System.Numerics;
using Content.Server.Decals;
using Content.Shared.Decals;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Cleaning;

/// <summary>
/// Decides which decals cleaning takes and washes them off a tile, for space cleaner, mops and cleanbots.
/// </summary>
public sealed partial class WFDecalCleaningSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Reagent units one decal costs to wash away, as <c>CleanDecalsReaction</c> charges.</summary>
    public const float DecalCleanCost = 0.25f;

    /// <summary>
    /// Whether cleaning takes this decal: its own flag, or its prototype's default. Many mapped dirt, blood and
    /// footprint decals were saved with the flag off.
    /// </summary>
    public bool IsCleanable(Decal decal)
    {
        return decal.Cleanable
               || _prototypes.TryIndex<DecalPrototype>(decal.Id, out var proto) && proto.DefaultCleanable;
    }

    /// <summary>Whether a wash of this tile would find a decal to take.</summary>
    public bool HasCleanableDecals(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!TryComp(gridUid, out DecalGridComponent? decalGrid))
            return false;

        foreach (var (_, decal) in _decals.GetDecalsIntersecting(gridUid, WashBounds(tile, grid), decalGrid))
        {
            if (IsCleanable(decal))
                return true;
        }

        return false;
    }

    /// <summary>Washes cleanable decals off one tile, up to what the budget pays for. Returns the units spent.</summary>
    public float CleanTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile, float budget)
    {
        if (!TryComp(gridUid, out DecalGridComponent? decalGrid))
            return 0f;

        var spent = 0f;
        foreach (var (index, decal) in _decals.GetDecalsIntersecting(gridUid, WashBounds(tile, grid), decalGrid).ToList())
        {
            if (spent + DecalCleanCost > budget)
                break;

            if (!IsCleanable(decal))
                continue;

            _decals.RemoveDecal(gridUid, index, decalGrid);
            spent += DecalCleanCost;
        }

        return spent;
    }

    /// <summary>
    /// Picks one of the nearest dirty tiles within range of a point on a grid, at random so an unreachable one
    /// doesn't hold a cleanbot forever.
    /// </summary>
    public bool TryPickDirtyTile(EntityUid gridUid, Vector2 localPos, float range, int choices, out Vector2i tile)
    {
        tile = default;
        if (!TryComp(gridUid, out DecalGridComponent? decalGrid))
            return false;

        // GetDecalsInRange only looks in one chunk, so this takes a box and trims it to a circle.
        var tiles = new HashSet<Vector2i>();
        var bounds = Box2.CenteredAround(localPos, new Vector2(range * 2f, range * 2f));
        foreach (var (_, decal) in _decals.GetDecalsIntersecting(gridUid, bounds, decalGrid))
        {
            if (!IsCleanable(decal) || (decal.Coordinates + new Vector2(0.5f, 0.5f) - localPos).Length() > range)
                continue;

            tiles.Add(new Vector2i((int) MathF.Floor(decal.Coordinates.X + 0.5f), (int) MathF.Floor(decal.Coordinates.Y + 0.5f)));
        }

        if (tiles.Count == 0)
            return false;

        var nearest = tiles
            .OrderBy(t => ((Vector2) t + new Vector2(0.5f, 0.5f) - localPos).LengthSquared())
            .Take(choices)
            .ToList();
        tile = nearest[_random.Next(nearest.Count)];
        return true;
    }

    /// <summary>The reach of one wash: the tile and half a tile around it, as <c>CleanDecalsReaction</c> has it.</summary>
    private Box2 WashBounds(Vector2i tile, MapGridComponent grid)
    {
        return _lookup.GetLocalBounds(tile, grid.TileSize).Enlarged(0.5f).Translated(new Vector2(-0.5f, -0.5f));
    }
}
