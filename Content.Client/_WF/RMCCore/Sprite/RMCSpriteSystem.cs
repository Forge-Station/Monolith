using System.Numerics;
using Content.Shared._WF.RMCCore.Sprite;
using Robust.Client.GameObjects;
using Robust.Shared.Map;

namespace Content.Client._WF.RMCCore.Sprite;

public sealed partial class RMCSpriteSystem : SharedRMCSpriteSystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private TransformSystem _transform = default!;

    // WOLFGATE: RMCMobStateDrawDepth (RMC Mobs) and the local player's BelowMobs draw depth are cut; the latter
    // would apply to every Wolfgate player and fight _CE ZLevels, which sets DrawDepth every frame.

    public override Shared.DrawDepth.DrawDepth UpdateDrawDepth(EntityUid sprite)
    {
        var depth = base.UpdateDrawDepth(sprite);
        if (!TryComp(sprite, out SpriteComponent? comp))
            return depth;

        _sprite.SetDrawDepth((sprite, comp), (int) depth);
        return depth;
    }

    /// <summary>
    ///     This is so the animating entity's current location gets updated during the animation, there is probably a better way to do this.
    /// </summary>
    /// <param name="uid">The entity to update the location of.</param>
    public void UpdatePosition(EntityUid uid)
    {
        var oldPos = _transform.GetWorldPosition(uid);

        // Reset the sprite offset when the entity is in NullSpace
        if (Transform(uid).MapID == MapId.Nullspace)
        {
            if (TryComp(uid, out SpriteComponent? sprite))
                _sprite.SetOffset((uid, sprite), new Vector2());

            return;
        }

        var newPos = oldPos with { Y = oldPos.Y + 0.0001f };
        _transform.SetWorldPosition(uid, newPos);
    }

    public override void Update(float frameTime)
    {
        UpdateColors();
        UpdatePositions();
    }

    private void UpdateColors()
    {
        try
        {
            var colors = EntityQueryEnumerator<SpriteColorComponent, SpriteComponent>();
            while (colors.MoveNext(out var uid, out var color, out var sprite))
            {
                _sprite.SetColor((uid, sprite), color.Color);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error updating {nameof(SpriteColorComponent)} colors:\n{e}");
        }
    }

    private void UpdatePositions()
    {
        try
        {
            var location = EntityQueryEnumerator<RMCUpdateClientLocationComponent>();
            while (location.MoveNext(out var uid, out _))
            {
                UpdatePosition(uid);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error updating {nameof(RMCUpdateClientLocationComponent)} positions:\n{e}");
        }
    }
}
