using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.Spawning;
using Content.Server._WF.DroneRings;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Gatherable;
using Content.Server.Shuttles.Systems;
using Content.Server.Worldgen.Systems.Debris;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.AsteroidMining;

/// <summary>How a rock broke, which sets how much of it survives.</summary>
public enum WFAsteroidCrack : byte
{
    Clean,
    Overcharge,
    Shatter,
}

/// <summary>
/// Builds asteroid grids, rolls what each rock holds, and breaks rocks into smaller asteroids and ore.
/// Cracks are queued and run in <see cref="Update"/>, since one deletes the rock's whole grid.
/// </summary>
public sealed partial class WFAsteroidSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SpawnCountSystem _spawnCount = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private DebrisFeaturePlacerSystem _debris = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private WFMiningBeamSystem _beams = default!;

    private static readonly EntProtoId CrackEffect = "WFAsteroidCrackEffect";

    private static readonly SoundSpecifier CrackSound = new SoundPathSpecifier("/Audio/Effects/break_stone.ogg",
        AudioParams.Default.WithVolume(4f).WithMaxDistance(40f));

    private readonly List<(EntityUid Rock, WFAsteroidCrack Kind)> _cracks = new();
    private readonly List<(EntityUid Rock, WFAsteroidCrack Kind)> _cracking = new();
    private List<Entity<MapGridComponent>> _grids = new();
    /// <summary>Cluster markers map-initialised since the last update.</summary>
    private readonly List<EntityUid> _clusters = new();
    private readonly List<EntityUid> _filling = new();
    /// <summary>Planned rocks not yet spawned, drained a few per tick so a loading field doesn't freeze the server.</summary>
    private readonly Queue<PendingRock> _pendingRocks = new();

    /// <summary>Asteroid grids spawned per tick at most. Every grid is sent to every player, so bursts flood the network.</summary>
    public const int RocksPerTick = 4;
    // Swapped debris: the mass budget for the cluster about to spawn at each point. Keyed by exact position, which
    // holds because worldgen spawns the marker straight onto the map at the point it raised the event with.
    private readonly Dictionary<MapCoordinates, float> _budgets = new();
    private Dictionary<string, (WFAsteroidReplacementKind Kind, float Mass, string? Barren)>? _replacements;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFAsteroidGridComponent, MapInitEvent>(OnGridMapInit);
        SubscribeLocalEvent<WFAsteroidGridComponent, GridFixtureChangeEvent>(OnGridFixtures, after: new[] { typeof(ShuttleSystem) });
        SubscribeLocalEvent<WFAsteroidComponent, MapInitEvent>(OnRockMapInit);
        SubscribeLocalEvent<WFAsteroidComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<WFAsteroidComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFAsteroidComponent, GatheredEvent>(OnGathered);
        SubscribeLocalEvent<WFAsteroidClusterComponent, MapInitEvent>((uid, _, _) => _clusters.Add(uid));
        SubscribeLocalEvent<WFDebrisPlacingEvent>(OnDebrisPlacing, after: new[] { typeof(WFDroneHeldSpaceSystem) });
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _replacements = null);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _budgets.Clear();
            _clusters.Clear();
            _pendingRocks.Clear();
        });
    }

    /// <summary>
    /// Swaps an ore tile asteroid for a cluster of single rocks with the same kind and amount of ore, or now and then
    /// for a barren tile asteroid that keeps its gas deposits.
    /// </summary>
    private void OnDebrisPlacing(ref WFDebrisPlacingEvent args)
    {
        if (args.Cancelled)
            return;

        _replacements ??= BuildReplacements();
        if (!_replacements.TryGetValue(args.Proto, out var replacement))
            return;

        if (replacement.Barren is { } barren && _random.Prob(replacement.Kind.BarrenChance))
        {
            args.Proto = barren;
            return;
        }

        args.Proto = replacement.Kind.Cluster;
        _budgets[args.At] = replacement.Mass;
    }

    private Dictionary<string, (WFAsteroidReplacementKind, float, string?)> BuildReplacements()
    {
        var map = new Dictionary<string, (WFAsteroidReplacementKind, float, string?)>();
        foreach (var proto in _proto.EnumeratePrototypes<WFAsteroidReplacementPrototype>())
        {
            foreach (var (prefix, kind) in proto.Kinds)
            {
                foreach (var (size, mass) in proto.Sizes)
                {
                    if (!_proto.HasIndex<EntityPrototype>(prefix + size))
                        continue;

                    // Only the sizes the barren kind defines, which are the ones that carry gas.
                    var barren = kind.Barren + size;
                    map[prefix + size] = (kind, mass, kind.Barren != null && _proto.HasIndex<EntityPrototype>(barren) ? barren : null);
                }
            }
        }

        return map;
    }

    private void OnGridMapInit(Entity<WFAsteroidGridComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<MapGridComponent>(ent, out var grid) ||
            !_proto.Index(ent.Comp.Rock).TryComp<WFAsteroidComponent>(out var rockProto, _factory))
        {
            Log.Error($"Asteroid grid {ToPrettyString(ent)} has no grid or its rock {ent.Comp.Rock} is not an asteroid.");
            return;
        }

        // Tile centres within the rock's silhouette, a disc or its irregular outline; the rock sits on tile (0, 0).
        var variant = _random.Next(Math.Max(1, rockProto.Variants));
        rockProto.Shapes.TryGetValue(variant, out var shape);
        var tile = new Tile(_tileDefs[ent.Comp.Tile].TileId);
        var radius = rockProto.Radius;
        var bound = (int) MathF.Ceiling(radius);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -bound; x <= bound; x++)
        {
            for (var y = -bound; y <= bound; y++)
            {
                if (shape?.Contains(new Vector2(x, y)) ?? x * x + y * y <= radius * radius)
                    tiles.Add((new Vector2i(x, y), tile));
            }
        }

        _map.SetTiles(ent, grid, tiles);
        // Space has no friction, so damp the rock: a split, a ram or a tractor tug settles after a few metres.
        if (TryComp<PhysicsComponent>(ent, out var body))
        {
            _physics.SetLinearDamping(ent, body, 0.6f);
            _physics.SetAngularDamping(ent, body, 0.8f);
        }

        var rock = SpawnAttachedTo(ent.Comp.Rock, new EntityCoordinates(ent, 0.5f, 0.5f));
        ent.Comp.RockUid = rock;
        if (TryComp<WFAsteroidComponent>(rock, out var rockComp))
        {
            rockComp.Variant = variant;
            if (shape != null)
            {
                rockComp.MassScale = shape.MassScale;
                SetHull(rock, shape);
            }

            Dirty(rock, rockComp);
            SyncGridVisuals(rock, rockComp);
        }
    }

    /// <summary>The asteroid grid built for this rock, not a ship or another rock's grid that a boulder sits on.</summary>
    private EntityUid? OwnGrid(EntityUid rock, TransformComponent? xform = null)
    {
        return Resolve(rock, ref xform) && xform.GridUid is { } grid &&
               TryComp<WFAsteroidGridComponent>(grid, out var builder) && builder.RockUid == rock
            ? grid
            : null;
    }

    /// <summary>Copies a grid rock's look onto its grid, for clients the rock itself is too far away to reach.</summary>
    private void SyncGridVisuals(EntityUid rock, WFAsteroidComponent comp)
    {
        if (OwnGrid(rock) is not { } grid || !TryComp<WFAsteroidGridVisualsComponent>(grid, out var visuals))
            return;

        visuals.Rock = MetaData(rock).EntityPrototype?.ID;
        visuals.Variant = comp.Variant;
        visuals.Look = comp.Look;
        visuals.RockColor = comp.RockColor;
        visuals.VeinColor = comp.VeinColor;
        Dirty(grid, visuals);
    }

    /// <summary>Makes the grid's tiles as heavy as solid rock, after the shuttle system has set them to hull density.</summary>
    private void OnGridFixtures(Entity<WFAsteroidGridComponent> ent, ref GridFixtureChangeEvent args)
    {
        foreach (var (id, fixture) in args.NewFixtures)
        {
            _physics.SetDensity(ent, id, fixture, ShuttleSystem.TileDensityMultiplier * ent.Comp.DensityScale, false);
        }
    }

    /// <summary>Picks the rock's sprite; a grid then sets its own rock's to match its tiles.</summary>
    private void OnRockMapInit(Entity<WFAsteroidComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Variant = _random.Next(Math.Max(1, ent.Comp.Variants));
        Dirty(ent);
    }

    /// <summary>Swaps the rock's round fixture for its silhouette's hull, keeping its collision settings.</summary>
    private void SetHull(EntityUid rock, WFAsteroidShape shape)
    {
        const string id = "fix1";
        if (shape.Hull.Count < 3 || _fixtures.GetFixtureOrNull(rock, id) is not { } old)
            return;

        var hull = new PolygonShape();
        if (!hull.Set(shape.Hull))
            return;

        var (density, hard, layer, mask) = (old.Density, old.Hard, old.CollisionLayer, old.CollisionMask);
        // Without updates, so the body never drops to no fixtures and stops colliding before the hull replaces it.
        _fixtures.DestroyFixture(rock, id, old, updates: false);
        _fixtures.TryCreateFixture(rock, hull, id, density, hard, layer, mask);
    }

    /// <summary>Rolls a rock's mass, ores, resistance and cracking window from a composition.</summary>
    public void Roll(EntityUid rock, ProtoId<WFAsteroidCompositionPrototype> id, WFAsteroidComponent? comp = null)
    {
        if (!Resolve(rock, ref comp) || !_proto.TryIndex(id, out var proto))
            return;

        comp.CompositionId = id;
        comp.Mass = _random.NextFloat(comp.MinMass, comp.MaxMass) * comp.MassScale;
        var inert = _random.NextFloat(proto.MinInert, proto.MaxInert);
        var pool = proto.Ores.ToList();
        var count = Math.Clamp(_random.Next(proto.MinOres, proto.MaxOres + 1), 1, Math.Max(1, pool.Count));
        var picked = new List<(WFAsteroidCompositionEntry Entry, float Share)>();
        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var entry = PickWeighted(pool);
            pool.Remove(entry);
            // The first pick leads the rock, so a rare ore can still dominate one.
            picked.Add((entry, _random.NextFloat(0.3f, 1f) * (i == 0 ? 2f : 1f)));
        }

        var total = picked.Sum(p => p.Share);
        comp.Ores.Clear();
        foreach (var (entry, share) in picked)
        {
            comp.Ores.Add(new WFAsteroidOre(entry.Ore, (1f - inert) * share / total, entry.Color, entry.Volatility));
        }

        comp.Ores.Sort((a, b) => b.Fraction.CompareTo(a.Fraction));
        comp.BaseResistance = _random.NextFloat(proto.MinResistance, proto.MaxResistance);
        ApplyResistance(comp);
        comp.Instability = _random.NextFloat(proto.MinInstability, proto.MaxInstability);
        RollWindow(comp);
        ApplyKind(rock, comp, proto);
        Dirty(rock, comp);
        SyncGridVisuals(rock, comp);
    }

    /// <summary>Gives a rock its kind's tint and name.</summary>
    private void ApplyKind(EntityUid rock, WFAsteroidComponent comp, WFAsteroidCompositionPrototype proto)
    {
        comp.RockColor = proto.RockColor;
        comp.Look = proto.Look;
        if (proto.Name is { } name)
            _meta.SetEntityName(rock, Loc.GetString(name, ("name", MetaData(rock).EntityPrototype?.Name ?? Name(rock))));
    }

    private WFAsteroidCompositionEntry PickWeighted(List<WFAsteroidCompositionEntry> pool)
    {
        var roll = _random.NextFloat(pool.Sum(e => e.Weight));
        foreach (var entry in pool)
        {
            roll -= entry.Weight;
            if (roll <= 0f)
                return entry;
        }

        return pool[^1];
    }

    /// <summary>The composition's resistance for this size of rock.</summary>
    private static void ApplyResistance(WFAsteroidComponent comp)
    {
        comp.Resistance = Math.Clamp(comp.BaseResistance * comp.ResistanceScale + comp.ResistanceBonus, 0f, 0.9f);
    }

    /// <summary>Rolls the rock's cracking window and heat drift, and tints its veins by its leading ore.</summary>
    private void RollWindow(WFAsteroidComponent comp)
    {
        if (comp.FixedWindow is { } window)
        {
            comp.WindowCenter = (window.X + window.Y) / 2f;
            comp.WindowHalfWidth = (window.Y - window.X) / 2f;
        }
        else
        {
            comp.WindowCenter = _random.NextFloat(0.35f, 0.55f);
            comp.WindowHalfWidth = 0.09f * (1f - 0.4f * comp.Instability);
        }

        comp.NoiseSeed = _random.NextFloat(0f, 100f);
        comp.VeinColor = comp.Ores.Count > 0 ? comp.Ores[0].Color : Color.Gray;
    }

    /// <summary>Gives a fragment its parent's ores, stability and heat with a new mass and window.</summary>
    private void Inherit(WFAsteroidComponent parent, EntityUid child, float mass)
    {
        if (!TryComp<WFAsteroidComponent>(child, out var comp))
            return;

        comp.CompositionId = parent.CompositionId;
        comp.Mass = mass;
        comp.Ores = new List<WFAsteroidOre>(parent.Ores);
        comp.BaseResistance = parent.BaseResistance;
        ApplyResistance(comp);
        comp.Instability = parent.Instability;
        RollWindow(comp);
        if (_proto.TryIndex(parent.CompositionId, out var proto))
            ApplyKind(child, comp, proto);

        // Fragments come off hot, so the laser can carry straight on to the next one.
        comp.BaseCharge = parent.BaseCharge;
        comp.Charge = parent.Charge;
        if (comp.BaseCharge > 0f)
            _beams.Track(child);
        Dirty(child, comp);
        SyncGridVisuals(child, comp);
    }

    private void OnDamaged(Entity<WFAsteroidComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Damageable.TotalDamage >= ent.Comp.ShatterDamage)
            QueueCrack(ent, WFAsteroidCrack.Shatter, ent.Comp);
    }

    private void OnExamined(Entity<WFAsteroidComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (_proto.TryIndex(ent.Comp.CompositionId, out var proto) && proto.Examine is { } examine)
            args.PushMarkup(Loc.GetString(examine));
        args.PushMarkup(Loc.GetString("wf-asteroid-examine-scan"));
    }

    /// <summary>Seconds in its window a rock needs to crack under one beam of crack rate 1.</summary>
    public static float CrackTime(WFAsteroidComponent comp)
    {
        return comp.CrackTimeBase + comp.Mass * comp.CrackTimePerMass;
    }

    /// <summary>Breaks the rock on the next update.</summary>
    public void QueueCrack(EntityUid rock, WFAsteroidCrack kind, WFAsteroidComponent? comp = null)
    {
        if (!Resolve(rock, ref comp) || comp.Cracking)
            return;

        comp.Cracking = true;
        _cracks.Add((rock, kind));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        SpawnClusters();
        SpawnPendingRocks();

        if (_cracks.Count == 0)
            return;

        _cracking.Clear();
        _cracking.AddRange(_cracks);
        _cracks.Clear();
        foreach (var (rock, kind) in _cracking)
        {
            if (!TerminatingOrDeleted(rock) && TryComp<WFAsteroidComponent>(rock, out var comp))
                Crack(rock, comp, kind);
        }
    }

    private void Crack(EntityUid rock, WFAsteroidComponent comp, WFAsteroidCrack kind)
    {
        var xform = Transform(rock);
        if (xform.MapUid is not { } mapUid)
            return;

        var center = _transform.GetMapCoordinates(rock, xform);
        var rotation = _transform.GetWorldRotation(xform);
        var grid = OwnGrid(rock, xform);
        var velocity = _physics.GetMapLinearVelocity(grid ?? rock);
        var controller = grid is { } owner ? _debris.GetDebrisOwner(owner) : null;
        var ores = comp.Ores;
        var yield = kind switch
        {
            WFAsteroidCrack.Clean => WFAsteroidHeat.Yield(comp.Progress, comp.HotProgress),
            WFAsteroidCrack.Overcharge => comp.OverchargeYield,
            _ => comp.ShatterYield,
        };

        var fragmentMass = kind == WFAsteroidCrack.Shatter || comp.FragmentPrototype == null
            ? 0f
            : comp.Mass * yield * comp.FragmentShare;
        var oreMass = comp.Mass * yield - fragmentMass;
        var volatility = WFAsteroidScan.Volatility(ores);
        var radius = comp.Radius;
        var mass = comp.Mass;

        // Removed first so nothing spawned below lands on a grid that is going away; comp stays readable after.
        if (grid is { } asteroidGrid)
            ReleasePassengers(asteroidGrid, rock, mapUid);
        Del(grid ?? rock);
        _audio.PlayPvs(CrackSound, new EntityCoordinates(mapUid, center.Position));
        SpawnCrackEffect(center, mapUid, radius, comp.VeinColor, kind != WFAsteroidCrack.Clean);

        if (kind == WFAsteroidCrack.Overcharge)
        {
            _explosion.QueueExplosion(center, "Default", WFAsteroidScan.BlastIntensity(mass, volatility), 1.2f, 6f, null,
                maxTileBreak: 0, canCreateVacuum: false);
        }

        if (fragmentMass > 0f && comp.FragmentPrototype is { } fragment)
            oreMass += SpawnFragments(comp, fragment, fragmentMass, center, rotation, velocity, mapUid, controller);

        if (comp.BoulderPrototype is { } boulder)
            oreMass -= SpawnBoulders(comp, boulder, oreMass * comp.BoulderShare, center, mapUid);

        SpawnOre(ores, oreMass * OreYield(comp), center, radius);
    }

    /// <summary>Ore each vein of the rock drops: the rock's yield times its kind's.</summary>
    private float OreYield(WFAsteroidComponent comp)
    {
        return comp.OreYield * (_proto.TryIndex(comp.CompositionId, out var proto) ? proto.OreYield : 1f);
    }

    /// <summary>Drops part of the ore as boulders for hand miners and returns the mass they took.</summary>
    private float SpawnBoulders(WFAsteroidComponent parent, EntProtoId boulder, float mass, MapCoordinates center,
        EntityUid mapUid)
    {
        if (!_proto.TryIndex(boulder, out var proto) || !proto.TryComp<WFAsteroidComponent>(out var boulderProto, _factory))
            return 0f;

        var typical = (boulderProto.MinMass + boulderProto.MaxMass) / 2f;
        var count = Math.Min(parent.MaxBoulders, (int) MathF.Floor(mass / MathF.Max(typical, 0.1f)));
        if (count <= 0)
            return 0f;

        for (var i = 0; i < count; i++)
        {
            // Prefer open space over a fragment's grid, so the boulder isn't lost when that fragment cracks.
            var position = center.Position;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                position = center.Position + _random.NextVector2(parent.Radius * 1.2f);
                if (!Blocked(center.MapId, position, 0.6f))
                    break;
            }

            var spawned = Spawn(boulder, new EntityCoordinates(mapUid, position));
            _transform.SetWorldRotation(spawned, _random.NextAngle());
            Inherit(parent, spawned, typical);
        }

        return count * typical;
    }

    /// <summary>Moves everything on an asteroid grid but its rock onto the map, so deleting the grid takes nothing else.</summary>
    private void ReleasePassengers(EntityUid grid, EntityUid rock, EntityUid mapUid)
    {
        var passengers = new List<EntityUid>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (child != rock)
                passengers.Add(child);
        }

        foreach (var passenger in passengers)
        {
            var xform = Transform(passenger);
            if (xform.Anchored)
                _transform.Unanchor(passenger, xform);
            _transform.SetParent(passenger, xform, mapUid);
        }
    }

    /// <summary>
    /// A pickaxe, drill or kinetic accelerator breaks a boulder into all of its ore. A ship drill takes the ore to
    /// itself, as it does from vein rock.
    /// </summary>
    private void OnGathered(EntityUid uid, WFAsteroidComponent comp, GatheredEvent args)
    {
        if (comp.Cracking)
            return;

        comp.Cracking = true;
        var center = _transform.GetMapCoordinates(uid);
        var mass = comp.Mass * OreYield(comp);
        if (args.TeleportLootToGatherer && args.Gatherer is { } gatherer)
            SpawnOre(comp.Ores, mass, _transform.GetMapCoordinates(gatherer), 0.2f);
        else
            SpawnOre(comp.Ores, mass, center, MathF.Max(comp.Radius, 0.3f));
        if (Transform(uid).MapUid is { } mapUid)
            SpawnCrackEffect(center, mapUid, MathF.Max(comp.Radius, 0.5f), comp.VeinColor, false);
    }

    /// <summary>Spawns the flash, shockwave, chips and dust of a breaking rock.</summary>
    private void SpawnCrackEffect(MapCoordinates center, EntityUid mapUid, float radius, Color veins, bool violent)
    {
        var effect = Spawn(CrackEffect, new EntityCoordinates(mapUid, center.Position));
        var comp = Comp<WFAsteroidCrackEffectComponent>(effect);
        comp.Radius = radius;
        comp.VeinColor = veins;
        comp.Violent = violent;
        comp.Start = _timing.CurTime;
        Dirty(effect, comp);
    }

    /// <summary>Spawns fragments around the old centre and returns the mass of any that had no room.</summary>
    private float SpawnFragments(WFAsteroidComponent parent, EntProtoId fragment, float mass, MapCoordinates center,
        Angle rotation, Vector2 velocity, EntityUid mapUid, EntityUid? controller)
    {
        if (!TryGetRock(fragment, out var child))
            return mass;

        var count = Math.Max(1, parent.FragmentCount);
        var each = mass / count;
        // Far enough apart that neighbouring fragments' tile discs, which reach past the radius by a tile corner, don't touch.
        var distance = count == 1 ? 0f : (child.Radius + 0.8f) / MathF.Sin(MathF.PI / count) * 1.05f;
        var start = _random.NextFloat(0f, MathF.Tau);
        var spots = new List<(Vector2 Position, Vector2 Direction)>();
        var lost = 0f;
        // Check every spot before spawning any, so a fragment never blocks its sibling.
        for (var i = 0; i < count; i++)
        {
            var angle = start + MathF.Tau * i / count;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var position = center.Position + direction * distance;
            if (Blocked(center.MapId, position, child.Radius + 0.5f))
                lost += each;
            else
                spots.Add((position, direction));
        }

        foreach (var (position, direction) in spots)
        {
            var spawned = Spawn(fragment, new EntityCoordinates(mapUid, position));
            _transform.SetWorldRotation(spawned, rotation + _random.NextFloat(-0.5f, 0.5f));
            // Thrown apart, then the grid's damping settles them a few metres out.
            _physics.SetLinearVelocity(spawned, velocity + direction * _random.NextFloat(1.5f, 2.5f));
            _physics.SetAngularVelocity(spawned, _random.NextFloat(-0.4f, 0.4f));
            if (TryComp<WFAsteroidGridComponent>(spawned, out var spawnedGrid) && spawnedGrid.RockUid is { } rock)
                Inherit(parent, rock, each);
            if (controller is { } owner)
                _debris.AdoptDebris(owner, spawned);
        }

        return lost;
    }

    /// <summary>The rock an asteroid grid prototype spawns.</summary>
    private bool TryGetRock(EntProtoId gridProto, [NotNullWhen(true)] out WFAsteroidComponent? rock)
    {
        rock = null;
        return _proto.TryIndex(gridProto, out var grid) &&
               grid.TryComp<WFAsteroidGridComponent>(out var builder, _factory) &&
               _proto.TryIndex(builder.Rock, out var rockProto) &&
               rockProto.TryComp(out rock, _factory);
    }

    private bool Blocked(MapId map, Vector2 position, float radius)
    {
        _grids.Clear();
        _map.FindGridsIntersecting(map, Box2.CenteredAround(position, new Vector2(radius * 2f)), ref _grids);
        return _grids.Count > 0;
    }

    /// <summary>Scatters each ore in piles evenly over where the rock was. Each unit of mass is one vein.</summary>
    private void SpawnOre(List<WFAsteroidOre> ores, float mass, MapCoordinates center, float radius)
    {
        foreach (var ore in ores)
        {
            if (!_proto.TryIndex(ore.Ore, out var proto) || proto.OreEntity is not { } entity)
                continue;

            var veins = mass * ore.Fraction;
            var average = (proto.MinOreYield + proto.MaxOreYield) / 2f;
            var total = (int) MathF.Round(veins * average * _random.NextFloat(0.9f, 1.1f));
            if (total <= 0)
                continue;

            var piles = Math.Clamp((int) MathF.Ceiling(veins / 2f), 1, Math.Min(12, total));
            for (var i = 0; i < piles; i++)
            {
                var amount = total / piles + (i < total % piles ? 1 : 0);
                // Uniform over the rock's disc, not bunched at its centre.
                var offset = _random.NextAngle().ToVec() * radius * 0.85f * MathF.Sqrt(_random.NextFloat());
                var coords = _transform.ToCoordinates(new MapCoordinates(center.Position + offset, center.MapId));
                _spawnCount.SpawnCount(entity, coords, amount);
            }
        }
    }

    /// <summary>Fills each new cluster marker's budget with asteroids once worldgen has handed it to a chunk.</summary>
    private void SpawnClusters()
    {
        if (_clusters.Count == 0)
            return;

        _filling.Clear();
        _filling.AddRange(_clusters);
        _clusters.Clear();
        foreach (var uid in _filling)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<WFAsteroidClusterComponent>(uid, out var cluster))
                continue;

            // Free the marker's chunk slot before its first rock takes the same key, so deleting it doesn't drop that
            // rock from the chunk.
            var controller = _debris.GetDebrisOwner(uid);
            _debris.ReleaseDebris(uid);
            FillCluster(uid, cluster, controller);
            RemComp<WFAsteroidClusterComponent>(uid);
            QueueDel(uid);
        }
    }

    /// <summary>Plans rocks by the cluster's size weights around its centre until the budget runs out.</summary>
    private void FillCluster(EntityUid marker, WFAsteroidClusterComponent cluster, EntityUid? controller)
    {
        var xform = Transform(marker);
        if (xform.MapUid is not { } mapUid)
            return;

        var center = _transform.GetMapCoordinates(xform);
        var budget = _budgets.Remove(center, out var swapped) ? swapped : cluster.Mass;
        var sizes = new List<ClusterSize>();
        foreach (var (grid, weight) in cluster.Sizes)
        {
            if (TryGetRock(grid, out var rock))
                sizes.Add(new ClusterSize(grid, weight, (rock.MinMass + rock.MaxMass) / 2f, rock.Radius));
        }

        if (sizes.Count == 0)
            return;

        sizes.Sort((a, b) => a.Mass.CompareTo(b.Mass));
        var smallest = sizes[0].Mass;
        var spread = 10f + 2.6f * MathF.Sqrt(budget);
        var rareChance = Math.Clamp(cluster.RareChance + cluster.RareChancePerKm * center.Position.Length() / 1000f, 0f, 1f);
        var remaining = budget;
        var planned = new List<(Vector2 Position, float Radius)>();
        for (var attempt = 0; attempt < 60 && planned.Count < cluster.MaxRocks && remaining >= smallest * 0.5f; attempt++)
        {
            var size = PickSize(sizes, remaining);
            var position = attempt == 0
                ? center.Position
                : center.Position + _random.NextAngle().ToVec() * spread * MathF.Sqrt(_random.NextFloat());
            if (Blocked(center.MapId, position, size.Radius + 1.5f) ||
                planned.Any(p => (p.Position - position).Length() < p.Radius + size.Radius + 1.5f))
                continue;

            var composition = cluster.RareComposition is { } rare && _random.Prob(rareChance)
                ? rare
                : cluster.Composition;
            // The last rock takes what is left rather than overshooting the budget.
            var mass = MathF.Max(MathF.Min(size.Mass, remaining), smallest * 0.5f);
            remaining -= mass;
            planned.Add((position, size.Radius));
            _pendingRocks.Enqueue(new PendingRock(size.Grid, mapUid, position, composition, mass, controller));
        }
    }

    /// <summary>Spawns up to <see cref="RocksPerTick"/> planned rocks.</summary>
    private void SpawnPendingRocks()
    {
        for (var i = 0; i < RocksPerTick && _pendingRocks.TryDequeue(out var pending); i++)
        {
            // The chunk unloaded before its turn came; nobody is near, so skip it.
            if (TerminatingOrDeleted(pending.Map) ||
                pending.Controller is { } controller && TerminatingOrDeleted(controller))
            {
                i--;
                continue;
            }

            var spawned = Spawn(pending.Grid, new EntityCoordinates(pending.Map, pending.Position));
            _transform.SetWorldRotation(spawned, _random.NextAngle());
            if (TryComp<WFAsteroidGridComponent>(spawned, out var spawnedGrid) && spawnedGrid.RockUid is { } rock &&
                TryComp<WFAsteroidComponent>(rock, out var rockComp))
            {
                Roll(rock, pending.Composition, rockComp);
                rockComp.Mass = pending.Mass;
            }

            if (pending.Controller is { } owner)
                _debris.AdoptDebris(owner, spawned);
        }
    }

    private readonly record struct PendingRock(EntProtoId Grid, EntityUid Map, Vector2 Position,
        ProtoId<WFAsteroidCompositionPrototype> Composition, float Mass, EntityUid? Controller);

    private readonly record struct ClusterSize(EntProtoId Grid, float Weight, float Mass, float Radius);

    /// <summary>Picks a size by weight among those that fit the budget left, or the smallest if none does.</summary>
    private ClusterSize PickSize(List<ClusterSize> sizes, float remaining)
    {
        // Sizes are sorted by mass, so the ones that fit are a prefix of the list.
        var limit = remaining * 1.2f;
        var fitting = 0;
        var total = 0f;
        while (fitting < sizes.Count && sizes[fitting].Mass <= limit)
        {
            total += sizes[fitting++].Weight;
        }

        if (fitting == 0)
            return sizes[0];

        var roll = _random.NextFloat(total);
        for (var i = 0; i < fitting; i++)
        {
            roll -= sizes[i].Weight;
            if (roll <= 0f)
                return sizes[i];
        }

        return sizes[fitting - 1];
    }
}
