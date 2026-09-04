using System.Linq;
using System.Numerics;
using Content.Client.Damage;
using Content.Client.IconSmoothing;
using Content.Client.Mind;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Mono.Shipyard;

/// <summary>
/// This handles spawning client-side grid and getting data from it.
/// </summary>
public sealed class ShipyardPreviewSystem : SharedShipyardPreviewSystem
{
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IconSmoothSystem _iconSmooth = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;

    public Entity<MapGridComponent>? CurrentGrid;

    public override void Initialize()
    {
        base.Initialize();
    }

    public bool TryPreviewGrid(VesselPrototype vessel)
    {
        CachePreviewMap();

        if (_previewMap == MapId.Nullspace || !_map.MapExists(_previewMap))
            return false;

        // Forge-Change: delete the previously previewed grid before loading a new one.
        // Otherwise every preview click leaks a full client-side shuttle grid onto the
        // (persistent) preview map, since Dispose() only ever deletes the last CurrentGrid.
        if (CurrentGrid != null)
        {
            Del(CurrentGrid.Value.Owner);
            CurrentGrid = null;
        }

        var opts = new DeserializationOptions();
        if (!_loader.TryLoadGrid(_previewMap,
                vessel.ShuttlePath,
                out var grid,
                opts))
            return false;

        _xform.SetMapCoordinates(grid.Value, new MapCoordinates(Vector2.Zero, _previewMap));
        _meta.SetEntityName(grid.Value, vessel.Name);
        CurrentGrid = grid.Value;

        // Client map-load adds Sprite before CopyTo fills rsi/layerDatums, so BaseRSI and
        // layers never get built. Rebuild them from the prototype template (which was
        // properly initialized at prototype-load time).
        RebuildPreviewSprites(grid.Value.Owner);
        return true;
    }

    /// <summary>
    /// Fix sprites that client-side <see cref="MapLoaderSystem.TryLoadGrid"/> left uninitialized.
    /// Without this, tiles/decals render but entity sprites have zero layers.
    /// </summary>
    private void RebuildPreviewSprites(EntityUid gridUid)
    {
        var query = AllEntityQuery<SpriteComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var sprite, out var xform, out var meta))
        {
            if (uid != gridUid && xform.GridUid != gridUid)
                continue;

            // Client map-load leaves BaseRSI null. If BaseRSI is already set, this sprite
            // was initialized normally (not via the broken deserialize path).
            if (sprite.BaseRSI != null)
                continue;

            if (meta.EntityPrototype is not { } proto
                || !proto.Components.TryGetComponent("Sprite", out var protoComp)
                || protoComp is not SpriteComponent protoSprite)
                continue;

            // Prototype sprites got BaseRSI during ResourceCache.AfterDeserialization at startup.
            if (protoSprite.BaseRSI != null)
                _sprite.SetBaseRsi((uid, sprite), protoSprite.BaseRSI);

            // DamageVisuals (and similar) may have added overlay layers during ComponentInit
            // onto an otherwise empty sprite. CopySprite only calls LoadPrototypeData when
            // Layers.Count == 0, so strip those overlays first or the window layer never
            // rebuilds and IconSmooth writes state0 onto cracks_diagonal.rsi.
            while (sprite.AllLayers.Any())
                sprite.RemoveLayer(0);

            _sprite.CopySprite((uid, sprite), (uid, sprite));

            // Overlay layers were wiped above; disable so Appearance updates don't retarget
            // layer 0. Missing cracks in a shipyard preview is fine.
            if (TryComp(uid, out DamageVisualsComponent? damageVis))
                damageVis.Valid = false;

            // IconSmooth walls have no layerDatums — corners are added at Startup, which ran
            // before BaseRSI existed. Re-run corner setup now that BaseRSI is set.
            if (TryComp(uid, out IconSmoothComponent? smooth)
                && smooth.Mode == IconSmoothingMode.Corners
                && sprite.BaseRSI != null)
            {
                _iconSmooth.SetStateBase(uid, smooth, smooth.StateBase);
            }

            if (TryComp(uid, out AppearanceComponent? appearance))
                _appearance.QueueUpdate(uid, appearance);
        }
    }

    public FormattedMessage GetGridData()
    {
        var msg = new FormattedMessage();
        if (CurrentGrid == null)
            return msg;

        msg.AddMarkupOrThrow(
            Loc.GetString("shipyard-preview-tile-count", ("count", _map.GetAllTiles(CurrentGrid.Value.Owner, CurrentGrid.Value.Comp).Count().ToString()))
            );

        return msg;
    }

    public void Dispose()
    {
        Del(CurrentGrid);
        CurrentGrid = null;
    }
}
