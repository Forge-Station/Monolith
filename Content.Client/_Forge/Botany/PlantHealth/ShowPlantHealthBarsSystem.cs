using System.Numerics;
using Content.Client.Overlays;
using Content.Shared._Forge.Botany.PlantHealth;
using Content.Shared.Botany;
using Content.Shared.Inventory.Events;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using static Robust.Shared.Maths.Color;

namespace Content.Client._Forge.Botany.PlantHealth;

public sealed class ShowPlantHealthBarsSystem : EquipmentHudSystem<ShowPlantHealthBarsComponent>
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;

    private PlantHealthBarOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new PlantHealthBarOverlay(EntityManager);
    }

    protected override void UpdateInternal(RefreshEquipmentHudEvent<ShowPlantHealthBarsComponent> args)
    {
        base.UpdateInternal(args);

        if (!_overlayMan.HasOverlay<PlantHealthBarOverlay>())
            _overlayMan.AddOverlay(_overlay);
    }

    protected override void DeactivateInternal()
    {
        base.DeactivateInternal();
        _overlayMan.RemoveOverlay(_overlay);
    }
}

public sealed class PlantHealthBarOverlay : Robust.Client.Graphics.Overlay
{
    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;
    private readonly SharedAppearanceSystem _appearance;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public PlantHealthBarOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
        _transform = entManager.System<SharedTransformSystem>();
        _appearance = entManager.System<SharedAppearanceSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var rotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();

        const float scale = 1f;
        var scaleMatrix = Matrix3Helpers.CreateScale(new Vector2(scale, scale));
        var rotationMatrix = Matrix3Helpers.CreateRotation(-rotation);

        var query = _entManager.AllEntityQueryEnumerator<PlantHealthBarTargetComponent, AppearanceComponent, TransformComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var appearance, out var xform, out var sprite))
        {
            if (xform.MapID != args.MapId)
                continue;

            if (!_appearance.TryGetData(uid, PlantHolderVisuals.HasPlant, out bool hasPlant, appearance) || !hasPlant)
                continue;

            if (!_appearance.TryGetData(uid, PlantHolderVisuals.HealthPercent, out float ratio, appearance))
                continue;

            ratio = Math.Clamp(ratio, 0f, 1f);

            var bounds = sprite.Bounds;
            var worldPos = _transform.GetWorldPosition(xform, xformQuery);
            if (!bounds.Translated(worldPos).Intersects(args.WorldAABB))
                continue;

            var worldMatrix = Matrix3Helpers.CreateTranslation(worldPos);
            var scaledWorld = Matrix3x2.Multiply(scaleMatrix, worldMatrix);
            var matty = Matrix3x2.Multiply(rotationMatrix, scaledWorld);
            handle.SetTransform(matty);

            var yOffset = bounds.Height * EyeManager.PixelsPerMeter / 2 - 3f;
            var widthOfMob = bounds.Width * EyeManager.PixelsPerMeter;
            var position = new Vector2(-widthOfMob / EyeManager.PixelsPerMeter / 2, yOffset / EyeManager.PixelsPerMeter);

            const float startX = 8f;
            var endX = widthOfMob - 8f;
            var xProgress = (endX - startX) * ratio + startX;

            var color = ratio switch
            {
                <= 0.25f => Red,
                <= 0.5f => Orange,
                <= 0.75f => Yellow,
                _ => LimeGreen
            };

            var boxBackground = new Box2(new Vector2(startX, 0f) / EyeManager.PixelsPerMeter, new Vector2(endX, 3f) / EyeManager.PixelsPerMeter);
            handle.DrawRect(boxBackground.Translated(position), Black.WithAlpha(192));

            var boxMain = new Box2(new Vector2(startX, 0f) / EyeManager.PixelsPerMeter, new Vector2(xProgress, 3f) / EyeManager.PixelsPerMeter);
            handle.DrawRect(boxMain.Translated(position), color);

            var pixelDarken = new Box2(new Vector2(startX, 2f) / EyeManager.PixelsPerMeter, new Vector2(xProgress, 3f) / EyeManager.PixelsPerMeter);
            handle.DrawRect(pixelDarken.Translated(position), Black.WithAlpha(128));
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
