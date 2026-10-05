using System.Linq;
using Robust.Shared.Random;
using System.Numerics;
using Content.Server._WF.Shrapnel;
using Content.Server.Destructible;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Explosion.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Stunnable;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Planets.Flight;

public sealed partial class WFFlightSystem
{

    [Dependency] private Content.Server.Shuttles.Systems.ThrusterSystem _crashThrusters = default!;
    [Dependency] private WFCrashApcFaultSystem _crashApcs = default!;
    [Dependency] private IRobustRandom _crashRandom = default!;
    [Dependency] private DamageableSystem _crashDamage = default!;
    [Dependency] private SharedStunSystem _crashStun = default!;
    [Dependency] private SharedDestructibleSystem _crashDestructible = default!;
    [Dependency] private IConfigurationManager _crashCfg = default!;

    /// <summary>Hull tiles kept between a crash burst and anything that goes off; a burst hurts the tiles beside it.</summary>
    private const int CrashBurstClearance = 2;

    /// <summary>Small mass-balanced outward impulses separate sections without launching wreckage.</summary>
    private void SeparateCrashSections(EntityUid original, EntityUid[] fragments)
    {
        if (!TryComp<WFCrashImpactComponent>(original, out var impact) || _timing.CurTime >= impact.NextImpact)
            return;
        var pieces = fragments.Append(original).Where(uid => HasComp<MapGridComponent>(uid) && HasComp<PhysicsComponent>(uid)).ToArray();
        if (pieces.Length < 2) return;
        var centre = Vector2.Zero;
        var mass = 0f;
        foreach (var uid in pieces)
        {
            var body = Comp<PhysicsComponent>(uid);
            var weight = Math.Max(body.Mass, 0.01f);
            centre += Vector2.Transform(Comp<MapGridComponent>(uid).LocalAABB.Center, _transform.GetWorldMatrix(uid)) * weight;
            mass += weight;
        }
        centre /= mass;
        var kicks = new Dictionary<EntityUid, Vector2>();
        var average = Vector2.Zero;
        foreach (var uid in pieces)
        {
            var delta = Vector2.Transform(Comp<MapGridComponent>(uid).LocalAABB.Center, _transform.GetWorldMatrix(uid)) - centre;
            var kick = delta.LengthSquared() > 0.001f ? Vector2.Normalize(delta) * 0.8f : Vector2.Zero;
            kicks[uid] = kick;
            average += kick * Math.Max(Comp<PhysicsComponent>(uid).Mass, 0.01f) / mass;
        }
        var spinDirection = _crashRandom.Next(2) == 0 ? -1f : 1f;
        foreach (var uid in pieces)
        {
            var body = Comp<PhysicsComponent>(uid);
            if (float.IsFinite(body.LinearVelocity.LengthSquared()))
                _physics.SetLinearVelocity(uid, body.LinearVelocity + kicks[uid] - average, body: body);
            if (!float.IsFinite(body.Mass) || body.Mass <= 0f)
                continue;
            var inheritedSpin = float.IsFinite(body.AngularVelocity) ? body.AngularVelocity : 0f;
            // Split grids default to fixed rotation. Unlock only these substantial crash sections.
            _physics.SetFixedRotation(uid, false, body: body);
            var sizeFactor = Math.Clamp(MathF.Sqrt(mass / (pieces.Length * body.Mass)), 0.65f, 1.5f);
            var spin = spinDirection * _crashRandom.NextFloat(0.2f, 0.35f) * sizeFactor;
            _physics.SetAngularVelocity(uid, Math.Clamp(inheritedSpin + spin, -0.65f, 0.65f), body: body);
            spinDirection = -spinDirection;
        }
    }

