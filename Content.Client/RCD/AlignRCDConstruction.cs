using System.Numerics;
using Content.Client.Gameplay;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.Utility;
using static Robust.Client.Placement.PlacementManager;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Client.RCD;

public sealed class AlignRCDConstruction : SnapgridCenter
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly RCDSystem _rcdSystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly SharedAtmosPipeLayersSystem _pipeLayersSystem;
    private readonly RCDConstructionGhostSystem _ghostSystem;
    private readonly SpriteSystem _spriteSystem;

    private const float PlaceColorBaseAlpha = 0.5f;
    private const float SearchBoxSize = 2f;

    private EntityCoordinates _unalignedMouseCoords = default;
    private AtmosPipeLayer _currentPipeLayer = AtmosPipeLayer.Primary;

    private readonly Color _guideColor = new(0, 0, 0.5785f);
    private const float GuideRadius = 0.1f;

    public override bool RangeRequired => true;

    public AlignRCDConstruction(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);
        _mapSystem = _entityManager.System<SharedMapSystem>();
        _rcdSystem = _entityManager.System<RCDSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _pipeLayersSystem = _entityManager.System<SharedAtmosPipeLayersSystem>();
        _ghostSystem = _entityManager.System<RCDConstructionGhostSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();

        ValidPlaceColor = ValidPlaceColor.WithAlpha(PlaceColorBaseAlpha);
    }

    public override void Render(in OverlayDrawArgs args)
    {
        if (ShouldHidePlacementOverlay())
            return;

        if (!ShouldDrawPipeLayerGuides(out var gridUid, out var mapGrid))
        {
            base.Render(args);
            return;
        }

        if (pManager.PlacementType == PlacementTypes.None)
        {
            _pipeLayersSystem.GetPipeLayerGuideWorldPositions(
                gridUid,
                mapGrid,
                MouseCoords,
                _eyeManager.CurrentEye.Rotation,
                out var center,
                out var secondary,
                out var tertiary);

            args.WorldHandle.DrawCircle(center.Position, GuideRadius, _guideColor);
            args.WorldHandle.DrawCircle(secondary.Position, GuideRadius, _guideColor);
            args.WorldHandle.DrawCircle(tertiary.Position, GuideRadius, _guideColor);
        }

        base.Render(args);
    }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        _unalignedMouseCoords = ScreenToCursorGrid(mouseScreen);

        if (pManager.PlacementType != PlacementTypes.None)
        {
            base.AlignPlacementMode(mouseScreen);
            return;
        }

        // Forge-Change: do not chain SnapgridCenter + tile-index offset (ghost lands on 2x2 junction)
        MouseCoords = _unalignedMouseCoords.AlignWithClosestGridTile(SearchBoxSize, _entityManager, _mapManager);

        var gridId = _transformSystem.GetGrid(MouseCoords);

        if (!_entityManager.TryGetComponent<MapGridComponent>(gridId, out var mapGrid))
            return;

        CurrentTile = _mapSystem.GetTileRef(gridId.Value, mapGrid, MouseCoords);
        GridDistancing = mapGrid.TileSize;
        SnapSize = mapGrid.TileSize;
        Grid = mapGrid;

        if (pManager.CurrentPermission is { IsTile: false } && pManager.PlacementOffset != Vector2i.Zero)
        {
            MouseCoords = new EntityCoordinates(
                MouseCoords.EntityId,
                MouseCoords.Position + new Vector2(pManager.PlacementOffset.X, pManager.PlacementOffset.Y));
        }

        UpdateRpdPipeLayer();
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        var player = _playerManager.LocalSession?.AttachedEntity;

        if (!_entityManager.TryGetComponent<TransformComponent>(player, out var xform))
            return false;

        if (!_transformSystem.InRange(xform.Coordinates, position, SharedInteractionSystem.InteractionRange))
        {
            InvalidPlaceColor = InvalidPlaceColor.WithAlpha(0);
            return false;
        }

        InvalidPlaceColor = InvalidPlaceColor.WithAlpha(PlaceColorBaseAlpha);

        if (!_entityManager.TryGetComponent<HandsComponent>(player, out var hands))
            return false;

        var heldEntity = hands.ActiveHand?.HeldEntity;

        if (!_entityManager.TryGetComponent<RCDComponent>(heldEntity, out var rcd))
            return false;

        if (!_rcdSystem.TryGetMapGridData(position, out var mapGridData))
            return false;

        var currentState = _stateManager.CurrentState;

        if (currentState is not GameplayStateBase screen)
            return false;

        var worldClick = _transformSystem.ToMapCoordinates(_unalignedMouseCoords);
        EntityUid? target = null;

        if (rcd.IsRpd && rcd.CachedPrototype.Mode == RcdMode.Deconstruct)
        {
            var clicked = screen.GetClickedEntity(worldClick);
            _rcdSystem.TryResolveRpdDeconstructTarget(rcd, mapGridData.Value, worldClick, clicked, out target);
        }
        else
            target = screen.GetClickedEntity(worldClick);

        string? rpdConstructionEntity = null;
        if (rcd.IsRpd && rcd.CachedPrototype.Mode == RcdMode.ConstructObject)
        {
            if (ShouldDrawPipeLayerGuides(out _, out _)
                && _protoManager.TryIndex<EntityPrototype>(rcd.CachedPrototype.Prototype!, out var baseProto)
                && baseProto.TryGetComponent<AtmosPipeLayersComponent>(out var layers, _entityManager.ComponentFactory)
                && _pipeLayersSystem.TryGetAlternativePrototype(layers, _currentPipeLayer, out var layerProto))
            {
                rpdConstructionEntity = layerProto;
            }
            else if (rcd.ConstructionEntity != null
                && _rcdSystem.IsValidRpdConstructionEntity(rcd, rcd.ConstructionEntity))
            {
                rpdConstructionEntity = rcd.ConstructionEntity;
            }
        }

        if (!_rcdSystem.IsRCDOperationStillValid(heldEntity.Value, rcd, mapGridData.Value, target, player.Value, false, rpdConstructionEntity))
            return false;

        return true;
    }

    private void UpdateRpdPipeLayer()
    {
        if (!ShouldDrawPipeLayerGuides(out var gridUid, out var mapGrid))
            return;

        if (!_entityManager.TryGetComponent<RCDComponent>(pManager.CurrentPermission?.MobUid, out var rcd))
            return;

        var worldPos = _transformSystem.ToMapCoordinates(_unalignedMouseCoords);
        _pipeLayersSystem.TryGetPipeLayerAtWorldPosition(
            gridUid,
            mapGrid,
            worldPos,
            MouseCoords,
            _eyeManager.CurrentEye.Rotation,
            out var layer);

        if (layer == _currentPipeLayer
            && rcd.ConstructionEntity != null
            && _protoManager.TryIndex<EntityPrototype>(rcd.CachedPrototype.Prototype!, out var baseProto)
            && baseProto.TryGetComponent<AtmosPipeLayersComponent>(out var layers, _entityManager.ComponentFactory)
            && _pipeLayersSystem.TryGetAlternativePrototype(layers, layer, out var currentProto)
            && rcd.ConstructionEntity == currentProto)
        {
            return;
        }

        _currentPipeLayer = layer;
        UpdateRpdGhostPrototype(rcd, layer);
        _ghostSystem.SyncPipeLayer(pManager.CurrentPermission!.MobUid, rcd, pManager.CurrentPermission!.EntityType, layer);
    }

    private void UpdateRpdGhostPrototype(RCDComponent rcd, AtmosPipeLayer layer)
    {
        if (pManager.CurrentPermission?.EntityType == null
            || !_protoManager.TryIndex<EntityPrototype>(rcd.CachedPrototype.Prototype!, out var baseProto)
            || !baseProto.TryGetComponent<AtmosPipeLayersComponent>(out var layers, _entityManager.ComponentFactory)
            || !_pipeLayersSystem.TryGetAlternativePrototype(layers, layer, out var newProtoId))
        {
            return;
        }

        if (pManager.CurrentPermission.EntityType == newProtoId)
            return;

        pManager.CurrentPermission.EntityType = newProtoId;

        if (_protoManager.TryIndex<EntityPrototype>(newProtoId, out var newProto)
            && newProto.TryGetComponent<SpriteComponent>(out var sprite, _entityManager.ComponentFactory))
        {
            var textures = new List<IDirectionalTextureProvider>();

            foreach (var spriteLayer in sprite.AllLayers)
            {
                if (spriteLayer.ActualRsi?.Path != null && spriteLayer.RsiState.Name != null)
                    textures.Add(_spriteSystem.RsiStateLike(new SpriteSpecifier.Rsi(spriteLayer.ActualRsi.Path, spriteLayer.RsiState.Name)));
            }

            pManager.CurrentTextures = textures;
        }
    }

    private bool ShouldHidePlacementOverlay()
    {
        if (!_entityManager.TryGetComponent<RCDComponent>(pManager.CurrentPermission?.MobUid, out var rcd)
            || !rcd.IsRpd
            || rcd.CachedPrototype.Mode != RcdMode.Deconstruct)
        {
            return false;
        }

        return true;
    }

    private bool ShouldDrawPipeLayerGuides(out EntityUid gridUid, out MapGridComponent mapGrid)
    {
        gridUid = default;
        mapGrid = default!;

        var gridOpt = _transformSystem.GetGrid(MouseCoords);

        if (gridOpt == null || Grid == null)
            return false;

        gridUid = gridOpt.Value;

        if (!_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var mg))
            return false;

        mapGrid = mg;

        if (!_entityManager.TryGetComponent<RCDComponent>(pManager.CurrentPermission?.MobUid, out var rcd)
            || !rcd.IsRpd
            || rcd.CachedPrototype.Mode != RcdMode.ConstructObject
            || string.IsNullOrEmpty(rcd.CachedPrototype.Prototype))
        {
            return false;
        }

        return _rcdSystem.UsesRpdPipeAlternativePrototypes(rcd.CachedPrototype.Prototype);
    }
}
