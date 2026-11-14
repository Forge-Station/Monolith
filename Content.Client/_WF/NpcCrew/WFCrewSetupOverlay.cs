using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._WF.NpcCrew;

/// <summary>Shows approved posts only on the requesting admin's client for thirty seconds.</summary>
public sealed class WFCrewSetupOverlay : Overlay
{
    private readonly WFCrewSetupClientSystem _setup;
    private readonly IEntityManager _entities;
    private readonly IGameTiming _timing;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFCrewSetupOverlay(WFCrewSetupClientSystem setup, IEntityManager entities, IGameTiming timing)
    {
        _setup = setup;
        _entities = entities;
        _timing = timing;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_timing.CurTime >= _setup.PreviewUntil || _setup.Preview is not { Grid: { } net } preview
            || !_entities.TryGetEntity(net, out var grid) || !_entities.TryGetComponent<TransformComponent>(grid, out var transform)
            || transform.MapID != args.MapId)
            return;
        var handle = args.WorldHandle;
        handle.SetTransform(_entities.System<SharedTransformSystem>().GetWorldMatrix(transform));
        foreach (var post in preview.Posts)
        {
            handle.DrawCircle(post.Position, 0.4f, Color.Cyan.WithAlpha(0.55f));
            handle.DrawLine(post.Position - new Vector2(0.6f, 0), post.Position + new Vector2(0.6f, 0), Color.White);
            handle.DrawLine(post.Position - new Vector2(0, 0.6f), post.Position + new Vector2(0, 0.6f), Color.White);
        }
        handle.SetTransform(Matrix3x2.Identity);
    }
}
