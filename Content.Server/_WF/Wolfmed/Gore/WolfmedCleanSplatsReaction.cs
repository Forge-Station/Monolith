using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Wolfmed.Gore;

/// <summary>
/// Washes Wolfmed's wall splats off a tile. The floor half is a cleanable decal and is already covered by
/// <c>CleanDecalsReaction</c>; this is the entity half, so one reagent and one mop stroke take both.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedCleanSplats : ITileReaction
{
    public FixedPoint2 TileReact(
        TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        if (!entityManager.TryGetComponent<MapGridComponent>(tile.GridUid, out var grid))
            return FixedPoint2.Zero;

        var spent = entityManager.System<WolfmedGoreSystem>()
            .CleanTile(tile.GridUid, grid, tile.GridIndices, reactVolume.Float());

        return FixedPoint2.New(spent);
    }
}
