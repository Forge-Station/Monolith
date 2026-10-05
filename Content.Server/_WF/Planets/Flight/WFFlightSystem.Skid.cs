using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Hard-landing skids: the leading edge grinds off and anything under the hull is crushed.</summary>
public sealed partial class WFFlightSystem
{
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>Speed (m/s) under which the hull has stopped and the skid is over.</summary>
    public const float SkidStopSpeed = 0.5f;

    /// <summary>Reference rock-impact speed (m/s) for one point of contact damage.</summary>
    public const float SkidRamSpeed = 4f;

    /// <summary>Damage a leading-edge tile takes per second, per metre per second of hull speed.</summary>
    public const float SkidTileDamageRate = 0.15f;

    /// <summary>Accumulated damage at which a leading-edge tile is torn off the hull.</summary>
    public const float SkidTileThreshold = 40f;

    /// <summary>Damage every hull tile takes at free-fall touchdown speed; not enough alone to tear one off.</summary>
    public const float ImpactTileDamage = 12f;

    /// <summary>Extra touchdown damage on leading-edge tiles when the hull lands with planar speed.</summary>
    public const float ImpactEdgeDamage = 45f;

    // Leading-edge depth, in tiles of projection behind the foremost tile.
    private const float SkidEdgeDepth = 1.5f;

    // Small silent blast where a tile tears off.
    private const float SkidTileIntensity = 2f;

    private const float SkidTileSlope = 2f;
    private const float SkidTileMaxIntensity = 2f;

    /// <summary>Interval for skid damage, crushing and ploughing, so their sounds don't fire every tick.</summary>
    public static readonly TimeSpan SkidBiteInterval = TimeSpan.FromSeconds(0.25);

    // Never grind below this; a massless grid breaks the physics solve.
    private const int SkidMinTiles = 4;

    // Per-bite tear-off cap; damage over it stays on the tile for the next bite.
    private const int SkidMaxBiteTiles = 24;

    private readonly List<Vector2i> _skidEdge = new();

    /// <summary>Grinds every skidding hull down, and lets go of the ones that have come to rest.</summary>
    private void UpdateSkids(float frameTime)
    {
        _skidScan.Clear();

        var query = EntityQueryEnumerator<WFSkidComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            _skidScan.Add(uid);
        }

