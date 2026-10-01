using System.Linq;
using System.Numerics;
using Content.Server.Body.Components;
using Content.Server.Decals;
using Content.Shared._WF.Wolfmed.Gore;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Decals;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Gore;

/// <summary>
/// G1: the spray a hit throws off a body of flesh, and the mark it leaves where it lands. The spray is a
/// networked effect the client slides along the exact angle of the hit (FIX1); the landing is decided
/// here, once, at spawn time, and placed when the spray would have got there.
/// </summary>
/// <remarks>
/// Server authoritative and deliberately cheap: one grid walk of at most three tiles per hit (already
/// throttled per body by <c>WolfmedWoundSfxSystem</c>), no physics on anything, and nothing that ticks
/// once a splat is down. The only loop is the short queue of sprays still in the air. A wall splat has to
/// be an entity because decals draw under walls; a floor splat is a cleanable decal, so space cleaner
/// already washes it off.
/// </remarks>
public sealed class WolfmedGoreSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    private readonly List<PendingSplat> _pending = new();
    private readonly HashSet<Entity<WolfmedBloodSplatComponent>> _splatsOnTile = new();
    private readonly HashSet<Entity<WolfmedCleanableComponent>> _cleanablesOnTile = new();

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (_pending.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].At > now)
                continue;

            Place(_pending[i]);
            _pending.RemoveAt(i);
        }
    }

    /// <summary>
    /// Throws a spray off <paramref name="body"/> and queues whatever it lands on. Returns null when the
    /// body has no blood to throw, which is the caller's cue to fall back to V4's mist.
    /// </summary>
    public EntityUid? TrySpawnSplatter(
        EntityUid body,
        WolfmedHitSplatterSpec spec,
        Vector2? direction,
        FixedPoint2 severity)
    {
        if (!spec.Enabled || spec.States.Count == 0 || TerminatingOrDeleted(body) ||
            GetBloodColor(body) is not { } color)
            return null;

        var xform = Transform(body);
        var grid = xform.GridUid;
        var angle = ResolveAngle(direction);
        var tiles = spec.GetDistance(severity);

        // Parented to the grid, not the map, so the spray rides a moving shuttle. The angle stays a world
        // angle: the sprite does not rotate with its parent, so nothing else would line up.
        var effect = Spawn(spec.Effect, _transform.GetMoverCoordinates(body, xform));
        var splatter = EnsureComp<WolfmedHitSplatterComponent>(effect);
        splatter.Angle = (float) angle.Theta;
        splatter.Distance = tiles;
        splatter.Color = color;
        splatter.State = _random.Pick(spec.States);
        splatter.Travel = (float) spec.Travel.TotalSeconds;
        Dirty(effect, splatter);

        if (grid is { } gridUid && TryComp(gridUid, out MapGridComponent? gridComp))
            Queue(spec, (gridUid, gridComp), xform, angle, tiles, color);

        return effect;
    }

    /// <summary>
    /// Which way a hit was travelling, in world space, or null when nothing behind it says. A projectile
    /// answers with its own velocity; anything else with the line from where it was to the victim.
    /// </summary>
    public Vector2? GetHitDirection(EntityUid body, EntityUid? origin, EntityUid? tool)
    {
        if (TerminatingOrDeleted(body))
            return null;

        // A projectile is at the victim by the time it wounds, so its position says nothing; its velocity
        // is the line it came in on and already points away from whoever fired it.
        if (tool is { } projectile && !TerminatingOrDeleted(projectile) &&
            TryComp(projectile, out PhysicsComponent? physics) && physics.LinearVelocity.LengthSquared() > 0.01f)
        {
            // LinearVelocity is in the PARENT's frame. A bullet crossing a ship is parented to the ship, so on a
            // turned grid the raw vector points the wrong way by the grid's rotation. Turn it into world
            // orientation, but do not add the grid's own velocity: the line that matters is relative to the deck.
            var parent = Transform(projectile).ParentUid;
            return parent.IsValid()
                ? _transform.GetWorldRotation(parent).RotateVec(physics.LinearVelocity)
                : physics.LinearVelocity;
        }

        var target = _transform.GetMapCoordinates(body);
        foreach (var source in new[] { tool, origin })
        {
            if (source is not { } uid || uid == body || TerminatingOrDeleted(uid))
                continue;

            var from = _transform.GetMapCoordinates(uid);
            if (from.MapId != target.MapId)
                continue;

            var away = target.Position - from.Position;
            if (away.LengthSquared() > 0.01f)
                return away;
        }

        return null;
    }

    /// <summary>The colour of whatever this body bleeds, or null when it does not bleed at all.</summary>
    public Color? GetBloodColor(EntityUid body)
    {
        return TryComp(body, out BloodstreamComponent? bloodstream) &&
               _prototypes.TryIndex<ReagentPrototype>(bloodstream.BloodReagent, out var reagent)
            ? reagent.SubstanceColor
            : null;
    }

    /// <summary>
    /// Washes every cleanable Wolfmed splat off one tile, up to what the reagent can pay for. Returns the
    /// units spent. Called by the space cleaner tile reaction.
    /// </summary>
    public float CleanTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile, float budget)
    {
        var spent = 0f;
        var centre = new EntityCoordinates(gridUid, (Vector2) tile + new Vector2(0.5f, 0.5f));
        _cleanablesOnTile.Clear();
        _lookup.GetEntitiesInRange(centre, 0.45f, _cleanablesOnTile);

        foreach (var cleanable in _cleanablesOnTile)
        {
            if (spent + cleanable.Comp.CleanCost > budget)
                break;

            spent += cleanable.Comp.CleanCost;
            QueueDel(cleanable.Owner);
        }

        // Playtest 5: the floor half too, for a reagent that is no cleaner of decals in general (a mop's water). Only
        // Wolfmed's own blood and gibs go, so a spilled cup never washes a mapper's paint.
        if (TryComp(gridUid, out DecalGridComponent? decalGrid))
        {
            var bounds = _lookup.GetLocalBounds(tile, grid.TileSize).Enlarged(0.5f).Translated(new Vector2(-0.5f, -0.5f));
            foreach (var (index, decal) in _decals.GetDecalsIntersecting(gridUid, bounds, decalGrid).ToList())
            {
                if (!decal.Cleanable || !decal.Id.StartsWith(WolfmedDecalPrefix) || spent + DecalCleanCost > budget)
                    continue;

                _decals.RemoveDecal(gridUid, index, decalGrid);
                spent += DecalCleanCost;
            }
        }

        return spent;
    }

    /// <summary>Every decal Wolfmed leaves starts with this: the floor splats and the gibs.</summary>
    public const string WolfmedDecalPrefix = "WFWolfmed";

    /// <summary>Reagent units one Wolfmed decal costs to wash away, the same as CleanDecalsReaction charges.</summary>
    private const float DecalCleanCost = 0.25f;

    /// <summary>
    /// Decides now, with one walk of at most three tiles, whether the spray ends on a wall or on the
    /// deck, and remembers it until the spray would have arrived.
    /// </summary>
    private void Queue(
        WolfmedHitSplatterSpec spec,
        Entity<MapGridComponent> grid,
        TransformComponent xform,
        Angle angle,
        float tiles,
        Color color)
    {
        var start = _map.TileIndicesFor(grid.Owner, grid.Comp, _transform.GetMapCoordinates(xform.Owner));
        // The walk is in the grid's own frame; the angle is a world angle, so the grid's rotation comes off.
        var step = (angle - _transform.GetWorldRotation(grid.Owner)).ToVec();
        var reach = Math.Max(1, (int) MathF.Round(tiles));
        var landing = start;
        var wall = false;

        // FIX1: a step walk along the exact line rather than one cardinal, so a diagonal spray lands on the
        // wall a diagonal spray would hit. Bounded: four samples a tile, at most three tiles.
        var from = (Vector2) start + new Vector2(0.5f, 0.5f);
        var samples = reach * SamplesPerTile;
        for (var i = 1; i <= samples; i++)
        {
            var point = from + step * (i / (float) SamplesPerTile);
            var tile = new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));
            if (tile == landing)
                continue;

            if (_turf.IsTileBlocked(grid.Owner, tile, CollisionGroup.Impassable, grid.Comp))
            {
                landing = tile;
                wall = true;
                break;
            }

            landing = tile;
        }

        _pending.Add(new PendingSplat(spec, grid.Owner, landing, angle, wall, color,
            _timing.CurTime + spec.Travel));
    }

    /// <summary>Samples taken per tile of reach when walking the spray's line.</summary>
    private const int SamplesPerTile = 4;

    /// <summary>Puts the splat down, if the grid it was aimed at is still there.</summary>
    private void Place(PendingSplat splat)
    {
        if (!TryComp(splat.Grid, out MapGridComponent? grid))
            return;

        if (splat.Wall)
            PlaceWall(splat, grid);
        else
            PlaceFloor(splat, grid);
    }

    private void PlaceWall(PendingSplat splat, MapGridComponent grid)
    {
        if (splat.Spec.WallStates.Count == 0)
            return;

        var centre = new EntityCoordinates(splat.Grid, (Vector2) splat.Tile + new Vector2(0.5f, 0.5f));
        Cap(centre, splat.Spec.MaxPerTile);

        var entity = Spawn(splat.Spec.WallSplat, centre);
        var comp = EnsureComp<WolfmedBloodSplatComponent>(entity);
        comp.Color = splat.Color;
        comp.State = _random.Pick(splat.Spec.WallStates);
        // Facing back the way it came, so the mark reads as something that hit the wall from the room.
        comp.Angle = (float) (splat.Angle + Math.PI).Theta;
        Dirty(entity, comp);

        // Turn the entity itself to face back at the source, snapped to the grid's own axes (walls are grid
        // aligned). A vector angle (0 = +X) becomes an entity rotation (0 = south) by a quarter turn; the
        // grid's world rotation comes off because this is a local rotation.
        var facing = new Angle(comp.Angle) + MathHelper.PiOver2 - _transform.GetWorldRotation(splat.Grid);
        var snapped = Math.Round(facing.Theta / MathHelper.PiOver4) * MathHelper.PiOver4;
        _transform.SetLocalRotation(entity, new Angle(snapped));
    }

    private void PlaceFloor(PendingSplat splat, MapGridComponent grid)
    {
        if (splat.Spec.FloorDecals.Count == 0 ||
            _turf.IsSpace(_map.GetTileRef(splat.Grid, grid, splat.Tile)))
            return;

        // Decals stack invisibly and never expire, so the cap matters more here than on the wall.
        var bounds = Box2.FromDimensions((Vector2) splat.Tile, Vector2.One);
        var already = 0;
        foreach (var (_, decal) in _decals.GetDecalsIntersecting(splat.Grid, bounds))
        {
            if (decal.Cleanable && splat.Spec.FloorDecals.Contains(decal.Id))
                already++;
        }

        if (already >= splat.Spec.MaxPerTile)
            return;

        _decals.TryAddDecal(_random.Pick(splat.Spec.FloorDecals),
            new EntityCoordinates(splat.Grid, splat.Tile),
            out _,
            splat.Color,
            cleanable: true);
    }

    /// <summary>Throws the oldest splats on a tile away so a firefight cannot pile dozens on one wall.</summary>
    private void Cap(EntityCoordinates centre, int max)
    {
        _splatsOnTile.Clear();
        _lookup.GetEntitiesInRange(centre, 0.45f, _splatsOnTile);

        // Lowest uid is the one that has been there longest. Deleted outright rather than queued: several
        // sprays can land on one tile in one tick, and a queued splat is still in the lookup.
        var count = 0;
        var oldest = EntityUid.Invalid;
        foreach (var splat in _splatsOnTile)
        {
            if (TerminatingOrDeleted(splat.Owner))
                continue;

            count++;
            if (!oldest.IsValid() || splat.Owner.CompareTo(oldest) < 0)
                oldest = splat.Owner;
        }

        if (count >= max && oldest.IsValid())
            Del(oldest);
    }

    /// <summary>
    /// FIX1: the exact world angle the spray leaves on, kept as an angle rather than snapped to a
    /// cardinal. Nothing usable behind the hit picks a random way.
    /// </summary>
    public Angle ResolveAngle(Vector2? direction) =>
        direction is { } vector && vector.LengthSquared() > 0.01f
            ? new Angle(vector)
            : _random.NextAngle();

    /// <summary>A spray still in the air, and what it will leave when it lands.</summary>
    private readonly record struct PendingSplat(
        WolfmedHitSplatterSpec Spec,
        EntityUid Grid,
        Vector2i Tile,
        Angle Angle,
        bool Wall,
        Color Color,
        TimeSpan At);
}
