using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using ChunkIndicesEnumerator = Robust.Shared.Map.Enumerators.ChunkIndicesEnumerator;

namespace Content.Server.Parallax;

/// <summary>
/// Spreads a planet layer's chunk loading over a few passes. Upstream loads every chunk of a load area in the pass it
/// comes into range: eighty-one when someone arrives, and a strip of fourteen or more each second under anyone flying.
/// </summary>
// What still loads at once: the chunks round every player, fewer round every eye, and the ground and air under any hull. The rest loads nearest
// first, a few milliseconds' worth a pass, in the 16-tile blocks the grid is sent in, so no block is sent twice.
public sealed partial class BiomeSystem
{
    /// <summary>How many chunks round a player's own body load in the pass they come into range, by default.</summary>
    public const int WfLoadNowRing = 0;

    /// <summary>How many tiles round a player's own body load in the pass they come into range, by default.</summary>
    public const float WfLoadNowPad = 2f;

    /// <summary>
    /// How many chunks round an eye load in the pass they come into range. An eye looks down a hole, or at the ground
    /// from the air, where the far part filling in a moment late is little to see, and an eye's first sight of a layer
    /// is where most whole areas get loaded.
    /// </summary>
    public const int WfLoadNowEyeRing = 1;

    /// <summary>The side in tiles of the blocks put-off chunks load in: one grid chunk, which is always sent whole.</summary>
    public const int WfLoadBlock = 16;

    private double _wfLoadBudgetMs;
    private double _wfLoadSpentMs;
    private GameTick _wfLoaderTick;
    private GameTick _wfHullTick;

    /// <summary>Per biome: the chunk each of this pass's loaders is in, and how many chunks round it load at once.</summary>
    private readonly Dictionary<BiomeComponent, List<WfLoader>> _wfLoaders = new();

    /// <summary>Per planet network: the bounds of every hull on its layers and in the gaps between them.</summary>
    private readonly Dictionary<NetEntity, List<Box2>> _wfHullBounds = new();

    private readonly Stack<List<WfLoader>> _wfLoaderPool = new();
    private readonly HashSet<Vector2i> _wfNearBlocks = new();
    private readonly List<(int Distance, Vector2i Block, Vector2i Chunk)> _wfPending = new();
    private readonly Stopwatch _wfLoadWatch = new();
    private BiomeComponent? _wfLoadFor;
    private List<WfLoader>? _wfLoadLoaders;
    private GameTick _wfBodySettingsTick;
    private int _wfBodyRing = WfLoadNowRing;
    private float _wfBodyPad = WfLoadNowPad;

    /// <summary>A loader: its chunk, its position, and how much round it loads at once (whole chunks, and tiles).</summary>
    private readonly record struct WfLoader(Vector2i Chunk, Vector2 Pos, int Ring, float Pad);

    /// <summary>Notes a player's body as a loader; only the chunk it stands in and the tiles round it load at once.</summary>
    private void WfNoteBodyLoader(BiomeComponent biome, Vector2 worldPos)
    {
        if (_wfBodySettingsTick != _wfTiming.CurTick)
        {
            _wfBodySettingsTick = _wfTiming.CurTick;
            _wfBodyRing = _configManager.GetCVar(PlanetCVars.TerrainLoadNowRing);
            _wfBodyPad = _configManager.GetCVar(PlanetCVars.TerrainLoadNowPad);
        }

        WfNoteLoader(biome, worldPos, _wfBodyRing, _wfBodyPad);
    }

