using System.Numerics;
using Content.Shared._Forge.Item;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._Forge.Item;

/// <summary>
/// Client-only storage UI drawing for entities with <see cref="ForgeScaledStorageItemComponent"/>.
/// </summary>
public static class ForgeScaledStorageDraw
{
    public static bool TryDraw(
        IEntityManager entityManager,
        ItemComponent item,
        ForgeScaledStorageItemComponent forgeStorage,
        DrawingHandleScreen handle,
        Box2i boundingGrid,
        Vector2 size,
        Vector2 pixelPosition,
        Vector2? parentGlobalPixelPosition,
        Vector2 globalPixelPosition,
        float uiScale)
    {
        if (item.StoredSprite is not { } storageSprite)
            return false;

        var slotWide = boundingGrid.Width >= boundingGrid.Height;
        var iconRotation = (slotWide ? Angle.Zero : Angle.FromDegrees(90))
                           + Angle.FromDegrees(item.StoredRotation);
        var baseScale = 2 * uiScale;
        var drawScale = new Vector2(baseScale * forgeStorage.Scale.X, baseScale * forgeStorage.Scale.Y);
        var offset = (((Box2) boundingGrid).Size - Vector2.One) * size;
        var sprite = entityManager.System<SpriteSystem>().Frame0(storageSprite);
        var spriteSize = sprite.Size * drawScale;

        var gridPixelSize = new Vector2((boundingGrid.Width + 1) * size.X, (boundingGrid.Height + 1) * size.Y);
        var gridCenter = pixelPosition * 2
                         + (parentGlobalPixelPosition ?? Vector2.Zero)
                         + offset
                         + gridPixelSize / 2f;

        var half = spriteSize / 2f;
        var storedOffset = slotWide && forgeStorage.OffsetWide != Vector2i.Zero
            ? forgeStorage.OffsetWide
            : item.StoredOffset;
        var localOffset = new Vector2(storedOffset.X * 2f, storedOffset.Y * 2f);
        handle.SetTransform(gridCenter, iconRotation);
        handle.DrawTextureRect(sprite, new UIBox2(-half.X + localOffset.X, -half.Y + localOffset.Y, half.X + localOffset.X, half.Y + localOffset.Y));
        handle.SetTransform(globalPixelPosition, Angle.Zero);
        return true;
    }
}
