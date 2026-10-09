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

            if (!_appearance.TryGetData(uid, PlantHolderVisuals.HealthPercent, out float healthRatio, appearance))
                continue;

            healthRatio = Math.Clamp(healthRatio, 0f, 1f);
            _appearance.TryGetData(uid, PlantHolderVisuals.WaterPercent, out float waterRatio, appearance);
            waterRatio = Math.Clamp(waterRatio, 0f, 1f);
            _appearance.TryGetData(uid, PlantHolderVisuals.NutritionPercent, out float nutritionRatio, appearance);
            nutritionRatio = Math.Clamp(nutritionRatio, 0f, 1f);

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

            var color = healthRatio switch
            {
                <= 0.25f => Red,
                <= 0.5f => Orange,
                <= 0.75f => Yellow,
                _ => LimeGreen
            };

            DrawBar(handle, position, startX, endX, 0f, healthRatio, color);

            var waterColor = waterRatio switch
            {
                <= 0.15f => Red,
                <= 0.35f => Orange,
                _ => Color.FromHex("#4aa8ff")
            };
            DrawBar(handle, position, startX, endX, -4f, waterRatio, waterColor);

            var nutritionColor = nutritionRatio switch
            {
                <= 0.08f => Red,
                <= 0.2f => Orange,
                _ => Color.FromHex("#7dce4a")
            };
            DrawBar(handle, position, startX, endX, -8f, nutritionRatio, nutritionColor);

            const float iconSize = 3.5f;
            const float iconGap = 1.5f;
            var iconY0 = -11f;
            var iconY1 = iconY0 + iconSize;
            var iconX = startX;

            DrawStatusIcon(handle, position, ref iconX, iconY0, iconY1, iconSize, iconGap,
                TryFlag(uid, appearance, PlantHolderVisuals.HarvestLight), Color.FromHex("#e8d050"));
            DrawStatusIcon(handle, position, ref iconX, iconY0, iconY1, iconSize, iconGap,
                TryFlag(uid, appearance, PlantHolderVisuals.Radioactive), Color.FromHex("#f0c040"));
            DrawStatusIcon(handle, position, ref iconX, iconY0, iconY1, iconSize, iconGap,
                TryFlag(uid, appearance, PlantHolderVisuals.WeedsHigh), Color.FromHex("#7dce4a"));
            DrawStatusIcon(handle, position, ref iconX, iconY0, iconY1, iconSize, iconGap,
                TryFlag(uid, appearance, PlantHolderVisuals.AlertLight), Color.FromHex("#ff6b6b"));
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private static void DrawBar(
        DrawingHandleWorld handle,
        Vector2 position,
        float startX,
        float endX,
        float yOffset,
        float ratio,
        Color color)
    {
        var xProgress = (endX - startX) * ratio + startX;

        var boxBackground = new Box2(
            new Vector2(startX, yOffset) / EyeManager.PixelsPerMeter,
            new Vector2(endX, yOffset + 3f) / EyeManager.PixelsPerMeter);
        handle.DrawRect(boxBackground.Translated(position), Black.WithAlpha(192));

        var boxMain = new Box2(
            new Vector2(startX, yOffset) / EyeManager.PixelsPerMeter,
            new Vector2(xProgress, yOffset + 3f) / EyeManager.PixelsPerMeter);
        handle.DrawRect(boxMain.Translated(position), color);

        var pixelDarken = new Box2(
            new Vector2(startX, yOffset + 2f) / EyeManager.PixelsPerMeter,
            new Vector2(xProgress, yOffset + 3f) / EyeManager.PixelsPerMeter);
        handle.DrawRect(pixelDarken.Translated(position), Black.WithAlpha(128));
    }

    private bool TryFlag(EntityUid uid, AppearanceComponent appearance, PlantHolderVisuals key)
    {
        return _appearance.TryGetData(uid, key, out bool value, appearance) && value;
    }

    private static void DrawStatusIcon(
        DrawingHandleWorld handle,
        Vector2 position,
        ref float iconX,
        float iconY0,
        float iconY1,
        float iconSize,
        float iconGap,
        bool enabled,
        Color color)
    {
        if (!enabled)
            return;

        var box = new Box2(
            new Vector2(iconX, iconY0) / EyeManager.PixelsPerMeter,
            new Vector2(iconX + iconSize, iconY1) / EyeManager.PixelsPerMeter);
        handle.DrawRect(box.Translated(position), color);
        iconX += iconSize + iconGap;
    }
}