    /// <summary>Notes where a loader is, and how much round it must load at once, as its load area is added.</summary>
    private void WfNoteLoader(BiomeComponent biome, Vector2 worldPos, int ring, float pad = 0f)
    {
        var tick = _wfTiming.CurTick;

        if (_wfLoaderTick != tick)
        {
            _wfLoaderTick = tick;

            foreach (var list in _wfLoaders.Values)
            {
                list.Clear();
                _wfLoaderPool.Push(list);
            }

            _wfLoaders.Clear();
        }

        if (!_wfLoaders.TryGetValue(biome, out var loaders))
            _wfLoaders[biome] = loaders = _wfLoaderPool.TryPop(out var pooled) ? pooled : new List<WfLoader>();

        loaders.Add(new WfLoader(SharedMapSystem.GetChunkIndices(worldPos, ChunkSize) * ChunkSize, worldPos, ring, pad));
    }

    /// <summary>Whether a chunk of a planet layer's load area is put off to <see cref="WfLoadDeferred"/>, or never loads at all.</summary>
    private bool WfDeferLoad(BiomeComponent biome, EntityUid map, Vector2i chunk)
    {
        // Outside a bounded world's circle there is no ground.
        if (WfOutOfBounds(map, chunk))
            return true;

        if (!ReferenceEquals(_wfLoadFor, biome))
            WfBeginLoad(biome, map);

        // A stashed chunk's tiles are there; its entities are laid again after the pass's loads.
        if (biome.LoadedChunks.Contains(chunk))
        {
            WfNoteRestore(biome, map, chunk);
            return false;
        }

        if (_wfLoadLoaders == null)
            return false;

        var block = WfBlockOf(chunk);

        if (_wfNearBlocks.Contains(block))
            return false;

        _wfPending.Add((WfBlockDistance(block, _wfLoadLoaders), block, chunk));
        return true;
    }

    /// <summary>Works out which blocks of a layer load at once this pass: those round its loaders and under hulls.</summary>
    private void WfBeginLoad(BiomeComponent biome, EntityUid map)
    {
        _wfLoadFor = biome;
        _wfLoadLoaders = null;
        _wfPending.Clear();
        _wfNearBlocks.Clear();

        if (_wfLoadBudgetMs <= 0
            || _wfLoaderTick != _wfTiming.CurTick
            || !_wfLoaders.TryGetValue(biome, out var loaders)
            || loaders.Count == 0
            || !TryComp<WFPlanetLayerComponent>(map, out var layer))
            return;

        try
        {
            foreach (var (loader, pos, ring, pad) in loaders)
            {
                for (var x = -ring; x <= ring; x++)
                for (var y = -ring; y <= ring; y++)
                {
                    _wfNearBlocks.Add(WfBlockOf(loader + new Vector2i(x, y) * ChunkSize));
                }

                // The blocks within a few steps of a body: ground it could walk onto before the next pass.
                if (pad > 0f)
                {
                    var blocks = new ChunkIndicesEnumerator(Box2.CenteredAround(pos, new Vector2(pad * 2f)), WfLoadBlock);

                    while (blocks.MoveNext(out var index))
                    {
                        _wfNearBlocks.Add(index.Value * WfLoadBlock);
                    }
                }
            }

            // A hull's riders load its ground through eyes that follow it a second late, and it lands on what is there.
            // None goes below the ground, so a cavern has no hull to hurry for.
            if (layer.Network is { } network
                && !(TryComp<CEZMapComponent>(map, out var level) && level.Depth < 0)
                && WfHullBounds().TryGetValue(network, out var hulls))
            {
                foreach (var bounds in hulls)
                {
                    var blocks = new ChunkIndicesEnumerator(bounds, WfLoadBlock);

                    while (blocks.MoveNext(out var index))
                    {
                        _wfNearBlocks.Add(index.Value * WfLoadBlock);
                    }
                }
            }

            _wfLoadLoaders = loaders;
        }
        catch (Exception e)
        {
            // Thrown out of Update, this would stop every biome loading; the layer loads all at once instead.
            Log.Error($"Putting off chunk loads for {ToPrettyString(map)} failed. {e}");
        }
    }

