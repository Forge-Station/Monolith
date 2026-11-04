using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client._WF.GroundCover;

/// <summary>Draws footprints over the snow cover, under entities.</summary>
public sealed class WFSnowPrintsOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;

    private static readonly ResPath Rsi = new("/Textures/_WF/BloodTrail/footprints.rsi");

    /// <summary>A trodden-in blue grey, so prints read as hollows in the snow.</summary>
    private static readonly Color Tint = new(0.48f, 0.55f, 0.68f, 0.55f);

    private readonly WFSnowPrintsSystem _prints;
    private readonly WFGroundCoverSystem _cover;
    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly Texture[] _textures;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public WFSnowPrintsOverlay(WFSnowPrintsSystem prints)
    {
        IoCManager.InjectDependencies(this);
        _prints = prints;
        _cover = _entMan.System<WFGroundCoverSystem>();
        _sprite = _entMan.System<SpriteSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        // Over the snow cover the ground cover overlay draws at 10 and the stains at 11.
        ZIndex = 12;
        _textures = new[]
        {
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-left-bare-human")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-right-bare-human")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-shoes")),
        };
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _prints.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var (grid, tiles) in _prints.Prints)
        {
            if (!_entMan.TryGetComponent(grid, out TransformComponent? xform) || xform.MapID != args.MapId)
                continue;

            var (_, _, matrix, inverse) = _transform.GetWorldPositionRotationMatrixWithInv(grid);
            var bounds = inverse.TransformBox(args.WorldAABB).Enlarged(1f);
            handle.SetTransform(matrix);
            foreach (var (tile, list) in tiles)
            {
                if (!bounds.Contains(tile + new Vector2(0.5f, 0.5f))
                    || _cover.IsUnderVessel(args.MapId, Vector2.Transform(tile + new Vector2(0.5f, 0.5f), matrix)))
                    continue;

                foreach (var print in list)
                {
                    var alpha = _prints.Visibility(print);
                    if (alpha <= 0f)
                        continue;

                    var box = Box2.CenteredAround(print.Position, Vector2.One);
                    handle.DrawTextureRect(_textures[(int) print.Kind], new Box2Rotated(box, print.Rotation, print.Position),
                        Tint.WithAlpha(Tint.A * alpha));
                }
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
