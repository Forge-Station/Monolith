using System.Numerics;
using Content.Shared._Forge.Atmos.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using DrawOverlay = Robust.Client.Graphics.Overlay;

namespace Content.Client._Forge.Atmos;

/// <summary>
/// Fills the cloud's gameplay radius with animated smoke, instead of a solid "zone" disc.
/// </summary>
public sealed class GasCloudOverlay : DrawOverlay
{
    private static readonly SpriteSpecifier.Rsi SmokeSprite =
        new(new ResPath("/Textures/Effects/chemsmoke.rsi"), "chemsmoke");

    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;
    private readonly IGameTiming _timing;
    private readonly ShaderInstance _unshaded;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public GasCloudOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
        _transform = entManager.System<SharedTransformSystem>();
        _sprite = entManager.System<SpriteSystem>();
        _timing = IoCManager.Resolve<IGameTiming>();
        _unshaded = IoCManager.Resolve<IPrototypeManager>().Index(UnshadedShader).Instance();
        ZIndex = 5;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.UseShader(_unshaded);
        var time = (float)_timing.RealTime.TotalSeconds;

        var query = _entManager.EntityQueryEnumerator<GasCloudComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var cloud, out var xform))
        {
            var radius = cloud.CurrentRadius;
            var gasColor = cloud.Color;
            if (xform.MapID != args.MapId || radius < 0.5f)
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            if (!args.WorldAABB.Intersects(Box2.CenteredAround(worldPos, new Vector2(radius * 2.6f, radius * 2.6f))))
                continue;

            var seed = uid.GetHashCode();
            var pulse = 0.82f + 0.08f * MathF.Sin(time * 0.55f + seed);

            // Dense core — same smoke as the deposit sprite, stretched over the whole radius.
            DrawPuff(handle, worldPos, radius * 2.2f, time, seed, gasColor.WithAlpha(0.5f * pulse));

            var puffs = 7 + (int)(radius * 0.35f);
            for (var i = 0; i < puffs; i++)
            {
                var angle = seed * 0.21f + i * (MathF.Tau / puffs) + time * 0.06f;
                var dist = radius * (0.28f + 0.5f * ((i * 0.37f + seed * 0.01f) % 1f));
                var pos = worldPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                var size = radius * (1.05f + (i % 3) * 0.18f);
                var alpha = (0.28f + (i % 2) * 0.1f) * pulse;
                DrawPuff(handle, pos, size, time + i * 0.13f, seed + i * 17, gasColor.WithAlpha(alpha));
            }
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawPuff(DrawingHandleWorld handle, Vector2 pos, float size, float time, int salt, Color color)
    {
        var frame = _sprite.GetFrame(SmokeSprite, TimeSpan.FromSeconds(time * 0.4 + salt * 0.05));
        handle.DrawTextureRect(frame, Box2.CenteredAround(pos, new Vector2(size, size)), color);
    }
}
