using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WF.GroundCover;

/// <summary>Draws tread marks over the snow cover, under footprints and entities.</summary>
public sealed class WFSnowTracksOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;

    /// <summary>Pressed-flat snow, a shade darker and bluer than the cover.</summary>
    private static readonly Color Tint = new(0.42f, 0.48f, 0.6f, 0.6f);

    /// <summary>Tiles across and along one mark.</summary>
    private static readonly Vector2 Size = new(0.45f, 0.8f);

    private readonly WFSnowTracksSystem _tracks;
    private readonly WFGroundCoverSystem _cover;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public WFSnowTracksOverlay(WFSnowTracksSystem tracks)
    {
        IoCManager.InjectDependencies(this);
        _tracks = tracks;
        _cover = _entMan.System<WFGroundCoverSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        // Over the snow cover (10) and stains (11), under footprints (12).
        ZIndex = 11;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _tracks.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var (grid, tiles) in _tracks.Marks)
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

                foreach (var mark in list)
                {
                    var alpha = _tracks.Visibility(mark);
                    if (alpha <= 0f)
                        continue;

                    var box = Box2.CenteredAround(mark.Position, Size);
                    handle.DrawRect(new Box2Rotated(box, mark.Rotation, mark.Position), Tint.WithAlpha(Tint.A * alpha));
                }
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
