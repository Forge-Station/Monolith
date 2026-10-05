using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.Caverns;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

public sealed partial class WFCavernMouthSystem
{
    /// <summary>Random candidates each cell tries after the gate candidate, if it has one.</summary>
    public const int CandidatesPerCell = 8;

    /// <summary>A candidate's pad keeps this many tiles from its cell's edges, so it never crosses into the next cell.</summary>
    public const int CellMargin = 4;

    /// <summary>How far past the hole's extent, along each axis from its anchor, the cavern check samples the tunnel.</summary>
    public const int OpennessMargin = 2;

    /// <summary>How many of the four samples must be open for the pad to sit in the tunnel web.</summary>
    public const int OpennessNeeded = 3;

    /// <summary>No grid but the map may lie this close to a footprint when it is stamped.</summary>
    public const float GridClearance = 4f;

    /// <summary>Tiles around a claim source, in either axis, whose cells are claimed.</summary>
    public const int ClaimReach = 96;

    /// <summary>How often the claim sources are gathered and the cells due listed.</summary>
    public static readonly TimeSpan ClaimInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Milliseconds a tick the claims may take; one candidate or claim always runs.</summary>
    public const double ClaimBudgetMs = 2;

    /// <summary>One candidate taking longer than this, in milliseconds, is logged.</summary>
    private const double SlowClaimMs = 20;

    /// <summary>Biome chunk edge in tiles; SharedBiomeSystem.ChunkSize is protected.</summary>
    private const int BiomeChunk = 8;

    private static readonly Vector2i[] Cardinals = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    private static readonly Entity<MapGridComponent>? NoGrid = null;

    /// <summary>What the lazy claims have cost since it was last reset; the tests read it.</summary>
    public readonly WFCavernClaimStats ClaimStats = new();

    private bool _claimsEnabled;
    private TimeSpan _nextClaim;

    /// <summary>Cells due a claim, nearest a source first, and how far the current tick got through them.</summary>
    private readonly List<(EntityUid Ground, Vector2i Cell, float Distance)> _due = new();
    private int _dueNext;

    /// <summary>Where each ground's claim sources stand, and the boxes their chunk loaders reach.</summary>
    private readonly Dictionary<EntityUid, List<Vector2>> _sources = new();
    private readonly Dictionary<EntityUid, List<Box2>> _loading = new();
    private readonly HashSet<EntityUid> _counted = new();
    private readonly Dictionary<EntityUid, MouthContext> _contexts = new();

    /// <summary>One candidate before its shape is grown: the shape's seed and where in the cell it goes, as fractions.</summary>
    private readonly record struct Candidate(int Seed, double X, double Y, Vector2i? Fixed);

    /// <summary>What stops a site being stamped: nothing, something that may clear, or a pin, which never does.</summary>
    private enum Block : byte
    {
        None,
        Waiting,
        Pinned,
    }

    /// <summary>A cell's site, found and cached without stamping it; null when no candidate passes.</summary>
    public WFCavernSite? EvaluateCell(Entity<WFCavernGroundComponent> ground, Vector2i cell)
    {
        if (!TryGetContext(ground, out var context))
            return null;

        var state = CellState(ground, cell);
        if (!state.Evaluated)
            Evaluate(ground, context, cell, state, null);

        return state.Site;
    }

