using System.Numerics;
using Content.Client.Decals;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Mapping;

public sealed class MappingEyedropperSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _maps = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private DecalSystem _decals = default!;
    private SpriteSystem _sprites = default!;

    public override void Initialize()
    {
        base.Initialize();
        _decals = EntityManager.System<DecalSystem>();
        _sprites = EntityManager.System<SpriteSystem>();
    }

    public bool TryPick(
        EntityCoordinates coords,
        EntityUid hovered,
        MappingEyedropperMode mode,
        out IPrototype? prototype)
    {
        return TryPick(coords, hovered, mode, out prototype, out _);
    }

    public bool TryPick(
        EntityCoordinates coords,
        EntityUid hovered,
        MappingEyedropperMode mode,
        out IPrototype? prototype,
        out Decal? sampledDecal)
    {
        prototype = null;
        sampledDecal = null;

        // RMB on UI (or a predicted replay) arrives as EntityUid.Invalid — never convert those coords.
        var coordsValid = _transform.IsValid(coords);
        if (!coordsValid && mode is MappingEyedropperMode.Tile or MappingEyedropperMode.Decal)
            return false;

        switch (mode)
        {
            case MappingEyedropperMode.Entity:
                return TryPickEntity(hovered, out prototype);
            case MappingEyedropperMode.Tile:
                return TryPickTile(coords, out prototype);
            case MappingEyedropperMode.Decal:
                return TryPickDecal(coords, out prototype, out sampledDecal);
            default:
                // Copy what is visually under the cursor: entities, then decals, then the floor tile.
                // Tiles always exist on a grid, so checking them first made tinted decals un-pickable.
                if (TryPickEntity(hovered, out prototype))
                    return true;

                if (!coordsValid)
                    return false;

                if (TryPickDecal(coords, out prototype, out sampledDecal))
                    return true;

                return TryPickTile(coords, out prototype);
        }
    }

    public Direction? GetEntityDirection(EntityUid uid)
    {
        if (!uid.IsValid() || !TryComp(uid, out TransformComponent? xform))
            return null;

        return xform.LocalRotation.GetDir();
    }

    private bool TryPickEntity(EntityUid hovered, out IPrototype? prototype)
    {
        prototype = null;

        if (!hovered.IsValid() || hovered == _players.LocalEntity)
            return false;

        if (!TryComp(hovered, out MetaDataComponent? meta) ||
            meta.EntityDeleted ||
            meta.EntityPrototype is not { } entity ||
            entity.Abstract ||
            entity.HideSpawnMenu)
        {
            return false;
        }

        prototype = entity;
        return true;
    }

    private bool TryPickTile(EntityCoordinates coords, out IPrototype? prototype)
    {
        prototype = null;
        if (!_transform.IsValid(coords))
            return false;

        var mapPos = _transform.ToMapCoordinates(coords, logError: false);

        if (!_maps.TryFindGridAt(mapPos, out var gridUid, out var grid) ||
            !_mapSystem.TryGetTileRef(gridUid, grid, coords, out var tileRef))
        {
            return false;
        }

        prototype = _turf.GetContentTileDefinition(tileRef);
        return true;
    }

    private bool TryPickDecal(EntityCoordinates coords, out IPrototype? prototype, out Decal? sampledDecal)
    {
        prototype = null;
        sampledDecal = null;
        if (!_transform.IsValid(coords))
            return false;

        var mapPos = _transform.ToMapCoordinates(coords, logError: false);

        if (!_maps.TryFindGridAt(mapPos, out var gridUid, out _))
            return false;

        var local = _transform.ToCoordinates(gridUid, mapPos);
        var point = local.Position;
        var decals = _decals.GetDecalsNear(gridUid, point, 2f);
        if (decals.Count == 0)
            return false;

        Decal? bestDecal = null;
        var bestZ = int.MinValue;
        uint bestId = 0;
        var bestDist = float.MaxValue;
        var hadHit = false;

        foreach (var (id, decal) in decals)
        {
            if (!SpriteContains(decal, point, out var distSq))
                continue;

            // Same sprite stack: the overlay draws higher Z, then newer ids, on top.
            if (hadHit)
            {
                if (decal.ZIndex < bestZ)
                    continue;
                if (decal.ZIndex == bestZ && id < bestId)
                    continue;
                if (decal.ZIndex == bestZ && id == bestId && distSq >= bestDist)
                    continue;
            }

            hadHit = true;
            bestZ = decal.ZIndex;
            bestId = id;
            bestDist = distSq;
            bestDecal = decal;
        }

        if (bestDecal == null || !_prototypes.TryIndex<DecalPrototype>(bestDecal.Id, out var decalProto))
            return false;

        prototype = decalProto;
        sampledDecal = bestDecal;
        return true;
    }

    /// <summary>
    ///     True if the cursor sits on this decal's drawn sprite. distSq is to the sprite centre for tie-breaks.
    /// </summary>
    private bool SpriteContains(Decal decal, Vector2 point, out float distSq)
    {
        distSq = float.MaxValue;
        if (!_prototypes.TryIndex<DecalPrototype>(decal.Id, out var proto))
            return false;

        var texture = _sprites.Frame0(proto.Sprite);
        var size = texture.Size / (float) EyeManager.PixelsPerMeter;
        if (size.X <= 0f || size.Y <= 0f)
            size = Vector2.One;

        var box = Box2.FromDimensions(decal.Coordinates, size);
        var center = box.Center;
        distSq = (point - center).LengthSquared();

        if (decal.Angle.Equals(Angle.Zero))
            return box.Contains(point);

        var local = (-decal.Angle).RotateVec(point - center) + center;
        return box.Contains(local);
    }
}
