using System.Numerics;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

public sealed partial class WFCrewWorkSystem
{
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    /// <summary>Missing snapshot tiles and structures are rebuilt by the normal SRD interaction; <paramref name="only"/> rechecks one of them.</summary>
    private IEnumerable<EntityCoordinates?> MissingStructure(EntityUid grid, EntityUid worker, EntityCoordinates? only = null)
    {
        if (!TryComp<ShipRepairDataComponent>(grid, out var data) || !TryComp<MapGridComponent>(grid, out var map)
            || !TryComp<ShipRepairToolComponent>(worker, out var tool)
            || TryComp<ShipRepairRestrictComponent>(grid, out var restriction)
                && _whitelist.IsWhitelistFail(restriction.ToolWhitelist, worker))
            yield break;
        Vector2i? onlyTile = only is { } wanted
            ? new Vector2i((int) MathF.Floor(wanted.Position.X), (int) MathF.Floor(wanted.Position.Y))
            : null;
        foreach (var (chunkIndex, chunk) in data.Chunks)
        {
            if (onlyTile is not { } single
                || chunkIndex == new Vector2i(FloorDiv(single.X, data.ChunkSize), FloorDiv(single.Y, data.ChunkSize)))
            {
                for (var y = 0; y < data.ChunkSize; y++)
                for (var x = 0; x < data.ChunkSize; x++)
                {
                    var stored = chunk.Tiles[x + y * data.ChunkSize];
                    var tile = chunkIndex * data.ChunkSize + new Vector2i(x, y);
                    if (onlyTile is { } expected && tile != expected)
                        continue;
                    if (tool.EnableTileRepair && stored != 0 && _maps.GetTileRef(grid, map, tile).Tile.TypeId != stored)
                        yield return new EntityCoordinates(grid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
                }
            }
            foreach (var spec in chunk.Entities.Values)
            {
                if (only is { } at && spec.LocalPosition != at.Position)
                    continue;
                if (!tool.EnableEntityRepair || !_prototypes.TryIndex(data.EntityPalette[spec.ProtoIndex], out var prototype)
                    || !prototype.TryComp<ShipRepairableComponent>(out _, Factory)
                    || prototype.TryComp<ShipRepairableRestrictComponent>(out var restricted, Factory)
                        && _whitelist.IsWhitelistFail(restricted.ToolWhitelist, worker))
                    continue;
                if (spec.OriginalEntity is not { } original || !TryGetEntity(original, out var entity)
                    || entity is not { } uid || TerminatingOrDeleted(uid))
                    yield return new EntityCoordinates(grid, spec.LocalPosition);
            }
        }
    }

    private static int FloorDiv(int value, int divisor) => (int) MathF.Floor(value / (float) divisor);
}
