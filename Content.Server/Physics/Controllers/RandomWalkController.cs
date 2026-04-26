using System.Numerics;
using Content.Server.Physics.Components;
using Content.Shared.Follower.Components;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Physics.Controllers;

/// <summary>
/// The entity system responsible for managing <see cref="RandomWalkComponent"/>s.
/// Handles updating the direction they move in when their cooldown elapses.
/// </summary>
internal sealed class RandomWalkController : VirtualController
{
    #region Dependencies
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly PhysicsSystem _physics = default!;
    #endregion Dependencies

    // OPT: Pre-cache component queries to avoid repeated GetEntityQuery<> calls in the hot loop.
    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<ThrownItemComponent> _thrownQuery;
    private EntityQuery<FollowerComponent> _followerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _actorQuery = GetEntityQuery<ActorComponent>();
        _thrownQuery = GetEntityQuery<ThrownItemComponent>();
        _followerQuery = GetEntityQuery<FollowerComponent>();

        SubscribeLocalEvent<RandomWalkComponent, ComponentStartup>(OnRandomWalkStartup);
    }

    /// <summary>
    /// Updates the cooldowns of all random walkers.
    /// If each of them is off cooldown it updates their velocity and resets its cooldown.
    /// </summary>
    /// <param name="prediction">??? Not documented anywhere I can see ???</param> // TODO: Document this.
    /// <param name="frameTime">The amount of time that has elapsed since the last time random walk cooldowns were updated.</param>
    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // OPT: Read CurTime once per frame. IGameTiming.CurTime involves a getter call;
        // doing it inside the loop would cost one property dispatch per entity.
        var curTime = _timing.CurTime;

        var query = EntityQueryEnumerator<RandomWalkComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var randomWalk, out var physics))
        {
            // OPT: Use cached EntityQuery<T>.HasComponent instead of EntityManager.HasComponent<T>.
            // EntityManager.HasComponent goes through an extra virtual dispatch layer.
            if (_actorQuery.HasComponent(uid)
            ||  _thrownQuery.HasComponent(uid)
            ||  _followerQuery.HasComponent(uid))
                continue;

            if (randomWalk.NextStepTime <= curTime)
                Update(uid, randomWalk, physics);
        }
    }

    /// <summary>
    /// Updates the direction and speed a random walker is moving at.
    /// Also resets the random walker's cooldown.
    /// </summary>
    /// <param name="randomWalk">The random walker state.</param>
    /// <param name="physics">The physics body associated with the random walker.</param>
    public void Update(EntityUid uid, RandomWalkComponent? randomWalk = null, PhysicsComponent? physics = null)
    {
        if (!Resolve(uid, ref randomWalk))
            return;

        // OPT: Read CurTime once and reuse.
        var curTime = _timing.CurTime;
        randomWalk.NextStepTime = curTime + TimeSpan.FromSeconds(
            _random.NextDouble(randomWalk.MinStepCooldown.TotalSeconds, randomWalk.MaxStepCooldown.TotalSeconds));

        if (!Resolve(uid, ref physics))
            return;

        var pushVec = _random.NextAngle().ToVec();
        pushVec += randomWalk.BiasVector;

        // OPT: Normalize only if the vector has meaningful length to avoid a NaN from normalizing zero.
        // Also avoid calling Vector2.Normalize (returns a new value) — mutate in place.
        var vecLen = pushVec.Length();
        if (vecLen > 1e-6f)
            pushVec /= vecLen;

        if (randomWalk.ResetBiasOnWalk)
            randomWalk.BiasVector = Vector2.Zero; // OPT: Assign zero constant rather than multiply by 0f.

        var pushStrength = _random.NextFloat(randomWalk.MinSpeed, randomWalk.MaxSpeed);

        _physics.SetLinearVelocity(uid, physics.LinearVelocity * randomWalk.AccumulatorRatio + pushVec * pushStrength, body: physics);
    }

    /// <summary>
    /// Syncs up a random walker step timing when the component starts up.
    /// </summary>
    /// <param name="uid">The uid of the random walker to start up.</param>
    /// <param name="comp">The state of the random walker to start up.</param>
    /// <param name="args">The startup prompt arguments.</param>
    private void OnRandomWalkStartup(EntityUid uid, RandomWalkComponent comp, ComponentStartup args)
    {
        if (comp.StepOnStartup)
            Update(uid, comp);
        else
            comp.NextStepTime = _timing.CurTime + TimeSpan.FromSeconds(
                _random.NextDouble(comp.MinStepCooldown.TotalSeconds, comp.MaxStepCooldown.TotalSeconds));
    }
}
