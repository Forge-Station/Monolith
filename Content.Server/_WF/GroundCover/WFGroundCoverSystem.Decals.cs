using System.Linq;
using Content.Server.Decals;
using Content.Shared.Decals;
using Content.Shared._WF.GroundCover;
using Robust.Shared.Random;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Wetness a map needs before its rain washes decals.</summary>
    public const float DecalWetness = 0.3f;

    /// <summary>Most decals washed off one chunk in one pass.</summary>
    public const int DecalsPerChunk = 6;

    /// <summary>Chance a washable decal goes in one pass at full wetness.</summary>
    public const float DecalWashChance = 0.35f;

    private const int DecalChunksPerStep = 2;

    private static readonly TimeSpan DecalInterval = TimeSpan.FromSeconds(1);

    private readonly Queue<(EntityUid Grid, Vector2i Chunk)> _decalQueue = new();
    private readonly List<(EntityUid Grid, Vector2i Chunk)> _decalView = new();
    private TimeSpan _nextDecal;

    /// <summary>Rain washes decals off open ground near players, a couple of chunks a second.</summary>
    private void UpdateDecals(TimeSpan now)
    {
        if (now < _nextDecal)
            return;

        _nextDecal = now + DecalInterval;
        if (_decalQueue.Count == 0)
        {
            foreach (var session in _player.Sessions)
            {
                _decalView.Clear();
                CollectView(session, _decalView);
                foreach (var key in _decalView)
                {
                    _decalQueue.Enqueue(key);
                }
            }
        }

        for (var i = 0; i < DecalChunksPerStep && _decalQueue.TryDequeue(out var chunk); i++)
        {
            WashDecals(chunk.Grid, chunk.Chunk);
        }
    }

    /// <summary>
    /// Washes some decals off the open tiles of a chunk while its map is rained on. Only decals flagged cleanable go, so
    /// mapped road markings and street paint stay. Returns how many went.
    /// </summary>
    public int WashDecals(EntityUid gridUid, Vector2i chunk)
    {
        if (!TryComp(gridUid, out TransformComponent? xform)
            || !TryComp<WFGroundWeatherComponent>(xform.MapUid, out var ground)
            || ground.WetnessExact < DecalWetness
            || !TryComp<DecalGridComponent>(gridUid, out var decalGrid)
            || !CanHoldCover(gridUid))
            return 0;

        var origin = new System.Numerics.Vector2(chunk.X, chunk.Y) * WFSnowCover.ChunkSize;
        var bounds = new Box2(origin, origin + new System.Numerics.Vector2(WFSnowCover.ChunkSize));
        var chance = DecalWashChance * ground.WetnessExact;
        var removed = 0;
        foreach (var (index, decal) in _decals.GetDecalsIntersecting(gridUid, bounds, decalGrid).ToList())
        {
            if (!decal.Cleanable)
                continue;

            var tile = new Vector2i((int) MathF.Floor(decal.Coordinates.X), (int) MathF.Floor(decal.Coordinates.Y));
            if (!IsOpen(gridUid, tile) || !_random.Prob(chance))
                continue;

            _decals.RemoveDecal(gridUid, index, decalGrid);
            if (++removed >= DecalsPerChunk)
                break;
        }

        return removed;
    }
}
