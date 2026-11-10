#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Destructible;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared.Destructible;
using Content.Shared.GameTicking;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Records the events the mover raises on a vehicle, standing in for the systems that subscribe to them later. It listens
/// on the physics component, which every vehicle has, so it can never clash with a real system's subscription.
/// </summary>
[Reflect(false)]
public sealed partial class VehicleEventProbeSystem : EntitySystem
{
    public readonly List<(EntityUid Vehicle, VehicleCollisionEvent Event)> Collisions = new();
    public readonly List<(EntityUid Vehicle, VehicleOpenTileAttemptEvent Event)> OpenTileAttempts = new();

    /// <summary>How many times each destructible entity was destroyed: a thing destroyed through its thresholds and then again is counted twice.</summary>
    public readonly Dictionary<EntityUid, int> Destructions = new();

    [Dependency] private ThrowingSystem _throwing = default!;

    /// <summary>Whether to let vehicles onto empty tiles, as the falling system will.</summary>
    public bool AllowOpenTiles;

    /// <summary>
    /// A throw to make at the start of the next physics step, where a ship impact makes its own: after the movers have
    /// run and right before the bodies are integrated, so the velocity it gives is used before any mover can clear it.
    /// </summary>
    public (EntityUid Target, Vector2 Direction, float Speed)? PendingThrow;

    public override void Initialize()
    {
        SubscribeLocalEvent<PhysicsComponent, VehicleCollisionEvent>(OnCollision);
        SubscribeLocalEvent<PhysicsComponent, VehicleOpenTileAttemptEvent>(OnOpenTile);
        // On the body, not on Destructible: only one system may take an event from a component, and the ship status system has that one.
        SubscribeLocalEvent<PhysicsComponent, DestructionEventArgs>(OnDestruction);
        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent ev)
    {
        if (PendingThrow is not { } pending)
            return;

        PendingThrow = null;
        _throwing.TryThrow(pending.Target, pending.Direction, baseThrowSpeed: pending.Speed);
    }

    private void OnCollision(Entity<PhysicsComponent> ent, ref VehicleCollisionEvent args)
    {
        Collisions.Add((ent.Owner, args));
    }

    private void OnDestruction(EntityUid uid, PhysicsComponent comp, DestructionEventArgs args)
    {
        Destructions[uid] = Destructions.GetValueOrDefault(uid) + 1;
    }

    private void OnOpenTile(Entity<PhysicsComponent> ent, ref VehicleOpenTileAttemptEvent args)
    {
        OpenTileAttempts.Add((ent.Owner, args));
        if (AllowOpenTiles)
            args.Allow = true;
    }

    /// <summary>Forgets what was recorded and stops allowing open tiles; a pooled pair is not restarted between tests, so each rig calls this.</summary>
    public void Reset()
    {
        Collisions.Clear();
        OpenTileAttempts.Clear();
        Destructions.Clear();
        AllowOpenTiles = false;
        PendingThrow = null;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        Reset();
    }
}
