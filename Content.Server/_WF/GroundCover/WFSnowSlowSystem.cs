using Content.Shared._WF.GroundCover;
using Robust.Shared.Player;

namespace Content.Server._WF.GroundCover;

/// <summary>Snow slowdown and footsteps from the depths clients were sent, as each player's client has them.</summary>
public sealed class WFSnowSlowSystem : SharedWFSnowSlowSystem
{
    [Dependency] private WFGroundCoverSystem _cover = default!;

    private EntityQuery<ActorComponent> _actorQuery;

    public override void Initialize()
    {
        base.Initialize();
        _actorQuery = GetEntityQuery<ActorComponent>();
    }

    protected override int SnowVersion => _cover.SentStamp;

    protected override bool HasSnow(EntityUid grid)
    {
        return HasComp<WFSnowCoverComponent>(grid);
    }

    protected override bool Tracks(EntityUid mob)
    {
        return true;
    }

    protected override int TileDepth(EntityUid mob, EntityUid grid, Vector2i tile)
    {
        return _cover.GetSentDepth(grid, tile, _actorQuery.TryComp(mob, out var actor) ? actor.PlayerSession : null);
    }
}
