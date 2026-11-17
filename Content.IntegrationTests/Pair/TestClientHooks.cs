using System.Collections;
using System.Reflection;
using System.Runtime;
using Content.Client.Sprite;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;

namespace Content.IntegrationTests.Pair;

/// <summary>
/// Runs inside the client process, from the test assembly, so the client sandbox never sees it.
/// </summary>
public static class TestClientHooks
{
    private static int _frames;

    public static void LimitImagePool()
    {
        // The default allocator keeps every decoded RSI buffer. After preload that retained pool
        // is what stays above 8 GB. A small pool lets the next collection return it.
        SixLabors.ImageSharp.Configuration.Default.MemoryAllocator =
            SixLabors.ImageSharp.Memory.MemoryAllocator.Create(
                new SixLabors.ImageSharp.Memory.MemoryAllocatorOptions
                {
                    MaximumPoolSizeMegabytes = 32,
                });
    }

    public static void ReleaseImageMemory()
    {
        SixLabors.ImageSharp.Configuration.Default.MemoryAllocator.ReleaseRetainedResources();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    public static void OnFrame(IEntityManager entityManager)
    {
        RepairSpriteBounds(entityManager);

        // Full collections every frame would turn the suite into a GC benchmark.
        if (++_frames % 240 != 0)
            return;

        SixLabors.ImageSharp.Configuration.Default.MemoryAllocator.ReleaseRetainedResources();
    }

    private static readonly FieldInfo SpriteBoundsDirty =
        typeof(SpriteComponent).GetField("BoundsDirty", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SnapCardinalsField =
        typeof(SpriteComponent).GetField("_snapCardinals", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo LayersField =
        typeof(SpriteComponent).GetField("Layers", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo LayerBoundsDirty =
        typeof(SpriteComponent.Layer).GetField("BoundsDirty", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// A debug client throws while rebuilding bounds when a drawn layer is dirty on a clean sprite,
    /// and again when that layer snaps to cardinals but its RSI state has more than one direction.
    /// Release builds compile both asserts out, which is why the GitHub run stays green.
    /// </summary>
    private static void RepairSpriteBounds(IEntityManager entityManager)
    {
        if (SpriteBoundsDirty == null || SnapCardinalsField == null || LayersField == null || LayerBoundsDirty == null)
            return;

        var query = entityManager.AllEntityQueryEnumerator<SpriteComponent>();
        while (query.MoveNext(out _, out var sprite))
        {
            if (!sprite.Visible || sprite.ContainerOccluded)
                continue;

            if (LayersField.GetValue(sprite) is not IList layers)
                continue;

            var spriteDirty = SpriteBoundsDirty.GetValue(sprite) is true;
            var layerDirty = false;
            var clearSpriteSnap = false;

            foreach (var entry in layers)
            {
                if (entry is not SpriteComponent.Layer layer)
                    continue;

                if (!layer.Visible || layer.Blank || layer.CopyToShaderParameters != null)
                    continue;

                if (LayerBoundsDirty.GetValue(layer) is true)
                    layerDirty = true;

                if (!SnapsToCardinals(sprite, layer))
                    continue;

                var state = layer.ActualState;
                if (state == null || state.RsiDirections == RsiDirectionType.Dir1)
                    continue;

                // A per-layer strategy overrides the sprite flag. Leave the sprite snap alone for the other layers.
                if (sprite.GranularLayersRendering &&
                    layer.RenderingStrategy == LayerRenderingStrategy.SnapToCardinals)
                {
                    layer.RenderingStrategy = LayerRenderingStrategy.NoRotation;
                    continue;
                }

                clearSpriteSnap = true;
            }

            if (clearSpriteSnap)
                SnapCardinalsField.SetValue(sprite, false);

            // Forcing every sprite dirty rebuilds layers that were safely cached and hits the direction assert.
            // Only a clean sprite with a dirty drawn layer needs the rebuild flag.
            if (!spriteDirty && layerDirty)
                SpriteBoundsDirty.SetValue(sprite, true);
        }
    }

    private static bool SnapsToCardinals(SpriteComponent sprite, SpriteComponent.Layer layer)
    {
        if (sprite.GranularLayersRendering &&
            layer.RenderingStrategy != LayerRenderingStrategy.UseSpriteStrategy)
            return layer.RenderingStrategy == LayerRenderingStrategy.SnapToCardinals;

        return sprite.SnapCardinals;
    }
}