    /// <summary>Gathers the claim sources every <see cref="ClaimInterval"/>, then claims the cells due within the tick's budget.</summary>
    private void UpdateClaims()
    {
        if (!_claimsEnabled)
            return;

        if (_timing.CurTime >= _nextClaim)
        {
            _nextClaim = _timing.CurTime + ClaimInterval;
            CollectSources();
            ListDue();
        }

        if (_dueNext >= _due.Count)
            return;

        var watch = Stopwatch.StartNew();
        _contexts.Clear();

        do
        {
            var (groundUid, cell, _) = _due[_dueNext];

            if (!TryComp<WFCavernGroundComponent>(groundUid, out var comp) || !CachedContext((groundUid, comp), out var context))
            {
                _dueNext++;
                continue;
            }

            var ground = (groundUid, comp);
            var state = CellState(ground, cell);

            if (state.State is WFCavernClaim.Claimed or WFCavernClaim.Empty)
            {
                _dueNext++;
                continue;
            }

            if (!state.Evaluated)
            {
                Evaluate(ground, context, cell, state, watch);

                // Out of time mid-cell the cursor keeps its place; a site found late is stamped next tick.
                if (!state.Evaluated || watch.Elapsed.TotalMilliseconds >= ClaimBudgetMs)
                    break;
            }

            var stamping = Stopwatch.GetTimestamp();
            if (ClaimCell(ground, context, cell, WFCavernMouthKind.Cell, _loading.GetValueOrDefault(groundUid)) == WFCavernClaim.Claimed)
            {
                ClaimStats.Stamped++;
                ClaimStats.StampMs += Stopwatch.GetElapsedTime(stamping).TotalMilliseconds;
            }

            _dueNext++;
        }
        while (_dueNext < _due.Count && watch.Elapsed.TotalMilliseconds < ClaimBudgetMs);

        var spent = watch.Elapsed.TotalMilliseconds;
        ClaimStats.BusyTicks++;
        ClaimStats.TotalMs += spent;
        ClaimStats.MaxTickMs = Math.Max(ClaimStats.MaxTickMs, spent);
    }

