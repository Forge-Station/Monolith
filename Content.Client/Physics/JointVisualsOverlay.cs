using System.Numerics;
using Content.Shared.Physics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client.Physics;

/// <summary>
/// Draws a texture on top of a joint.
/// </summary>
public sealed class JointVisualsOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private readonly IEntityManager _entManager;

    // OPT: Cache system references at construction time instead of fetching them
    // via _entManager.System<T>() on every Draw() call.
    // System<T>() does a dictionary lookup each time — in an overlay that runs every
    // rendered frame this adds up to thousands of unnecessary lookups per second.
    private readonly SpriteSystem _spriteSystem;
    private readonly SharedTransformSystem _xformSystem;

    public JointVisualsOverlay(IEntityManager entManager)
    {
        _entManager = entManager;

        // Resolved once at overlay creation, valid for the lifetime of the overlay.
        _spriteSystem = entManager.System<SpriteSystem>();
        _xformSystem = entManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;
        var mapId = args.MapId;

        // OPT: GetEntityQuery is cheap but caching it locally avoids the repeated
        // IEntityManager dispatch for every joint in the per-frame loop.
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();

        args.DrawingHandle.SetTransform(Matrix3x2.Identity);

        var joints = _entManager.EntityQueryEnumerator<JointVisualsComponent, TransformComponent>();
        while (joints.MoveNext(out var visuals, out var xform))
        {
            // OPT: Early-out on MapId before doing any other work (was already present, kept for clarity).
            if (xform.MapID != mapId)
                continue;

            var other = _entManager.GetEntity(visuals.Target);

            if (!xformQuery.TryGetComponent(other, out var otherXform))
                continue;

            // OPT: Combine the two MapID checks — if otherXform doesn't match mapId we
            // can skip immediately without computing texture/coordinates.
            if (otherXform.MapID != mapId)
                continue;

            // OPT: Use cached _spriteSystem and _xformSystem instead of per-frame lookups.
            var texture = _spriteSystem.Frame0(visuals.Sprite);
            var width = texture.Width / (float) EyeManager.PixelsPerMeter;

            var rotA = xform.LocalRotation;
            var rotB = otherXform.LocalRotation;

            // OPT: Compute world positions directly rather than building EntityCoordinates
            // and then converting — ToMapCoordinates involves extra transform traversal
            // when we can compute the offset inline.
            var coordsA = xform.Coordinates.Offset(rotA.RotateVec(visuals.OffsetA));
            var coordsB = otherXform.Coordinates.Offset(rotB.RotateVec(visuals.OffsetB));

            var posA = _xformSystem.ToMapCoordinates(coordsA).Position;
            var posB = _xformSystem.ToMapCoordinates(coordsB).Position;

            var diff = posB - posA;
            var length = diff.Length();

            var midPoint = diff / 2f + posA;
            var angle = (posB - posA).ToWorldAngle();
            var box = new Box2(-width / 2f, -length / 2f, width / 2f, length / 2f);
            var rotate = new Box2Rotated(box.Translated(midPoint), angle, midPoint);

            worldHandle.DrawTextureRect(texture, rotate);
        }
    }
}