    /// <summary>Loads what a layer put off this pass, nearest block first, until the pass's time is spent; one block always loads.</summary>
    private void WfLoadDeferred(BiomeComponent biome, EntityUid map, MapGridComponent grid, int seed)
    {
        WfRestoreStashed(biome, map, grid);

        if (!ReferenceEquals(_wfLoadFor, biome) || _wfLoadLoaders == null)
            return;

        if (_wfPending.Count == 0)
            return;

        _wfPending.Sort(static (a, b) =>
        {
            var order = a.Distance.CompareTo(b.Distance);
            if (order == 0)
                order = a.Block.X.CompareTo(b.Block.X);
            if (order == 0)
                order = a.Block.Y.CompareTo(b.Block.Y);
            if (order == 0)
                order = a.Chunk.X.CompareTo(b.Chunk.X);
            return order != 0 ? order : a.Chunk.Y.CompareTo(b.Chunk.Y);
        });

        Vector2i? current = null;

        try
        {
            foreach (var (_, block, chunk) in _wfPending)
            {
                if (block != current)
                {
                    // A block goes whole or not at all: the grid sends it again for each part that loads on a later tick.
                    if (current != null && _wfLoadSpentMs >= _wfLoadBudgetMs)
                        break;

                    current = block;
                }

                // Claimed as it is filled, never before: an empty tile on a loaded chunk reads as a hole.
                if (!biome.LoadedChunks.Add(chunk))
                    continue;

                _wfLoadWatch.Restart();
                LoadChunk(biome, map, grid, chunk, seed);
                _wfLoadSpentMs += _wfLoadWatch.Elapsed.TotalMilliseconds;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Loading put-off chunks of {ToPrettyString(map)} failed. {e}");
        }
        finally
        {
            _wfPending.Clear();
        }
    }

    /// <summary>The origin of the block a chunk loads with.</summary>
    private static Vector2i WfBlockOf(Vector2i chunk)
    {
        return SharedMapSystem.GetChunkIndices(chunk, WfLoadBlock) * WfLoadBlock;
    }

    /// <summary>How many tiles lie between a block and the nearest loader.</summary>
    private static int WfBlockDistance(Vector2i block, List<WfLoader> loaders)
    {
        var nearest = int.MaxValue;

        foreach (var loader in loaders)
        {
            var x = Math.Max(Math.Max(block.X - loader.Pos.X, loader.Pos.X - (block.X + WfLoadBlock)), 0f);
            var y = Math.Max(Math.Max(block.Y - loader.Pos.Y, loader.Pos.Y - (block.Y + WfLoadBlock)), 0f);
            nearest = Math.Min(nearest, (int) Math.Max(x, y));
        }

        return nearest;
    }

    /// <summary>The bounds of every hull over each planet, a chunk wider all round, worked out once a pass.</summary>
    private Dictionary<NetEntity, List<Box2>> WfHullBounds()
    {
        var tick = _wfTiming.CurTick;

        if (_wfHullTick == tick)
            return _wfHullBounds;

        _wfHullTick = tick;

        foreach (var list in _wfHullBounds.Values)
        {
            list.Clear();
        }

        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();

        while (grids.MoveNext(out var uid, out var hull, out var xform))
        {
            if (xform.MapUid is not { } map || uid == map || WfNetworkOf(map) is not { } network)
                continue;

            var bounds = _transform.GetWorldMatrix(xform).TransformBox(hull.LocalAABB).Enlarged(ChunkSize);
            _wfHullBounds.GetOrNew(network).Add(bounds);
        }

        return _wfHullBounds;
    }

    /// <summary>The planet network a map belongs to: a layer's own, or that of the layers a gap lies between.</summary>
    private NetEntity? WfNetworkOf(EntityUid map)
    {
        if (TryComp<WFPlanetLayerComponent>(map, out var layer))
            return layer.Network;

        if (!TryComp<CEZTransitMapComponent>(map, out var gap))
            return null;

        return TryComp(gap.LowerMap ?? gap.UpperMap, out layer) ? layer.Network : null;
    }
}