        foreach (var grid in _skidScan)
        {
            if (TerminatingOrDeleted(grid) || !TryComp<WFSkidComponent>(grid, out var skid))
                continue;

            if (!_zLevels.WfHasSkidGround(grid)
                || !TryComp<PhysicsComponent>(grid, out var body) || !TryComp<MapGridComponent>(grid, out var gridComp))
            {
                EndSkid(grid, skid);
                continue;
            }

            // A non-finite velocity would pass every comparison and spread NaN to the hull, so zero it.
            TryHeading(body.LinearVelocity, out var heading, out var speed);
            if (!float.IsFinite(body.LinearVelocity.LengthSquared()))
                _physics.SetLinearVelocity(grid, Vector2.Zero, body: body);
            if (!float.IsFinite(body.AngularVelocity))
                _physics.SetAngularVelocity(grid, 0f, body: body);
            var rotatingSpeed = MathF.Abs(body.AngularVelocity) * gridComp.LocalAABB.Size.Length() * 0.5f;
            if (speed <= SkidStopSpeed && rotatingSpeed <= SkidStopSpeed)
            {
                EndSkid(grid, skid);
                continue;
            }
            if (speed <= SkidStopSpeed)
            {
                heading = _transform.GetWorldRotation(grid).ToWorldVec();
                speed = rotatingSpeed;
            }

            EnsureGroundGrind((grid, gridComp), skid);

            if (_timing.CurTime < skid.NextBite)
                continue;

            var elapsed = (float) SkidBiteInterval.TotalSeconds;
            skid.NextBite = _timing.CurTime + SkidBiteInterval;

            // Anything under the footprint is crushed as by an FTL arrival.
            _zLevels.WfClearLandingObstacles(grid, reportImpacts: true);
            ScarSkidGround((grid, gridComp), skid);
            WearSlidingHull((grid, gridComp), skid, Math.Clamp(speed * SkidTileDamageRate * elapsed, 0f, 0.5f), heading);
            if (skid.Debris)
                continue; // Every section wears; only the main hull runs the occupant crush pass.
            _shuttle.Smimsh(grid);


        }
    }

    /// <summary>Survivable touchdown; <paramref name="severity"/> is touchdown speed over free-fall speed.</summary>
    public void HardLanding(Entity<MapGridComponent> grid, float severity)
    {
        if (!ImpactCrew(grid.Owner, severity))
            return;
        BeginSkid(grid.Owner);

        if (!TryComp<WFSkidComponent>(grid.Owner, out var skid))
            return;

        // Math.Clamp passes NaN through unchanged.
        severity = float.IsFinite(severity) ? Math.Clamp(severity, 0f, 1f) : 0f;

        // The whole hull takes the landing; nothing comes off from this alone.
        BiteTiles(grid, skid, ImpactTileDamage * severity, null);

        if (!TryComp<PhysicsComponent>(grid.Owner, out var body))
            return;

        // Straight down has no leading edge.
        if (!TryHeading(body.LinearVelocity, out var heading, out var speed) || speed <= SkidStopSpeed)
            return;

        BiteTiles(grid, skid, ImpactEdgeDamage * severity, heading);
    }

    /// <summary>Normalises a velocity into heading and speed; false for zero or non-finite velocity.</summary>
    private static bool TryHeading(Vector2 velocity, out Vector2 heading, out float speed)
    {
        heading = Vector2.Zero;
        speed = 0f;

        var lengthSquared = velocity.LengthSquared();

        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
            return false;

        speed = MathF.Sqrt(lengthSquared);
        heading = velocity / speed;
        return true;
    }

    /// <summary>Damages the leading edge, or the whole hull without a heading; returns tiles torn off.</summary>
    private int BiteTiles(Entity<MapGridComponent> grid, WFSkidComponent skid, float damage, Vector2? heading)
    {
        if (!float.IsFinite(damage) || damage <= 0f)
            return 0;

        // Heading in the hull's local frame, where tiles are indexed.
        var local = heading is { } world ? (-_transform.GetWorldRotation(grid.Owner)).RotateVec(world) : Vector2.Zero;

        _skidEdge.Clear();
        var best = float.MinValue;

        var tiles = _map.GetAllTilesEnumerator(grid.Owner, grid.Comp);
        while (tiles.MoveNext(out var tileRef))
        {
            var indices = tileRef.Value.GridIndices;

            if (heading is not null)
                best = MathF.Max(best, Projection(indices, local));

            _skidEdge.Add(indices);
        }

        var lost = 0;

        // Capped per bite and by the minimum hull size; excess damage stays on the tile.
        var budget = Math.Min(SkidMaxBiteTiles, _skidEdge.Count - SkidMinTiles);

        foreach (var indices in _skidEdge)
        {
            if (heading is not null && Projection(indices, local) < best - SkidEdgeDepth)
                continue;

            var total = skid.TileDamage.GetValueOrDefault(indices) + damage;

            if (total < SkidTileThreshold || lost >= budget)
            {
                skid.TileDamage[indices] = total;
                continue;
            }

            skid.TileDamage.Remove(indices);
            lost++;

            var coords = _map.GridTileToLocal(grid.Owner, grid.Comp, indices);
            _map.SetTile(grid.Owner, grid.Comp, indices, Tile.Empty);

            _explosion.QueueExplosion(coords,
                ExplosionSystem.DefaultExplosionPrototypeId,
                SkidTileIntensity,
                SkidTileSlope,
                SkidTileMaxIntensity,
                cause: grid.Owner,
                addLog: false,
                silent: true);
        }

        return lost;
    }

    /// <summary>How far along the heading a tile's centre sits.</summary>
    private static float Projection(Vector2i indices, Vector2 heading)
    {
        return Vector2.Dot(new Vector2(indices.X + 0.5f, indices.Y + 0.5f), heading);
    }

    private void EndSkid(EntityUid grid, WFSkidComponent skid)
    {
        StopGroundSounds(skid);
        RemComp<WFSkidComponent>(grid);
    }
}
