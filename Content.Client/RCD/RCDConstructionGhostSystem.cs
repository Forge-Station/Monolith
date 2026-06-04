using Content.Client.Atmos;
using Content.Client.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.RCD;

/// <summary>
/// System for handling structure ghost placement in places where RCD can create objects.
/// </summary>
public sealed class RCDConstructionGhostSystem : EntitySystem
{
    private const string PlacementMode = nameof(AlignRCDConstruction);
    private const string AtmosPipePlacementMode = nameof(AlignAtmosPipeLayers);

    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPlacementManager _placementManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly RCDSystem _rcdSystem = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;

    private Direction _placementDirection;
    private bool _useMirrorPrototype;
    private ProtoId<RCDPrototype>? _trackedRecipeId;
    private ProtoId<RCDPrototype>? _pendingRecipeId;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.EditorFlipObject,
                new PointerInputCmdHandler(HandleFlip, outsidePrediction: true))
            .Register<RCDConstructionGhostSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<RCDConstructionGhostSystem>();
        base.Shutdown();
    }

    /// <summary>
    /// Forge-Change: called when the player picks a recipe in the RCD menu before server state catches up.
    /// </summary>
    public void NotifyRecipeSelected(ProtoId<RCDPrototype> protoId)
    {
        _pendingRecipeId = protoId;
        _trackedRecipeId = null;

        var player = _playerManager.LocalSession?.AttachedEntity;
        if (TryComp<HandsComponent>(player, out var hands)
            && hands.ActiveHand?.HeldEntity is { } heldEntity
            && TryComp<RCDComponent>(heldEntity, out var rcd)
            && rcd.IsRpd)
        {
            RaiseNetworkEvent(new RCDConstructionGhostLayerEvent(
                GetNetEntity(heldEntity),
                _eyeManager.CurrentEye.Rotation,
                null,
                AtmosPipeLayer.Primary));
        }
    }

    private bool HandleFlip(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State != BoundKeyState.Down)
            return false;

        if (!_placementManager.IsActive || _placementManager.Eraser)
            return false;

        var placerEntity = _placementManager.CurrentPermission?.MobUid;

        if (!TryComp<RCDComponent>(placerEntity, out var rcd))
            return false;

        _rcdSystem.UpdateCachedPrototype(placerEntity.Value, rcd);
        var prototype = rcd.CachedPrototype;
        if (string.IsNullOrEmpty(prototype.MirrorPrototype))
            return false;

        _useMirrorPrototype = !rcd.UseMirrorPrototype;

        var useProto = _useMirrorPrototype ? prototype.MirrorPrototype : prototype.Prototype;
        CreatePlacer(placerEntity.Value, rcd, useProto, prototype.Mode, recipeId: rcd.ProtoId);

        RaiseNetworkEvent(new RCDConstructionGhostFlipEvent(GetNetEntity(placerEntity.Value), _useMirrorPrototype));
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var placerEntity = _placementManager.CurrentPermission?.MobUid;
        var placerProto = _placementManager.CurrentPermission?.EntityType;
        var placerIsRcd = HasComp<RCDComponent>(placerEntity);

        if (_placementManager.Eraser || placerEntity != null && !placerIsRcd)
            return;

        var player = _playerManager.LocalSession?.AttachedEntity;

        if (!TryComp<HandsComponent>(player, out var hands))
            return;

        var heldEntity = hands.ActiveHand?.HeldEntity;

        if (!TryComp<RCDComponent>(heldEntity, out var rcd))
        {
            if (placerIsRcd)
                _placementManager.Clear();

            return;
        }

        _rcdSystem.UpdateCachedPrototype(heldEntity.Value, rcd);

        if (_pendingRecipeId != null && rcd.ProtoId == _pendingRecipeId)
            _pendingRecipeId = null;

        var activeRecipeId = _pendingRecipeId ?? rcd.ProtoId;
        var prototype = _protoManager.Index(activeRecipeId);

        // Forge-Change: RPD placement ghost only for construction (deconstruct uses click, no tile overlay)
        if (rcd.IsRpd)
        {
            if (prototype.Mode == RcdMode.Invalid)
            {
                if (placerIsRcd)
                    _placementManager.Clear();

                return;
            }

            if (prototype.Mode == RcdMode.Deconstruct)
            {
                if (placerIsRcd)
                    _placementManager.Clear();

                return;
            }

            if (prototype.Mode == RcdMode.ConstructObject && string.IsNullOrEmpty(prototype.Prototype))
            {
                if (placerIsRcd)
                    _placementManager.Clear();

                return;
            }
        }

        if (_placementDirection != _placementManager.Direction)
        {
            _placementDirection = _placementManager.Direction;
            RaiseNetworkEvent(new RCDConstructionGhostRotationEvent(GetNetEntity(heldEntity.Value), _placementDirection));
        }

        _useMirrorPrototype = _pendingRecipeId != null ? false : rcd.UseMirrorPrototype;
        var useProto = _useMirrorPrototype && !string.IsNullOrEmpty(prototype.MirrorPrototype)
            ? prototype.MirrorPrototype
            : prototype.Prototype;

        // Forge-Change: only reuse layer-specific proto when it belongs to the current recipe
        if (_pendingRecipeId == null
            && rcd.IsRpd
            && rcd.ConstructionEntity != null
            && _rcdSystem.IsValidRpdConstructionEntity(rcd, rcd.ConstructionEntity))
        {
            useProto = rcd.ConstructionEntity;
        }

        var placementMode = GetPlacementMode(rcd, useProto);
        var recipeChanged = _trackedRecipeId != activeRecipeId;

        if (recipeChanged)
        {
            _trackedRecipeId = activeRecipeId;

            // Forge-Change: drop pipe-layer proto when switching to a recipe without alt layers
            if (rcd.IsRpd
                && rcd.ConstructionEntity != null
                && !_rcdSystem.IsValidRpdConstructionEntity(rcd, rcd.ConstructionEntity))
            {
                RaiseNetworkEvent(new RCDConstructionGhostLayerEvent(
                    GetNetEntity(heldEntity.Value),
                    _eyeManager.CurrentEye.Rotation,
                    null,
                    AtmosPipeLayer.Primary));
            }
        }

        if (heldEntity != placerEntity
            || useProto != placerProto
            || _placementManager.CurrentPermission?.PlacementOption != placementMode
            || recipeChanged)
        {
            CreatePlacer(heldEntity.Value, rcd, useProto, prototype.Mode, placementMode);
        }
    }

    public void SyncPipeLayer(EntityUid uid, RCDComponent rcd, string? constructionEntity, AtmosPipeLayer layer)
    {
        var eyeRotation = _eyeManager.CurrentEye.Rotation;
        if (rcd.ConstructionEntity == constructionEntity
            && rcd.PipeLayerEyeRotation == eyeRotation
            && rcd.ConstructionPipeLayer == layer)
        {
            return;
        }

        RaiseNetworkEvent(new RCDConstructionGhostLayerEvent(GetNetEntity(uid), eyeRotation, constructionEntity, layer));
    }

    private string GetPlacementMode(RCDComponent rcd, string? prototypeId)
    {
        if (!rcd.IsRpd || string.IsNullOrEmpty(prototypeId))
            return PlacementMode;

        if (_rcdSystem.UsesRpdAtmosTilePlacement(prototypeId))
            return AtmosPipePlacementMode;

        return PlacementMode;
    }

    private void CreatePlacer(
        EntityUid uid,
        RCDComponent component,
        string? prototype,
        RcdMode mode,
        string? placementMode = null,
        ProtoId<RCDPrototype>? recipeId = null)
    {
        var newObjInfo = new PlacementInformation
        {
            MobUid = uid,
            PlacementOption = placementMode ?? GetPlacementMode(component, prototype),
            EntityType = prototype,
            Range = (int) Math.Ceiling(SharedInteractionSystem.InteractionRange),
            IsTile = mode == RcdMode.ConstructTile,
            UseEditorContext = false,
        };

        var modeName = placementMode ?? GetPlacementMode(component, prototype);

        _placementManager.Clear();
        _placementManager.BeginPlacing(newObjInfo);

        // Forge-Change: user-rotated logistics need the pipe connector visible on the ghost (R key)
        if (component.IsRpd
            && _protoManager.TryIndex(recipeId ?? component.ProtoId, out var recipe)
            && recipe.Rotation != RcdRotation.User)
        {
            HideRpdPipeConnectorGhostLayer(modeName);
        }
    }

    private void HideRpdPipeConnectorGhostLayer(string placementMode)
    {
        if (placementMode == AtmosPipePlacementMode)
            return;

        if (_placementManager is not PlacementManager manager
            || manager.CurrentPlacementOverlayEntity is not { } overlay
            || !TryComp<SpriteComponent>(overlay, out var sprite)
            || !sprite.LayerMapTryGet(PipeVisualLayers.Pipe, out var pipeLayer))
        {
            return;
        }

        sprite[pipeLayer].Visible = false;
    }
}
