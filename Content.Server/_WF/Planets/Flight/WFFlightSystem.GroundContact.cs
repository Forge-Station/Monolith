using System.Numerics;
using System.Linq;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Planets.Flight;

public sealed partial class WFFlightSystem
{
    private void EnsureGroundGrind(Entity<MapGridComponent> hull, WFSkidComponent skid)
    {
        if (skid.Loop is { } loop && !TerminatingOrDeleted(loop) && _timing.CurTime < skid.NextLoopRefresh)
            return;
        skid.Loop = _audio.Stop(skid.Loop);
        var coords = new EntityCoordinates(hull, hull.Comp.LocalAABB.Center);
        var centre = _transform.ToMapCoordinates(coords);
        var halfSize = hull.Comp.LocalAABB.Size.Length() * 0.5f;
        var range = halfSize + 80f;
        skid.Loop = _audio.PlayStatic(SkidSound, _audience.Aboard(hull).AddInRange(centre, range), coords, true,
            AudioParams.Default.WithLoop(true).WithVolume(6f).WithReferenceDistance(Math.Max(8f, halfSize))
                .WithMaxDistance(range).WithRolloffFactor(1f))?.Entity;
        skid.NextLoopRefresh = _timing.CurTime + TimeSpan.FromSeconds(10);
    }

    /// <summary>Terrain clearance is the collision path for wrecks; react before the blocking rock is removed.</summary>
    public void GroundObstacleImpact(EntityUid grid, Vector2 worldPosition)
    {
        if (!TryComp<WFSkidComponent>(grid, out var skid) || !TryComp<MapGridComponent>(grid, out var hull)
            || !TryComp<PhysicsComponent>(grid, out var body) || !_zLevels.WfHasSkidGround(grid)
            || !TryHeading(body.LinearVelocity, out var heading, out var speed) || speed <= SkidStopSpeed
            || _timing.CurTime < skid.NextImpactSound)
            return;
        // Many rock tiles can be hit in the same frame. One bounded contact per section avoids audio floods.
        skid.NextImpactSound = _timing.CurTime + TimeSpan.FromSeconds(0.75);
        var local = Vector2.Transform(worldPosition, _transform.GetInvWorldMatrix(grid));
        WearSlidingHull((grid, hull), skid, Math.Clamp(speed / SkidRamSpeed, 0.5f, 3f), heading, local);
        skid.ImpactStream = _audio.Stop(skid.ImpactStream);
        var coords = new EntityCoordinates(grid, local);
        var range = hull.LocalAABB.Size.Length() * 0.5f + 80f;
        skid.ImpactStream = _audio.PlayStatic(HardLandingSound,
            _audience.Aboard(grid).AddInRange(_transform.ToMapCoordinates(coords), range), coords, true,
            AudioParams.Default.WithVolume(6f).WithReferenceDistance(12f).WithMaxDistance(range))?.Entity;
    }

    /// <summary>Light abrasion on a few contact tiles and their anchored structures, including detached sections.</summary>
    private void WearSlidingHull(Entity<MapGridComponent> hull, WFSkidComponent skid, float amount,
        Vector2 heading, Vector2? contact = null)
    {
        if (!float.IsFinite(amount) || amount <= 0f)
            return;
        var lattice = new Tile(_scarTiles["Lattice"].TileId);
        var indices = new List<Vector2i>();
        var tiles = _map.GetAllTilesEnumerator(hull, hull.Comp);
        while (tiles.MoveNext(out var tile))
            if (tile.Value.Tile.TypeId != lattice.TypeId)
                indices.Add(tile.Value.GridIndices);
        var localHeading = (-_transform.GetWorldRotation(hull)).RotateVec(heading);
        var selected = contact is { } point
            ? indices.OrderBy(t => Vector2.DistanceSquared(((Vector2)t + new Vector2(0.5f)) * hull.Comp.TileSize, point))
            : indices.OrderByDescending(t => Projection(t, localHeading));
        var damaged = new HashSet<EntityUid>();
        var floors = new List<(Vector2i, Tile)>();
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", FixedPoint2.New(amount));
        foreach (var index in selected.Take(4))
        {
            var total = skid.TileDamage.GetValueOrDefault(index) + amount;
            skid.TileDamage[index] = total;
            if (total >= SkidTileThreshold && floors.Count < Math.Max(0, indices.Count - SkidMinTiles))
            {
                // Expose lattice instead of removing floor, so slides never leave massless or tiny grids.
                floors.Add((index, lattice));
                skid.TileDamage.Remove(index);
            }
            var anchored = _map.GetAnchoredEntitiesEnumerator(hull, hull.Comp, index);
            while (anchored.MoveNext(out var entity))
                if (!HasComp<MobStateComponent>(entity) && HasComp<DamageableComponent>(entity))
                    damaged.Add(entity.Value);
        }
        foreach (var entity in damaged)
            if (!TerminatingOrDeleted(entity))
                // Ignores resistances: wall armour would otherwise negate these small hits entirely.
                _crashDamage.TryChangeDamage(entity, damage, ignoreResistances: true, canSever: false);
        if (floors.Count > 0)
            _map.SetTiles(hull, hull.Comp, floors);
    }

    private void StopGroundSounds(WFSkidComponent skid)
    {
        skid.Loop = _audio.Stop(skid.Loop);
        skid.ImpactStream = _audio.Stop(skid.ImpactStream);
    }

    private void OnSkidShutdown(Entity<WFSkidComponent> ent, ref ComponentShutdown args) => StopGroundSounds(ent.Comp);
}
