using Content.Shared._WF.GroundCover;
using Robust.Client.GameStates;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.GroundCover;

/// <summary>Predicts snow slowdown and footsteps for the mobs this client moves, from the snow cover it was sent.</summary>
public sealed class WFSnowSlowSystem : SharedWFSnowSlowSystem
{
    [Dependency] private WFGroundCoverSystem _cover = default!;
    [Dependency] private IClientGameStateManager _state = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _state.GameStateApplied += OnStateApplied;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _state.GameStateApplied -= OnStateApplied;
    }

    private void OnStateApplied(GameStateAppliedArgs args)
    {
        ForgetTracked();
    }

    protected override int SnowVersion => _cover.Version;

    protected override bool HasSnow(EntityUid grid)
    {
        return _cover.HasSnow(grid);
    }

    protected override bool Tracks(EntityUid mob)
    {
        return _physicsQuery.TryComp(mob, out var body) && body.Predict;
    }

    protected override int TileDepth(EntityUid mob, EntityUid grid, Vector2i tile)
    {
        return _cover.GetDepth(grid, tile);
    }
}