    /// <summary>One crew shock per touchdown, even if several landing callbacks run in the same tick.</summary>
    private bool ImpactCrew(EntityUid grid, float severity)
    {
        var impact = EnsureComp<WFCrashImpactComponent>(grid);
        if (_timing.CurTime < impact.NextImpact)
            return false;
        impact.NextImpact = _timing.CurTime + TimeSpan.FromSeconds(1);
        _crashApcs.DamageOverloadedApcs(grid);
        severity = float.IsFinite(severity) ? Math.Clamp(severity, 0f, 2f) : 1f;
        var speed = TryComp<PhysicsComponent>(grid, out var body) ? body.LinearVelocity.Length() : 0f;
        speed = float.IsFinite(speed) ? Math.Clamp(speed, 0f, 20f) : 0f;
        var amount = Math.Clamp(5f + 30f * severity * severity + speed * 0.5f, 5f, 85f);
        var crew = new HashSet<Entity<MobStateComponent>>();
        // Buckled occupants are children of their seats, not direct children of the hull.
        var descendants = new Stack<EntityUid>();
        descendants.Push(grid);
        while (descendants.TryPop(out var parent))
        {
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                descendants.Push(child);
                if (TryComp<MobStateComponent>(child, out var mob))
                    crew.Add((child, mob));
            }
        }
        foreach (var person in crew)
        {
            var uid = person.Owner;
            if (person.Comp.CurrentState == MobState.Dead
                || !HasComp<DamageableComponent>(uid))
                continue;
            var restrained = TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled;
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", (int) MathF.Ceiling(amount * (restrained ? 0.5f : 1f)));
            _crashDamage.TryChangeDamage(uid, damage, ignoreResistances: true, canSever: false);
            _crashStun.TryKnockdown(uid, TimeSpan.FromSeconds(Math.Clamp(2f + severity * 3f, 2f, 7f)), true);
        }
        return true;
    }

    /// <summary>Break a crashing ship into substantial wreck sections rather than exploding every hull tile.</summary>
    public void StructuralCrash(Entity<MapGridComponent> hull, float severity)
    {
        if (!ImpactCrew(hull, severity))
            return;
        _crashThrusters.WfCaptureCrashThrust(hull);
        BeginSkid(hull);
        var tiles = new HashSet<Vector2i>();
        var all = _map.GetAllTilesEnumerator(hull, hull.Comp);
        while (all.MoveNext(out var tile))
            tiles.Add(tile.Value.GridIndices);
        var speed = TryComp<PhysicsComponent>(hull, out var body) ? body.LinearVelocity.Length() : 0f;
        var parts = severity >= 1.4f || speed >= 14f ? 4 : severity >= 1.1f || speed >= 8f ? 3 : 2;
        var cut = WFCrashFractures.Plan(tiles, parts, _crashRandom.Next());
        if (cut.Count == 0)
            return; // Small or unsuitable hulls crumple without being turned into tile-sized confetti.

        // Snapshot ground-relative burst positions before removing tiles triggers grid splitting.
        var ground = Transform(hull).MapUid!.Value;
        var matrix = _transform.GetWorldMatrix(hull);
        var ordered = cut.OrderBy(t => t.X).ThenBy(t => t.Y).ToArray();
        var bursts = new List<(Vector2i Tile, Vector2 Position)>();
        for (var i = 0; i < Math.Min(parts, ordered.Length); i++)
        {
            var tile = ordered[i * ordered.Length / parts];
            bursts.Add((tile, Vector2.Transform(((Vector2) tile + new Vector2(0.5f)) * hull.Comp.TileSize, matrix)));
        }

        var remaining = new HashSet<Vector2i>(tiles);
        remaining.ExceptWith(cut);
        foreach (var section in WFCrashFractures.Sections(remaining).Take(4))
        {
            var centre = section.Aggregate(Vector2.Zero, (sum, tile) => sum + (Vector2) tile) / section.Count;
            var tile = section.MinBy(t => Vector2.DistanceSquared((Vector2) t, centre));
            bursts.Add((tile, Vector2.Transform(((Vector2) tile + new Vector2(0.5f)) * hull.Comp.TileSize, matrix)));
        }

        var machinery = new HashSet<EntityUid>();
        foreach (var index in cut)
        {
            var anchored = _map.GetAnchoredEntitiesEnumerator(hull, hull.Comp, index);
            while (anchored.MoveNext(out var uid))
                if (!HasComp<MobStateComponent>(uid))
                    machinery.Add(uid.Value);
        }
        foreach (var uid in machinery)
        {
            _crashDestructible.BreakEntity(uid);
            if (!TerminatingOrDeleted(uid))
                QueueDel(uid);
        }
        var hazards = CrashHazardTiles(hull, machinery);
        Comp<WFCrashImpactComponent>(hull).LatticeSeam = cut;
        CutSeam(hull, cut);
        foreach (var (tile, position) in bursts)
        {
            // Explicit above-deck animation remains visible when the explosion flood selects the terrain grid below.
            Spawn("WFCrashBurst", new EntityCoordinates(ground, position));
            // A burst would set off a hazard beside it, and a gyroscope's shrapnel chains through the wreck.
            if (hazards.Any(e => Math.Max(Math.Abs(e.X - tile.X), Math.Abs(e.Y - tile.Y)) <= CrashBurstClearance))
                continue;
            _explosion.QueueExplosion(new EntityCoordinates(ground, position), ExplosionSystem.DefaultExplosionPrototypeId,
                15f, 3f, 3.5f, cause: hull, maxTileBreak: 0, canCreateVacuum: false, addLog: false, silent: true);
        }
    }

    /// <summary>Hull tiles holding a crash hazard, fitted or stowed, other than the seam machinery being removed.</summary>
    private HashSet<Vector2i> CrashHazardTiles(Entity<MapGridComponent> hull, HashSet<EntityUid> removed)
    {
        var tiles = new HashSet<Vector2i>();
        var explosives = EntityQueryEnumerator<ExplosiveComponent, TransformComponent>();
        while (explosives.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == hull.Owner && !removed.Contains(uid))
                tiles.Add(_map.WorldToTile(hull, hull.Comp, _transform.GetWorldPosition(xform)));
        }

        var destructibles = EntityQueryEnumerator<DestructibleComponent, TransformComponent>();
        while (destructibles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == hull.Owner && !removed.Contains(uid) && IsCrashHazard(uid))
                tiles.Add(_map.WorldToTile(hull, hull.Comp, _transform.GetWorldPosition(xform)));
        }
        return tiles;
    }

    /// <summary>Whether destroying the entity sets something off: an explosive, or a destructible that bursts into shrapnel.</summary>
    public bool IsCrashHazard(EntityUid uid)
    {
        if (HasComp<ExplosiveComponent>(uid))
            return true;

        return TryComp<DestructibleComponent>(uid, out var destructible)
            && destructible.Thresholds.Any(threshold => threshold.Behaviors.Any(behavior => behavior is ShrapnelBehavior));
    }

    /// <summary>Removes the seam with grid splitting forced on; the development config preset turns it off for mapping.</summary>
    private void CutSeam(Entity<MapGridComponent> hull, HashSet<Vector2i> cut)
    {
        // The engine splits inside SetTiles, so the override only has to cover this call.
        var splitting = _crashCfg.GetCVar(CVars.GridSplitting);
        if (!splitting)
            _crashCfg.SetCVar(CVars.GridSplitting, true);
        try
        {
            _map.SetTiles(hull, hull.Comp, cut.Select(index => (index, Tile.Empty)).ToList());
        }
        finally
        {
            if (!splitting)
                _crashCfg.SetCVar(CVars.GridSplitting, false);
        }
    }
}
