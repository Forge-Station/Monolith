// Forge-Change-full: dungeon post-gen that swaps anchored entities and can repaint room floors.
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.PostGeneration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// <see cref="EntityReplaceDunGen"/>
    /// </summary>
    private async Task PostGen(EntityReplaceDunGen gen, Dungeon dungeon, Random random)
    {
        if (gen.Tile is { } tileId)
        {
            var tileDef = (ContentTileDefinition) _tileDefManager[tileId];
            var batch = new List<(Vector2i, Tile)>(256);

            foreach (var index in dungeon.RoomTiles)
            {
                batch.Add((index, _tile.GetVariantTile(tileDef, random)));

                if (batch.Count < 256)
                    continue;

                _maps.SetTiles(_gridUid, _grid, batch);
                batch.Clear();
                await SuspendDungeon();

                if (!ValidateResume())
                    return;
            }

            if (batch.Count > 0)
                _maps.SetTiles(_gridUid, _grid, batch);

            if (_entManager.TryGetComponent(_gridUid, out DecalGridComponent? decalGrid) &&
                TryGetRoomBounds(dungeon, out var area))
            {
                var decalIds = new List<uint>();

                foreach (var (id, decal) in _decals.GetDecalsIntersecting(_gridUid, area, decalGrid))
                {
                    if (dungeon.RoomTiles.Contains(decal.Coordinates.Floored()))
                        decalIds.Add(id);
                }

                for (var i = 0; i < decalIds.Count; i++)
                {
                    _decals.RemoveDecal(_gridUid, decalIds[i], decalGrid);

                    if (i % 64 != 0)
                        continue;

                    await SuspendDungeon();

                    if (!ValidateResume())
                        return;
                }
            }
        }

        var anchored = new List<EntityUid>();
        var deletions = new List<EntityUid>();
        var pending = new List<(EntityUid Entity, EntProtoId Replacement)>();

        foreach (var tile in dungeon.RoomTiles)
        {
            anchored.Clear();
            _maps.GetAnchoredEntities((_gridUid, _grid), tile, anchored);

            foreach (var ent in anchored)
            {
                if (!_entManager.TryGetComponent(ent, out MetaDataComponent? meta) ||
                    meta.EntityPrototype is not { } proto)
                {
                    continue;
                }

                if (gen.Removals.Contains(proto.ID))
                {
                    deletions.Add(ent);
                    continue;
                }

                if (!gen.Replacements.TryGetValue(proto.ID, out var replacement))
                    continue;

                pending.Add((ent, replacement));
            }
        }

        var work = 0;

        foreach (var ent in deletions)
        {
            _entManager.DeleteEntity(ent);

            if (++work % 24 != 0)
                continue;

            await SuspendDungeon();

            if (!ValidateResume())
                return;
        }

        foreach (var (ent, replacement) in pending)
        {
            if (!_xformQuery.TryGetComponent(ent, out var xform))
                continue;

            var coords = xform.Coordinates;
            var rotation = xform.LocalRotation;
            var wasAnchored = xform.Anchored;
            _entManager.DeleteEntity(ent);

            var spawned = _entManager.SpawnEntity(replacement, coords);
            var spawnedXform = _xformQuery.GetComponent(spawned);
            _transform.SetLocalRotation(spawned, rotation, spawnedXform);

            if (wasAnchored && !spawnedXform.Anchored)
                _transform.AnchorEntity((spawned, spawnedXform), (_gridUid, _grid));

            if (++work % 24 != 0)
                continue;

            await SuspendDungeon();

            if (!ValidateResume())
                return;
        }

        if (gen.CorridorEntity is { } corridorEntity)
        {
            foreach (var tile in dungeon.CorridorTiles)
            {
                if (HasHardAnchored(tile))
                    continue;

                var coords = _maps.ToCenterCoordinates(_gridUid, tile, _grid);
                _entManager.SpawnEntity(corridorEntity, coords);

                if (++work % 24 != 0)
                    continue;

                await SuspendDungeon();

                if (!ValidateResume())
                    return;
            }
        }

        await SuspendDungeon();
    }

    private bool HasHardAnchored(Vector2i tile)
    {
        var anchored = new List<EntityUid>();
        _maps.GetAnchoredEntities((_gridUid, _grid), tile, anchored);

        foreach (var ent in anchored)
        {
            if (_physicsQuery.TryGetComponent(ent, out var physics) && physics.Hard)
                return true;
        }

        return false;
    }

    private static bool TryGetRoomBounds(Dungeon dungeon, out Box2 area)
    {
        var hasBounds = false;
        area = default;

        foreach (var room in dungeon.Rooms)
        {
            var box = new Box2(room.Bounds.Left, room.Bounds.Bottom, room.Bounds.Right, room.Bounds.Top);
            area = hasBounds ? area.Union(box) : box;
            hasBounds = true;
        }

        return hasBounds;
    }
}
