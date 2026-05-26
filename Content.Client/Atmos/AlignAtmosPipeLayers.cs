using Content.Client.Construction;
using Content.Client.Gameplay;
using Content.Client.RCD;
using Content.Shared.Atmos.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.RCD.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Numerics;
using static Robust.Client.Placement.PlacementManager;

namespace Content.Client.Atmos;

/// <summary>
/// Allows users to place atmos pipes on different layers depending on how the mouse cursor is positioned within a grid tile.
/// </summary>
/// <remarks>
/// This placement mode is not on the engine because it is content specific.
/// </remarks>
public sealed partial class AlignAtmosPipeLayers : SnapgridCenter
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IStateManager _stateManager = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly SharedAtmosPipeLayersSystem _pipeLayersSystem;
    private readonly SpriteSystem _spriteSystem;
    private readonly RCDConstructionGhostSystem _rpdGhostSystem;
    private readonly RCDSystem _rcdSystem;

    private const float SearchBoxSize = 2f;
    private EntityCoordinates _unalignedMouseCoords = default;

    private readonly Color _guideColor = new(0, 0, 0.5785f);
    private const float GuideRadius = 0.1f;

    public override bool RangeRequired => true;

    public AlignAtmosPipeLayers(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);

        _mapSystem = _entityManager.System<SharedMapSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _pipeLayersSystem = _entityManager.System<SharedAtmosPipeLayersSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _rpdGhostSystem = _entityManager.System<RCDConstructionGhostSystem>();
        _rcdSystem = _entityManager.System<RCDSystem>();
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        if (!RangeCheck(position))
            return false;

        var player = _playerManager.LocalSession?.AttachedEntity;

        if (player == null
            || !_entityManager.TryGetComponent<HandsComponent>(player, out var hands)
            || hands.ActiveHand?.HeldEntity is not { } heldEntity
            || !_entityManager.TryGetComponent<RCDComponent>(heldEntity, out var rcd)
            || !rcd.IsRpd)
        {
            return true;
        }

        if (!_rcdSystem.TryGetMapGridData(position, out var mapGridData))
            return false;

        var currentState = _stateManager.CurrentState;

        if (currentState is not GameplayStateBase screen)
            return false;

        var worldClick = _transformSystem.ToMapCoordinates(_unalignedMouseCoords);
        var constructionEntity = pManager.CurrentPermission?.EntityType;
        EntityUid? target = null;

        if (rcd.CachedPrototype.Mode == RcdMode.Deconstruct)
        {
            var clicked = screen.GetClickedEntity(worldClick);
            _rcdSystem.TryResolveRpdDeconstructTarget(rcd, mapGridData.Value, worldClick, clicked, out target);
        }
        else
            target = screen.GetClickedEntity(worldClick);

        return _rcdSystem.IsRCDOperationStillValid(
            heldEntity,
            rcd,
            mapGridData.Value,
            target,
            player.Value,
            popMsgs: false,
            rpdConstructionEntity: constructionEntity);
    }

    /// <inheritdoc/>
    public override void Render(in OverlayDrawArgs args)
    {
        var gridUid = _entityManager.System<SharedTransformSystem>().GetGrid(MouseCoords);

        if (gridUid == null || Grid == null)
            return;

        // Forge-Change: shared pipe-layer guide positions
        if (pManager.PlacementType == PlacementTypes.None
            && _entityManager.TryGetComponent<MapGridComponent>(gridUid, out var mapGrid))
        {
            _pipeLayersSystem.GetPipeLayerGuideWorldPositions(
                gridUid.Value,
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

    /// <inheritdoc/>
    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        _unalignedMouseCoords = ScreenToCursorGrid(mouseScreen);

        if (pManager.PlacementType != PlacementTypes.None)
        {
            base.AlignPlacementMode(mouseScreen);
            return;
        }

        MouseCoords = _unalignedMouseCoords.AlignWithClosestGridTile(SearchBoxSize, _entityManager, _mapManager);

        var gridId = _transformSystem.GetGrid(MouseCoords);

        if (!_entityManager.TryGetComponent<MapGridComponent>(gridId, out var mapGrid))
            return;

        CurrentTile = _mapSystem.GetTileRef(gridId.Value, mapGrid, MouseCoords);
        GridDistancing = mapGrid.TileSize;
        SnapSize = mapGrid.TileSize;
        Grid = mapGrid;

        if (pManager.PlacementOffset != Vector2i.Zero)
        {
            MouseCoords = new EntityCoordinates(
                MouseCoords.EntityId,
                MouseCoords.Position + new Vector2(pManager.PlacementOffset.X, pManager.PlacementOffset.Y));
        }

        var tileCenter = MouseCoords;
        var worldPos = _transformSystem.ToMapCoordinates(_unalignedMouseCoords);
        _pipeLayersSystem.TryGetPipeLayerAtWorldPosition(
            gridId.Value,
            mapGrid,
            worldPos,
            tileCenter,
            _eyeManager.CurrentEye.Rotation,
            out var layer);

        // Forge-Change: sync RPD layer selection when placing with an RPD
        if (pManager.CurrentPermission?.MobUid is { } mobUid
            && _entityManager.TryGetComponent<RCDComponent>(mobUid, out var rcd)
            && rcd.IsRpd)
        {
            UpdatePlacer(layer);
        }
        else if (pManager.Hijack != null)
            UpdateHijackedPlacer(layer, mouseScreen);
        else
            UpdatePlacer(layer);
    }

    private void UpdateHijackedPlacer(AtmosPipeLayer layer, ScreenCoordinates mouseScreen)
    {
        // Try to get alternative prototypes from the construction prototype
        var constructionSystem = (pManager.Hijack as ConstructionPlacementHijack)?.CurrentConstructionSystem;
        var altPrototypes = (pManager.Hijack as ConstructionPlacementHijack)?.CurrentPrototype?.AlternativePrototypes;

        if (constructionSystem == null || altPrototypes == null || (int)layer >= altPrototypes.Length)
            return;

        var newProtoId = altPrototypes[(int)layer];

        if (!_protoManager.TryIndex(newProtoId, out var newProto))
            return;

        if (newProto.Type != ConstructionType.Structure)
        {
            pManager.Clear();
            return;
        }

        if (newProto.ID == (pManager.Hijack as ConstructionPlacementHijack)?.CurrentPrototype?.ID)
            return;

        // Start placing
        pManager.BeginPlacing(new PlacementInformation()
        {
            IsTile = false,
            PlacementOption = newProto.PlacementMode,
        }, new ConstructionPlacementHijack(constructionSystem, newProto));

        if (pManager.CurrentMode is AlignAtmosPipeLayers { } newMode)
            newMode.RefreshGrid(mouseScreen);

        // Update construction guide
        constructionSystem.GetGuide(newProto);
    }

    private void UpdatePlacer(AtmosPipeLayer layer)
    {
        if (pManager.CurrentPermission?.EntityType == null)
            return;

        if (!_protoManager.TryIndex<EntityPrototype>(pManager.CurrentPermission.EntityType, out var currentProto)
            || !currentProto.TryGetComponent<AtmosPipeLayersComponent>(out var atmosPipeLayers, _entityManager.ComponentFactory))
        {
            return;
        }

        if (_pipeLayersSystem.TryGetAlternativePrototype(atmosPipeLayers, layer, out var newProtoId)
            && _protoManager.TryIndex<EntityPrototype>(newProtoId, out var newProto))
        {
            pManager.CurrentPermission.EntityType = newProtoId;

            if (newProto.TryGetComponent<SpriteComponent>(out var sprite, _entityManager.ComponentFactory))
            {
                var textures = new List<IDirectionalTextureProvider>();

                foreach (var spriteLayer in sprite.AllLayers)
                {
                    if (spriteLayer.ActualRsi?.Path != null && spriteLayer.RsiState.Name != null)
                        textures.Add(_spriteSystem.RsiStateLike(new SpriteSpecifier.Rsi(spriteLayer.ActualRsi.Path, spriteLayer.RsiState.Name)));
                }

                pManager.CurrentTextures = textures;
            }

            return;
        }

        // Forge-Change: mixers / logistics — same prototype, different pipe layer (alt1/alt2 connector RSI)
        if (pManager is PlacementManager manager
            && manager.CurrentPlacementOverlayEntity is { } overlay
            && _entityManager.TryGetComponent<AtmosPipeLayersComponent>(overlay, out var overlayLayers))
        {
            _pipeLayersSystem.SetPipeLayer((overlay, overlayLayers), layer);
        }

        if (pManager.CurrentPermission?.MobUid is { } mobUid
            && _entityManager.TryGetComponent<RCDComponent>(mobUid, out var rcd)
            && rcd.IsRpd)
        {
            _rpdGhostSystem.SyncPipeLayer(mobUid, rcd, pManager.CurrentPermission.EntityType, layer);
        }
    }

    private void RefreshGrid(ScreenCoordinates mouseScreen)
    {
        base.AlignPlacementMode(mouseScreen);
    }
}
