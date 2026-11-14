using Content.Shared._WF.GroundCover;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>Half the side of the square around a player whose snow chunks are worked and sent, in tiles.</summary>
    public const float ViewRange = 40f;

    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(30);

    /// <summary>The chunk versions each session was last sent; a chunk it holds that's gone is sent as cleared.</summary>
    private readonly Dictionary<ICommonSession, Dictionary<(EntityUid Grid, Vector2i Chunk), uint>> _sent = new();

    private readonly Dictionary<EntityUid, WFSnowCoverChunksMessage> _messages = new();
    private readonly List<(EntityUid Grid, Vector2i Chunk)> _stale = new();
    private readonly HashSet<EntityUid> _forgotten = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private TimeSpan _nextSend;
    private TimeSpan _nextPrune;

    private void InitializeNetwork()
    {
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeNetworkEvent<WFSnowCoverForgetMessage>(OnForget);
    }

    /// <summary>Forgets what a session was sent of grids its client dropped, so their snow goes again.</summary>
    private void OnForget(WFSnowCoverForgetMessage message, EntitySessionEventArgs args)
    {
        if (!_sent.TryGetValue(args.SenderSession, out var sent))
            return;

        // Resolved this way round: a grid in the record may already be deleted, with no net entity left.
        _forgotten.Clear();
        foreach (var net in message.Grids)
        {
            if (TryGetEntity(net, out var grid))
                _forgotten.Add(grid.Value);
        }

        _stale.Clear();
        foreach (var key in sent.Keys)
        {
            if (_forgotten.Contains(key.Grid))
                _stale.Add(key);
        }

        foreach (var key in _stale)
        {
            sent.Remove(key);
        }
    }

    private void ShutdownNetwork()
    {
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
        _sent.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is SessionStatus.Disconnected or SessionStatus.Connected)
            _sent.Remove(args.Session);
    }

    private void UpdateNetwork(TimeSpan now)
    {
        if (now < _nextSend)
            return;

        _nextSend = now + SendInterval;
        Publish();
        var prune = now >= _nextPrune;
        if (prune)
            _nextPrune = now + PruneInterval;

        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            if (!_sent.TryGetValue(session, out var sent))
            {
                sent = new Dictionary<(EntityUid, Vector2i), uint>();
                _sent[session] = sent;
            }

            if (prune)
                Prune(sent);

            _view.Clear();
            CollectView(session, _view);
            SendView(session, sent);
        }
    }

    /// <summary>Goes up whenever the depths sent to clients change.</summary>
    public int SentStamp { get; private set; }

    /// <summary>Takes the depths of every changed chunk as the ones sent from now on.</summary>
    private void Publish()
    {
        var query = EntityQueryEnumerator<WFSnowCoverComponent>();
        while (query.MoveNext(out var cover))
        {
            foreach (var chunk in cover.Chunks.Values)
            {
                if (chunk.Sent != null && chunk.SentVersion == chunk.Version)
                    continue;

                chunk.Sent = (byte[]) chunk.Depths.Clone();
                chunk.SentVersion = chunk.Version;
                SentStamp++;
            }
        }
    }

    /// <summary>
    /// Snow depth on a tile as clients were last sent it. With a session, as that session was sent it: 0 until it has
    /// the chunk, as its client would read.
    /// </summary>
    public int GetSentDepth(EntityUid grid, Vector2i tile, ICommonSession? session = null)
    {
        var index = WFSnowCover.ChunkOf(tile);
        if (!TryComp<WFSnowCoverComponent>(grid, out var cover)
            || !cover.Chunks.TryGetValue(index, out var chunk)
            || chunk.Sent is not { } depths)
            return 0;

        if (session != null && (!_sent.TryGetValue(session, out var sent)
                                || !sent.TryGetValue((grid, index), out var version)
                                || version != chunk.SentVersion))
            return 0;

        return depths[WFSnowCover.IndexIn(tile)];
    }

    /// <summary>Sends a session the chunks in its view that changed since it was last sent them.</summary>
    private void SendView(ICommonSession session, Dictionary<(EntityUid Grid, Vector2i Chunk), uint> sent)
    {
        _messages.Clear();
        foreach (var key in _view)
        {
            WFSnowChunk? chunk = null;
            if (TryComp<WFSnowCoverComponent>(key.Grid, out var cover))
                cover.Chunks.TryGetValue(key.Chunk, out chunk);

            var had = sent.TryGetValue(key, out var version);
            byte[]? depths;
            if (chunk == null)
            {
                if (!had)
                    continue;

                sent.Remove(key);
                depths = null;
            }
            else
            {
                if (chunk.Sent == null || had && version == chunk.SentVersion)
                    continue;

                sent[key] = chunk.SentVersion;
                depths = chunk.Sent;
            }

            if (!_messages.TryGetValue(key.Grid, out var message))
            {
                message = new WFSnowCoverChunksMessage { Grid = GetNetEntity(key.Grid) };
                _messages[key.Grid] = message;
            }

            message.Chunks.Add(new WFSnowChunkData(key.Chunk, depths));
        }

        foreach (var message in _messages.Values)
        {
            RaiseNetworkEvent(message, session);
        }
    }

    /// <summary>Forgets what a session was sent of grids that are gone.</summary>
    private void Prune(Dictionary<(EntityUid Grid, Vector2i Chunk), uint> sent)
    {
        _stale.Clear();
        foreach (var key in sent.Keys)
        {
            if (TerminatingOrDeleted(key.Grid))
                _stale.Add(key);
        }

        foreach (var key in _stale)
        {
            sent.Remove(key);
        }
    }
}