    /// <summary>
    /// Every entity that loads terrain, as the biome loader counts them: each session's attached entity and every
    /// view subscription (z-level eyes included) that may load terrain, by the ground whose cells it claims.
    /// </summary>
    // A cavern's viewers claim for the ground above it. Ghosts that load nothing claim nothing.
    private void CollectSources()
    {
        _sources.Clear();
        _counted.Clear();

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } attached)
                AddSource(attached);

            foreach (var viewer in session.ViewSubscriptions)
            {
                AddSource(viewer);
            }
        }
    }

    private void AddSource(EntityUid uid)
    {
        if (!_counted.Add(uid) || TerminatingOrDeleted(uid))
            return;

        var xform = Transform(uid);
        if (xform.MapUid is not { } map)
            return;

        EntityUid ground;
        if (HasComp<WFCavernGroundComponent>(map))
            ground = map;
        else if (TryComp<WFCavernLayerComponent>(map, out var layer) && HasComp<WFCavernGroundComponent>(layer.Ground))
            ground = layer.Ground;
        else
            return;

        if (!_biome.WfCanLoad(uid))
            return;

        if (!_sources.TryGetValue(ground, out var list))
            _sources[ground] = list = new List<Vector2>();

        list.Add(_transform.GetWorldPosition(xform));
    }

    /// <summary>Lists every unclaimed or deferred cell whose square meets a source's reach, nearest a source first.</summary>
    private void ListDue()
    {
        _due.Clear();
        _dueNext = 0;
        _loading.Clear();

        // The loader takes every chunk its box touches, so a chunk past the box's edge still loads.
        var loadReach = _biome.WfLoadRange + BiomeChunk;

        foreach (var (groundUid, positions) in _sources)
        {
            if (positions.Count == 0
                || !TryComp<WFCavernGroundComponent>(groundUid, out var comp)
                || !_proto.TryIndex(comp.Prototype, out var cavern))
                continue;

            var spec = cavern.Mouths;
            var boxes = new List<Box2>(positions.Count);
            var nearest = new Dictionary<Vector2i, float>();

            foreach (var position in positions)
            {
                boxes.Add(Box2.CenteredAround(position, new Vector2(loadReach * 2)));

                var min = CellOf(spec, Floor(position - new Vector2(ClaimReach)));
                var max = CellOf(spec, Floor(position + new Vector2(ClaimReach)));

                for (var x = min.X; x <= max.X; x++)
                for (var y = min.Y; y <= max.Y; y++)
                {
                    var cell = new Vector2i(x, y);
                    if (comp.Cells.TryGetValue(cell, out var state) && state.State is WFCavernClaim.Claimed or WFCavernClaim.Empty)
                        continue;

                    var centre = (new Vector2(x, y) + new Vector2(0.5f)) * spec.CellSize;
                    var distance = Vector2.DistanceSquared(centre, position);
                    if (!nearest.TryGetValue(cell, out var best) || distance < best)
                        nearest[cell] = distance;
                }
            }

            _loading[groundUid] = boxes;

            foreach (var (cell, distance) in nearest)
            {
                _due.Add((groundUid, cell, distance));
            }
        }

        _due.Sort((a, b) => a.Distance.CompareTo(b.Distance));
    }

    /// <summary>The claim context for a ground, resolved once a tick.</summary>
    private bool CachedContext(Entity<WFCavernGroundComponent> ground, out MouthContext context)
    {
        if (_contexts.TryGetValue(ground, out context))
            return true;

        if (!TryGetContext(ground, out context))
            return false;

        _contexts[ground] = context;
        return true;
    }

    /// <summary>A cell's claim state, added as Unclaimed the first time it is asked for.</summary>
    private static WFCavernCell CellState(Entity<WFCavernGroundComponent> ground, Vector2i cell)
    {
        if (!ground.Comp.Cells.TryGetValue(cell, out var state))
        {
            state = new WFCavernCell();
            ground.Comp.Cells[cell] = state;
        }

        return state;
    }

    /// <summary>Claims a cell: its site is found once and cached, then stamped as soon as nothing blocks it.</summary>
    /// <param name="loading">Boxes where chunks are about to load; a site touching one waits.</param>
    private WFCavernClaim ClaimCell(
        Entity<WFCavernGroundComponent> ground,
        MouthContext context,
        Vector2i cell,
        WFCavernMouthKind kind,
        IReadOnlyList<Box2>? loading = null)
    {
        var state = CellState(ground, cell);

        if (state.State is WFCavernClaim.Claimed or WFCavernClaim.Empty)
            return state.State;

        if (!state.Evaluated)
            Evaluate(ground, context, cell, state, null);

        if (state.Site is not { } site)
        {
            state.State = WFCavernClaim.Empty;
            return state.State;
        }

        switch (Blocked(context, site, loading))
        {
            case Block.Pinned:
                state.State = WFCavernClaim.Empty;
                return state.State;
            case Block.Waiting:
                state.State = WFCavernClaim.Deferred;
                return state.State;
        }

        Stamp(ground, context, site, kind);
        state.State = WFCavernClaim.Claimed;
        return state.State;
    }

    /// <summary>
    /// Tries a cell's candidates from its cursor until one passes, all have failed, or the budget runs out. Reads only
    /// noise, so the site never depends on when the cell is looked at.
    /// </summary>
    /// <param name="budget">This tick's clock; null evaluates the whole cell at once.</param>
    private void Evaluate(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i cell, WFCavernCell state, Stopwatch? budget)
    {
        var candidates = Candidates(ground, context, cell);

        while (state.Cursor < candidates.Count)
        {
            var started = Stopwatch.GetTimestamp();
            var candidate = candidates[state.Cursor++];
            ClaimStats.Candidates++;

            var found = TrySite(context, cell, candidate, out var site);

            var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (took > SlowClaimMs)
                Log.Warning($"A cavern mouth candidate in cell {cell} on {ToPrettyString(ground)} took {took:F1} ms.");

            if (found)
            {
                state.Site = site;
                state.Evaluated = true;
                return;
            }

            if (budget != null && budget.Elapsed.TotalMilliseconds >= ClaimBudgetMs)
                return;
        }

        state.Evaluated = true;
    }

    /// <summary>The gate's first candidate: the planet centre plus the gate offset, as a tile.</summary>
    private static Vector2i GateCandidate(Entity<WFCavernGroundComponent> ground, WFCavernMouthSpec spec)
    {
        var centre = new Vector2i((int) MathF.Floor(ground.Comp.Centre.X), (int) MathF.Floor(ground.Comp.Centre.Y));
        return centre + spec.GateOffset;
    }

    /// <summary>A cell's candidates in order: the gate candidate if it lies in the cell, then eight seeded ones.</summary>
    private List<Candidate> Candidates(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i cell)
    {
        var spec = context.Spec;
        var seed = context.Ground.Comp1.Seed;
        var candidates = new List<Candidate>(CandidatesPerCell + 1);
        var gate = GateCandidate(ground, spec);

        if (CellOf(spec, gate) == cell)
            candidates.Add(new Candidate(WFCavernMouthShape.SeedAt(seed, gate), 0, 0, gate));

        // Plain arithmetic, never HashCode.Combine: that is randomised per process.
        var random = new System.Random(unchecked(seed * 7919 + cell.X * 73856093 + cell.Y * 19349663));

        for (var i = 0; i < CandidatesPerCell; i++)
        {
            candidates.Add(new Candidate(random.Next(), random.NextDouble(), random.NextDouble(), null));
        }

        return candidates;
    }

    /// <summary>Grows one candidate's shape and places it; true when it fits the cell and passes both pure checks.</summary>
    private bool TrySite(MouthContext context, Vector2i cell, Candidate candidate, out WFCavernSite site)
    {
        var shape = WFCavernMouthShape.Generate(context.Spec, candidate.Seed);
        site = default;

        if (!TryPlace(context.Spec, cell, shape, candidate, out var origin))
            return false;

        site = new WFCavernSite(origin, shape);
        return CavernAllows(context, site) && GroundAllows(context, site);
    }

    /// <summary>Anchors a candidate: the gate candidate where it is, the others spread by their fractions over the spots whose whole pad stays <see cref="CellMargin"/> inside the cell.</summary>
    private static bool TryPlace(WFCavernMouthSpec spec, Vector2i cell, WFCavernMouthShape shape, Candidate candidate, out Vector2i origin)
    {
        if (candidate.Fixed is { } fixedAt)
        {
            origin = fixedAt;
            return true;
        }

        var reach = spec.PadRadius + CellMargin;
        var cellMin = cell * spec.CellSize;
        var low = cellMin - shape.Min + new Vector2i(reach, reach);
        var high = cellMin + new Vector2i(spec.CellSize - 1, spec.CellSize - 1) - shape.Max - new Vector2i(reach, reach);

        origin = low;
        if (high.X < low.X || high.Y < low.Y)
            return false;

        origin = new Vector2i(
            low.X + Math.Min((int) (candidate.X * (high.X - low.X + 1)), high.X - low.X),
            low.Y + Math.Min((int) (candidate.Y * (high.Y - low.Y + 1)), high.Y - low.Y));
        return true;
    }

    /// <summary>Every footprint tile's natural tile is in the allowlist and its natural entity is not one to avoid.</summary>
    private bool GroundAllows(MouthContext context, WFCavernSite site)
    {
        var biome = context.Ground.Comp1;

        foreach (var offset in site.Shape.Hole.Concat(site.Shape.Ring))
        {
            var index = site.Origin + offset;

            if (!_biome.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile)
                || !context.GroundTiles.Contains(_tileDefs[tile.Value.TypeId].ID))
                return false;

            if (_biome.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out var entity)
                && context.Avoid.Contains(entity))
                return false;
        }

        return true;
    }

    /// <summary>The cavern is open under the anchor and at three of the four points just past the hole's extent along each axis.</summary>
    private bool CavernAllows(MouthContext context, WFCavernSite site)
    {
        var shape = site.Shape;

        if (!IsCavernOpen(context, site.Origin))
            return false;

        var reaches = new[]
        {
            shape.Max.Y + OpennessMargin,
            shape.Max.X + OpennessMargin,
            -shape.Min.Y + OpennessMargin,
            -shape.Min.X + OpennessMargin,
        };

        var open = 0;
        for (var i = 0; i < Cardinals.Length; i++)
        {
            if (IsCavernOpen(context, site.Origin + Cardinals[i] * reaches[i]))
                open++;
        }

        return open >= OpennessNeeded;
    }

    /// <summary>Whether the natural cavern has floor and no entity at a tile, the landing entity aside: a pool is open floor.</summary>
    private bool IsCavernOpen(MouthContext context, Vector2i index)
    {
        var biome = context.Level.Comp1;

        return _biome.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile)
               && (!_biome.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out var entity)
                   || entity == context.Spec.LandingEntity?.Id);
    }

    /// <summary>
    /// Whether a stamp would cut into something. A pinned footprint or pad tile (another mouth, or ground something
    /// else changed for good) blocks it for ever; loaded terrain, terrain about to load, something anchored or a grid
    /// nearby only for now.
    /// </summary>
    // Chunks about to load matter because nothing checks for mobs: a player who just arrived would stand in the hole.
    private Block Blocked(MouthContext context, WFCavernSite site, IReadOnlyList<Box2>? loading)
    {
        var groundBiome = (context.Ground.Owner, context.Ground.Comp1);
        var levelBiome = (context.Level.Owner, context.Level.Comp1);
        var pad = site.Shape.Pad(context.Spec.PadRadius);

        if (site.Shape.Hole.Concat(site.Shape.Ring).Any(offset => _biome.WfIsPinned(groundBiome, site.Origin + offset))
            || pad.Any(offset => _biome.WfIsPinned(levelBiome, site.Origin + offset)))
            return Block.Pinned;

        if (loading != null)
        {
            var radius = context.Spec.PadRadius;
            var padBox = new Box2(site.Origin + site.Shape.Min - new Vector2i(radius, radius),
                site.Origin + site.Shape.Max + new Vector2i(radius + 1, radius + 1));

            foreach (var box in loading)
            {
                if (box.Intersects(padBox))
                    return Block.Waiting;
            }
        }

        foreach (var offset in site.Shape.Hole.Concat(site.Shape.Ring))
        {
            var index = site.Origin + offset;

            if (_biome.WfIsChunkLoaded(groundBiome, index)
                || _map.GetAnchoredEntitiesEnumerator(context.Ground.Owner, context.Ground.Comp2, index).MoveNext(out _))
                return Block.Waiting;
        }

        if (pad.Any(offset => _biome.WfIsChunkLoaded(levelBiome, site.Origin + offset)))
            return Block.Waiting;

        var footprint = new Box2(site.Origin + site.Shape.Min - Vector2i.One, site.Origin + site.Shape.Max + new Vector2i(2, 2));
        var mapId = Comp<MapComponent>(context.Ground).MapId;

        // Ground that unloaded can still hold whoever was left on it, asleep or dead, and it is solid under them.
        _mobs.Clear();
        _lookup.GetEntitiesIntersecting(mapId, footprint, _mobs);
        if (_mobs.Count > 0)
            return Block.Waiting;

        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(mapId, footprint.Enlarged(GridClearance), ref grids,
            approx: true, includeMap: false);

        return grids.Count == 0 ? Block.None : Block.Waiting;
    }

    /// <summary>Cuts the mouth: ring and hole on the ground, the pad in the cavern, then shades, rim and climb point.</summary>
    // One SetTiles per map. Pinned tiles skip tile, entity and decal generation, so this holds on unloaded chunks too.
    private WFCavernMouth Stamp(Entity<WFCavernGroundComponent> ground, MouthContext context, WFCavernSite site, WFCavernMouthKind kind)
    {
        var spec = context.Spec;
        var mouth = new WFCavernMouth(site.Origin, site.Shape, kind);
        var groundGrid = (context.Ground.Owner, context.Ground.Comp2);
        var levelGrid = (context.Level.Owner, context.Level.Comp2);
        var groundBiome = context.Ground.Comp1;
        var levelBiome = context.Level.Comp1;
        var pad = mouth.Pad(spec.PadRadius);

        ClearBiomeEntities(context.Ground, mouth.Footprint);
        ClearBiomeEntities(context.Level, pad);

        var groundTiles = new List<(Vector2i, Tile)>();
        var pinned = new List<Vector2i>();

        foreach (var index in mouth.Ring)
        {
            pinned.Add(index);

            if (_map.TryGetTileRef(context.Ground.Owner, context.Ground.Comp2, index, out var existing) && !existing.Tile.IsEmpty)
                continue;

            if (_biome.TryGetTile(index, groundBiome.Layers, groundBiome.Seed, NoGrid, out var natural))
                groundTiles.Add((index, natural.Value));
        }

        foreach (var index in mouth.Hole)
        {
            pinned.Add(index);

            if (_map.TryGetTileRef(context.Ground.Owner, context.Ground.Comp2, index, out var existing) && !existing.Tile.IsEmpty)
                groundTiles.Add((index, Tile.Empty));
        }

        if (groundTiles.Count > 0)
            _map.SetTiles(context.Ground.Owner, context.Ground.Comp2, groundTiles);
        _biome.WfPinTiles((context.Ground.Owner, groundBiome), pinned);

        var landing = new Tile(_tileDefs[spec.LandingTile].TileId);
        var padTiles = new List<(Vector2i, Tile)>();

        foreach (var index in pad)
        {
            if (mouth.Contains(index) || !_biome.TryGetTile(index, levelBiome.Layers, levelBiome.Seed, NoGrid, out var natural))
                padTiles.Add((index, landing));
            else
                padTiles.Add((index, natural.Value));
        }

        _map.SetTiles(context.Level.Owner, context.Level.Comp2, padTiles);
        _biome.WfPinTiles((context.Level.Owner, levelBiome), pad.ToList());
        LayLandingEntities(context, padTiles, landing);

        var landingMultiplier = ((ContentTileDefinition) _tileDefs[spec.LandingTile]).FallDamageMultiplier;
        foreach (var index in mouth.Hole)
        {
            SpawnShade(ground, context, index, landingMultiplier);

            // A hole cut where the ground wasn't loaded changes no tile, so nothing told what was left lying there.
            WakeBodiesOn(context.Ground, index);
        }

        if (spec.Rim.Count > 0)
        {
            for (var i = 0; i < site.Shape.Rim.Count; i++)
            {
                SpawnAnchored(spec.Rim[i % spec.Rim.Count], groundGrid, site.Origin + site.Shape.Rim[i]);
            }
        }

        SpawnClimb(ground, context, levelGrid, mouth.ClimbTile);

        ground.Comp.Mouths.Add(mouth);
        return mouth;
    }

    /// <summary>
    /// Lays the landing's own entity, such as a pool's water, on cavern tiles just set: on every landing tile and
    /// wherever the cavern would grow it, since pinned tiles grow no biome entities.
    /// </summary>
    private void LayLandingEntities(MouthContext context, IEnumerable<(Vector2i Index, Tile Tile)> tiles, Tile landing)
    {
        if (context.Spec.LandingEntity is not { } landingEntity)
            return;

        var levelGrid = (context.Level.Owner, context.Level.Comp2);
        var levelBiome = context.Level.Comp1;

        foreach (var (index, tile) in tiles)
        {
            if (tile.TypeId != landing.TypeId
                && (!_biome.TryGetEntity(index, levelBiome.Layers, tile, levelBiome.Seed, NoGrid, out var natural)
                    || natural != landingEntity.Id))
                continue;

            if (!HasAnchored(levelGrid, index, landingEntity))
                SpawnAnchored(landingEntity, levelGrid, index);
        }
    }

    /// <summary>Spawns the shade over a hole tile unless it has one: the cavern, its air and the landing it reports.</summary>
    private void SpawnShade(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i index, float landingMultiplier)
    {
        if (ground.Comp.Shades.ContainsKey(index))
            return;

        var shade = Spawn(context.Spec.Shade, _map.GridTileToLocal(ground.Owner, context.Ground.Comp2, index));
        var shaft = EnsureComp<WFCavernShaftComponent>(shade);
        shaft.Cavern = context.Cavern.ID;
        shaft.Air = WFCavernAirClassifier.Classify(_proto.Index(context.Cavern.Level).Atmosphere);
        shaft.LandingMultiplier = landingMultiplier;
        Dirty(shade, shaft);
        ground.Comp.Shades[index] = shade;
    }

    /// <summary>Anchors a climb point in the cavern under a ground tile unless one is there, its delay scaled by the surface's gravity.</summary>
    private void SpawnClimb(Entity<WFCavernGroundComponent> ground, MouthContext context, Entity<MapGridComponent> levelGrid, Vector2i index)
    {
        if (ground.Comp.ClimbPoints.ContainsKey(index))
            return;

        var climb = SpawnAnchored(context.Spec.ClimbPoint, levelGrid, index);
        var climbComp = EnsureComp<WFCavernClimbComponent>(climb);
        climbComp.Delay = context.Spec.ClimbSeconds * Math.Clamp(context.Surface.Gravity, 1f, 2.5f);
        Dirty(climb, climbComp);
        ground.Comp.ClimbPoints[index] = climb;
    }

    /// <summary>
    /// Deletes the entities the biome spawned on these tiles: those a loaded chunk tracks, and those an unload kept
    /// where they grew. Nothing a player built.
    /// </summary>
    private void ClearBiomeEntities(Entity<BiomeComponent, MapGridComponent> map, IEnumerable<Vector2i> indices)
    {
        var doomed = new List<EntityUid>();

        foreach (var index in indices)
        {
            foreach (var anchored in _map.GetAnchoredEntities(map.Owner, map.Comp2, index))
            {
                if (_biome.WfIsBiomeSpawned((map.Owner, map.Comp1), anchored, index))
                    doomed.Add(anchored);
            }
        }

        foreach (var uid in doomed)
        {
            Del(uid);
        }
    }

    /// <summary>Whether an entity of this prototype is already anchored on a tile.</summary>
    private bool HasAnchored(Entity<MapGridComponent> grid, Vector2i index, EntProtoId proto)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, index);

        while (anchored.MoveNext(out var uid))
        {
            if (MetaData(uid.Value).EntityPrototype?.ID == proto.Id)
                return true;
        }

        return false;
    }

    /// <summary>Spawns an entity on a tile and anchors it there.</summary>
    private EntityUid SpawnAnchored(EntProtoId proto, Entity<MapGridComponent> grid, Vector2i index)
    {
        var uid = Spawn(proto, _map.GridTileToLocal(grid, grid.Comp, index));
        var xform = Transform(uid);

        if (!xform.Anchored && !_transform.AnchorEntity((uid, xform), grid, index))
            Log.Error($"Could not anchor {ToPrettyString(uid)} on {ToPrettyString(grid)} at {index}.");

        return uid;
    }

    /// <summary>The tile holding a world position.</summary>
    private static Vector2i Floor(Vector2 position)
    {
        return new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
    }
}

/// <summary>What the lazy claims have cost since the counters were last reset.</summary>
public sealed class WFCavernClaimStats
{
    /// <summary>Candidates checked against the noise.</summary>
    public int Candidates;

    /// <summary>Cell mouths stamped.</summary>
    public int Stamped;

    /// <summary>Milliseconds the stamps took, within <see cref="TotalMs"/>.</summary>
    public double StampMs;

    /// <summary>Ticks that did any claim work.</summary>
    public int BusyTicks;

    /// <summary>Milliseconds those ticks spent on claims.</summary>
    public double TotalMs;

    /// <summary>The most milliseconds one tick spent on claims.</summary>
    public double MaxTickMs;

    /// <summary>Zeroes every counter.</summary>
    public void Reset()
    {
        Candidates = 0;
        Stamped = 0;
        StampMs = 0;
        BusyTicks = 0;
        TotalMs = 0;
        MaxTickMs = 0;
    }
}
