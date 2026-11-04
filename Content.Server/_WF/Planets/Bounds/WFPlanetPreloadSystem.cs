using System.Linq;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.GameTicking;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets.Bounds;

/// <summary>
/// Generates a bounded world's whole ground when its network is built, nearest the centre first and a slice a tick,
/// then walls its outermost ring. One world at a time; the others wait their turn.
/// </summary>
public sealed partial class WFPlanetPreloadSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WFPlanetBoundsSystem _bounds = default!;

    /// <summary>Worlds waiting for or in the middle of their preload, first come first served.</summary>
    private readonly List<Preload> _queue = new();
    private readonly Stopwatch _watch = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt);
    }

    /// <summary>Whether a ground is queued or in the middle of its preload.</summary>
    public bool IsPreloading(EntityUid ground)
    {
        foreach (var preload in _queue)
        {
            if (preload.Ground == ground)
                return true;
        }

        return false;
    }

    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        if (!_cfg.GetCVar(PlanetCVars.Preload) || !args.Surface.Preload)
            return;

        // Starts next tick, once every handler of the build has had its turn with the fresh ground; the bounds are
        // read then, since the handler that marks them may run after this one.
        _queue.Add(new Preload(args.Ground, args.Surface.BoundaryWall));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ResumeWaiting();

        while (_queue.Count > 0)
        {
            var preload = _queue[0];

            if (TerminatingOrDeleted(preload.Ground)
                || !TryComp<BiomeComponent>(preload.Ground, out var biome)
                || !TryComp<MapGridComponent>(preload.Ground, out var grid)
                || !TryComp<WFPlanetBoundsComponent>(preload.Ground, out var bounds)
                || bounds.Radius <= 0f)
            {
                _queue.RemoveAt(0);
                continue;
            }

            if (preload.Holds == null)
            {
                preload.Holds = new List<Task>();
                preload.Phase = PreloadPhase.Holds;
                var starting = new WFPlanetPreloadStartingEvent(preload.Ground, bounds.Centre, bounds.Radius, preload.Holds);
                GenerationSpent();
                RaiseLocalEvent(ref starting);
                preload.Started = Stopwatch.GetTimestamp();
            }

            // Whatever must be in place before the terrain, such as a dungeon, finishes first.
            if (!HoldsDone(preload))
                return;

            if (preload.Phase == PreloadPhase.Holds)
                preload.Phase = PreloadPhase.Terrain;
            preload.Chunks ??= _biome.WfChunksInBounds(bounds);

            Load(preload, (preload.Ground, biome, grid));

            if (preload.Next < preload.Chunks.Count)
                return;

            // The ring and whatever the other modules do once the ground exists go a slice a tick too.
            preload.Finishing ??= FinishAsync(preload, (preload.Ground, biome, grid), bounds);
            if (!preload.Finishing.IsCompleted)
                return;

            if (preload.Finishing.IsFaulted)
                Log.Error($"Finishing the preload of {ToPrettyString(preload.Ground)} failed. {preload.Finishing.Exception}");

            _queue.RemoveAt(0);
        }
    }

    /// <summary>Whether every hold on a preload has finished; a failed one is logged and let go.</summary>
    private bool HoldsDone(Preload preload)
    {
        foreach (var hold in preload.Holds!)
        {
            if (!hold.IsCompleted)
                return false;

            if (hold.IsFaulted && preload.Faulted.Add(hold))
                Log.Error($"A hold on the preload of {ToPrettyString(preload.Ground)} failed. {hold.Exception}");
        }

        return true;
    }

    /// <summary>Loads chunks until the tick's budget is spent, the lobby's while the round hasn't started; one always goes.</summary>
    private void Load(Preload preload, Entity<BiomeComponent, MapGridComponent> ground)
    {
        var budget = _cfg.GetCVar(_ticker.RunLevel == GameRunLevel.PreRoundLobby ? PlanetCVars.PreloadBudgetLobby : PlanetCVars.PreloadBudget);
        // Generation already done this tick counts against it, and what is loaded here against generation's.
        var spent = TickWork();
        _watch.Restart();

        while (preload.Next < preload.Chunks!.Count)
        {
            // Not held: once formed, what nobody is near is stashed (BiomeSystem.WFStash).
            _biome.WfLoadChunk(ground, preload.Chunks[preload.Next++], hold: false);

            if (spent + _watch.Elapsed.TotalMilliseconds >= budget)
                break;
        }
    }

    /// <summary>
    /// Lays the boundary ring on the ground and the layers below it, tells the other modules and waits for what they
    /// start, then marks the ground preloaded.
    /// </summary>
    private async Task FinishAsync(Preload preload, Entity<BiomeComponent, MapGridComponent> ground, WFPlanetBoundsComponent bounds)
    {
        preload.Phase = PreloadPhase.Boundary;
        var lowers = TryComp<WFPlanetLayerComponent>(ground, out var layer)
                     && TryGetEntity(layer.Network, out var network)
                     && TryComp<WFPlanetNetworkComponent>(network, out var comp)
            ? comp.LowerLayers.ToList()
            : new List<EntityUid>();
        preload.Rings = 1 + lowers.Count;

        var walls = await LayRingAsync(ground, bounds, preload);

        // A cavern stays streamed, so its ring is laid onto bare ground and pinned: the loader fills in round it.
        foreach (var lower in lowers)
        {
            preload.RingsLaid++;
            if (TryComp<BiomeComponent>(lower, out var lowerBiome)
                && TryComp<MapGridComponent>(lower, out var lowerGrid)
                && TryComp<WFPlanetBoundsComponent>(lower, out var lowerBounds))
                walls += await LayRingAsync((lower, lowerBiome, lowerGrid), lowerBounds, preload);
        }

        preload.Phase = PreloadPhase.Wildlife;

        if (TerminatingOrDeleted(ground))
            return;

        var work = new List<Task>();
        var ev = new WFPlanetPreloadedEvent(ground, work);
        RaiseLocalEvent(ref ev);
        preload.Work = work.Count;
        foreach (var task in work)
        {
            try
            {
                await task;
                preload.WorkDone++;
            }
            catch (Exception error)
            {
                Log.Error($"Work after the preload of {ToPrettyString(ground)} failed. {error}");
            }
        }

        if (TerminatingOrDeleted(ground))
            return;

        var stashed = await StashAsync(preload, ground);
        if (TerminatingOrDeleted(ground))
            return;

        EnsureComp<WFPlanetPreloadedComponent>(ground);
        var took = Stopwatch.GetElapsedTime(preload.Started).TotalSeconds;
        Log.Info($"Preloaded {preload.Chunks!.Count} chunks of {ToPrettyString(ground)} in {took:F1} s, with {walls} boundary walls; {stashed} chunks stashed.");
    }

    /// <summary>
    /// Puts away the biome entities of every chunk nobody is near, once whatever needed them in place has been
    /// placed; they come back as someone does.
    /// </summary>
    private async Task<int> StashAsync(Preload preload, Entity<BiomeComponent, MapGridComponent> ground)
    {
        if (!_biome.WfStashEnabled)
            return 0;

        var stashed = 0;
        foreach (var chunk in preload.Chunks!)
        {
            await Breathe();
            if (TerminatingOrDeleted(ground))
                return stashed;

            if (_biome.WfStashIfIdle(ground, chunk))
                stashed++;
        }

        return stashed;
    }

    /// <summary>
    /// Walls every tile inside the circle with a neighbour outside it: whatever the biome grew there goes, a tile not
    /// loaded yet is laid from the recipe, and the tile is pinned so nothing grows back or is ever unloaded.
    /// </summary>
    private async Task<int> LayRingAsync(Entity<BiomeComponent, MapGridComponent> ground, WFPlanetBoundsComponent bounds, Preload preload)
    {
        if (preload.Wall is not { } wall)
            return 0;

        var ring = new List<Vector2i>();
        var radius = (int) MathF.Ceiling(bounds.Radius) + 1;
        var centre = new Vector2i((int) MathF.Floor(bounds.Centre.X), (int) MathF.Floor(bounds.Centre.Y));

        for (var x = centre.X - radius; x <= centre.X + radius; x++)
        for (var y = centre.Y - radius; y <= centre.Y + radius; y++)
        {
            var tile = new Vector2i(x, y);

            if (!bounds.ContainsTile(tile))
                continue;

            if (bounds.ContainsTile(tile + Vector2i.Right) && bounds.ContainsTile(tile + Vector2i.Left)
                && bounds.ContainsTile(tile + Vector2i.Up) && bounds.ContainsTile(tile + Vector2i.Down))
                continue;

            ring.Add(tile);
        }

        // Pinned first, so nothing grows back on a stretch still waiting for its wall.
        _biome.WfPinTiles((ground.Owner, ground.Comp1), ring);

        var laid = 0;
        var anchored = new List<EntityUid>();
        preload.RingTiles = ring.Count;
        preload.RingTile = 0;

        foreach (var tile in ring)
        {
            preload.RingTile++;
            await Breathe();
            if (TerminatingOrDeleted(ground))
                return laid;

            // A ring tile the biome left empty, such as under water, is filled so the wall has something to stand on.
            if (!_map.TryGetTileRef(ground, ground.Comp2, tile, out var existing) || existing.Tile.IsEmpty)
            {
                if (!_biome.TryGetBiomeTile(tile, ground.Comp1.Layers, ground.Comp1.Seed, (ground.Owner, ground.Comp2), out var natural))
                    continue;

                _map.SetTile(ground, ground.Comp2, tile, natural.Value);
            }

            anchored.Clear();
            var enumerator = _map.GetAnchoredEntitiesEnumerator(ground, ground.Comp2, tile);

            while (enumerator.MoveNext(out var uid))
            {
                anchored.Add(uid.Value);
            }

            foreach (var uid in anchored)
            {
                QueueDel(uid);
            }

            Spawn(wall, _map.GridTileToLocal(ground, ground.Comp2, tile));
            laid++;
        }

        return laid;
    }

    /// <summary>One world's preload: its chunks nearest first, and how far it has got.</summary>
    private sealed class Preload
    {
        public readonly EntityUid Ground;
        public readonly EntProtoId? Wall;
        public List<Task>? Holds;
        public readonly HashSet<Task> Faulted = new();
        public List<Vector2i>? Chunks;
        public int Next;
        public Task? Finishing;
        public long Started;
        public PreloadPhase Phase;

        /// <summary>The latest stage and fraction each hold reported, by who reported it.</summary>
        public readonly Dictionary<string, (WFWorldFormingStage Stage, float Fraction)> Reports = new();

        public int Rings = 1;
        public int RingsLaid;
        public int RingTiles;
        public int RingTile;
        public int Work;
        public int WorkDone;

        /// <summary>The furthest progress shown so far, so the bar never steps back.</summary>
        public float Shown;

        public Preload(EntityUid ground, EntProtoId? wall)
        {
            Ground = ground;
            Wall = wall;
        }
    }
}
