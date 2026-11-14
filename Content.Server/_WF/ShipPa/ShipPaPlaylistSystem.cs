using System.Linq;
using Content.Server._WF.Audio.InternetSound;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipPa;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipPa;

/// <summary>What a skip did.</summary>
public enum ShipPaSkipResult
{
    Nothing,
    Skipped,
    TooSoon,
}

/// <summary>
/// A ship's PA playlist: links wait on the grid and play one after another through the internet sound system,
/// which still plays a single track per ship.
/// </summary>
public sealed class ShipPaPlaylistSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private InternetSoundSystem _internetSound = default!;
    [Dependency] private ShipPaSystem _pa = default!;

    /// <summary>How often playlists check whether their track has ended.</summary>
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Each skip starts another fetch, so skipping is paced.</summary>
    private static readonly TimeSpan SkipCooldown = TimeSpan.FromSeconds(3);

    private readonly List<EntityUid> _grids = new();
    private TimeSpan _nextUpdate;
    private int _nextId = 1;

    public override void Initialize()
    {
        base.Initialize();

        // After the sound system has released the finished track, so the next one can take its place.
        SubscribeLocalEvent<ShipPaTrackFinishedEvent>(OnTrackFinished, after: new[] { typeof(InternetSoundSystem) });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        // Starting the next track can add or remove the component, so don't enumerate while doing it.
        _grids.Clear();
        var query = EntityQueryEnumerator<ShipPaPlaylistComponent>();
        while (query.MoveNext(out var grid, out _))
        {
            _grids.Add(grid);
        }

        foreach (var grid in _grids)
        {
            if (TerminatingOrDeleted(grid) || !TryComp(grid, out ShipPaPlaylistComponent? playlist))
                continue;

            if (_internetSound.IsPlayingOn(grid))
            {
                RefreshTitle(grid, playlist);
                continue;
            }

            // Whatever was playing is over: it finished, failed to fetch or was stopped from elsewhere.
            playlist.Playing = null;
            Advance(grid, playlist);
        }

        _grids.Clear();
    }

    /// <summary>Whether a new link would start playing at once rather than join a queue.</summary>
    public bool IsIdle(EntityUid grid) =>
        !_internetSound.IsPlayingOn(grid) && CompOrNull<ShipPaPlaylistComponent>(grid) is not { Links.Count: > 0 };

    /// <summary>
    /// Plays the link at once on a quiet ship, otherwise queues it. Position is 0 if it started, else its place in
    /// the queue; error is a loc string for the requester when this returns false.
    /// </summary>
    public bool TryAdd(EntityUid grid, string url, string requester, ICommonSession? session, out int position, out string? error)
    {
        position = 0;
        error = null;

        var playlist = CompOrNull<ShipPaPlaylistComponent>(grid);
        var idle = IsIdle(grid);

        var link = new ShipPaQueuedLink
        {
            Id = _nextId++,
            Url = url,
            Label = ShipPaPlaylistText.ShortenLink(url),
            Requester = requester,
            Session = session,
        };

        if (idle)
        {
            if (!_internetSound.PlayOverPa(null, requester, url, grid, out error, session))
                return false;

            playlist ??= EnsureComp<ShipPaPlaylistComponent>(grid);
            playlist.Playing = link;
            Sync(grid, playlist);
            return true;
        }

        if (playlist?.Links.Count >= ShipPaPlaylistComponent.MaxQueued)
        {
            error = Loc.GetString("ship-pa-playlist-full");
            return false;
        }

        playlist ??= EnsureComp<ShipPaPlaylistComponent>(grid);
        playlist.Links.Add(link);
        position = playlist.Links.Count;
        Sync(grid, playlist);
        return true;
    }

    /// <summary>Ends the playing track and starts the next waiting link, if any. Wait is how long until skipping is allowed again.</summary>
    public ShipPaSkipResult Skip(EntityUid grid, out TimeSpan wait)
    {
        wait = TimeSpan.Zero;

        var playlist = CompOrNull<ShipPaPlaylistComponent>(grid);

        if (playlist != null && _timing.CurTime < playlist.NextSkip)
        {
            wait = playlist.NextSkip - _timing.CurTime;
            return ShipPaSkipResult.TooSoon;
        }

        var stopped = _internetSound.StopForGrid(grid);

        if (playlist == null)
            return stopped ? ShipPaSkipResult.Skipped : ShipPaSkipResult.Nothing;

        var waiting = playlist.Links.Count > 0;

        if (waiting)
            playlist.NextSkip = _timing.CurTime + SkipCooldown;

        playlist.Playing = null;
        Advance(grid, playlist);
        return stopped || waiting ? ShipPaSkipResult.Skipped : ShipPaSkipResult.Nothing;
    }

    /// <summary>Takes one waiting link out by its id. False if it is no longer in the queue.</summary>
    public bool Remove(EntityUid grid, int id)
    {
        if (!TryComp(grid, out ShipPaPlaylistComponent? playlist) || playlist.Links.RemoveAll(link => link.Id == id) == 0)
            return false;

        Sync(grid, playlist);
        return true;
    }

    /// <summary>Empties the queue and leaves the playing track alone. Returns how many links were dropped.</summary>
    public int ClearQueued(EntityUid grid)
    {
        if (!TryComp(grid, out ShipPaPlaylistComponent? playlist) || playlist.Links.Count == 0)
            return 0;

        var count = playlist.Links.Count;
        playlist.Links.Clear();
        Sync(grid, playlist);
        return count;
    }

    /// <summary>Ends the playing track and empties the queue. True if either had anything to end.</summary>
    public bool Stop(EntityUid grid)
    {
        var cleared = false;

        if (TryComp(grid, out ShipPaPlaylistComponent? playlist))
        {
            cleared = playlist.Links.Count > 0;
            playlist.Links.Clear();
            playlist.Playing = null;
            Sync(grid, playlist);
        }

        return _internetSound.StopForGrid(grid) || cleared;
    }

    /// <summary>Empties every ship's queue, for when all internet sounds are stopped.</summary>
    public void ClearAll()
    {
        var grids = new List<EntityUid>();
        var query = EntityQueryEnumerator<ShipPaPlaylistComponent>();
        while (query.MoveNext(out var grid, out _))
        {
            grids.Add(grid);
        }

        foreach (var grid in grids)
        {
            if (!TryComp(grid, out ShipPaPlaylistComponent? playlist))
                continue;

            playlist.Links.Clear();
            playlist.Playing = null;
            Sync(grid, playlist);
        }
    }

    private void OnTrackFinished(ref ShipPaTrackFinishedEvent ev)
    {
        if (ev.Key != InternetSoundSystem.TrackKey
            || !TryComp(ev.Grid, out ShipPaPlaylistComponent? playlist)
            || _internetSound.IsPlayingOn(ev.Grid))
            return;

        playlist.Playing = null;
        Advance(ev.Grid, playlist);
    }

    /// <summary>
    /// Starts the first waiting link that will play. A link that is refused is dropped and its requester told why.
    /// With no speaker working, or the server at its track cap, the queue waits, since nothing is wrong with the links.
    /// </summary>
    private void Advance(EntityUid grid, ShipPaPlaylistComponent playlist)
    {
        // Starting a link on a busy ship would replace its track.
        if (!_internetSound.IsPlayingOn(grid))
        {
            while (playlist.Links.Count > 0 && _pa.CountSpeakers(grid).Online > 0 && _internetSound.HasFreeSlot(grid))
            {
                var next = playlist.Links[0];
                playlist.Links.RemoveAt(0);

                string? error = null;

                if (!_cfg.GetCVar(InternetSoundCVars.PlayerRequests))
                    error = Loc.GetString("ship-pa-sound-disabled");
                else if (_internetSound.PlayOverPa(null, next.Requester, next.Url, grid, out error, next.Session))
                {
                    playlist.Playing = next;
                    break;
                }

                Drop(next, error ?? Loc.GetString("ship-pa-sound-refused"));
            }
        }

        Sync(grid, playlist);
    }

    private void Drop(ShipPaQueuedLink link, string message)
    {
        Log.Info($"Dropped queued PA link from {link.Requester}: {message}");

        if (link.Session is { Status: SessionStatus.InGame } session)
            _popup.PopupCursor(message, session, PopupType.MediumCaution);
    }

    /// <summary>Shows the playing track by its title once the sound system has fetched it.</summary>
    private void RefreshTitle(EntityUid grid, ShipPaPlaylistComponent playlist)
    {
        if (playlist.Playing is not { } playing || !TryComp(grid, out ShipPaBroadcastComponent? broadcasts))
            return;

        var title = broadcasts.Broadcasts.Find(broadcast => broadcast.Key == InternetSoundSystem.TrackKey)?.Caption;

        if (string.IsNullOrWhiteSpace(title))
            return;

        var label = ShipPaPlaylistText.Label(title);

        if (label == playing.Label)
            return;

        playing.Label = label;
        Sync(grid, playlist);
    }

    /// <summary>Copies the server's lists into the networked labels, or removes the component once it holds nothing.</summary>
    private void Sync(EntityUid grid, ShipPaPlaylistComponent playlist)
    {
        if (playlist.Playing == null && playlist.Links.Count == 0)
        {
            RemComp<ShipPaPlaylistComponent>(grid);
            return;
        }

        ShipPaPlaylistEntry? current = playlist.Playing is { } playing
            ? new ShipPaPlaylistEntry(playing.Id, playing.Label)
            : null;
        var queue = playlist.Links.Select(link => new ShipPaPlaylistEntry(link.Id, link.Label)).ToList();

        if (current == playlist.Current && queue.SequenceEqual(playlist.Queue))
            return;

        playlist.Current = current;
        playlist.Queue = queue;
        Dirty(grid, playlist);
    }
}
