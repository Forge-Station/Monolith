using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipPa;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.Audio.InternetSound;

public sealed partial class InternetSoundSystem
{
    /// <summary>The track each source item is playing.</summary>
    private readonly Dictionary<EntityUid, int> _bySource = new();

    /// <summary>
    /// Fetches a link and plays it out of an item with <see cref="InternetSoundSourceComponent"/>, replacing
    /// whatever it was playing. <paramref name="error"/> is a loc string for the caller when this returns false.
    /// </summary>
    public bool PlayFromSource(string requester, string url, Entity<InternetSoundSourceComponent> source, out string? error, ICommonSession? errorRecipient = null)
    {
        if (TerminatingOrDeleted(source))
        {
            error = Loc.GetString("wf-internet-sound-no-source");
            return false;
        }

        return Begin(null, requester, url, null, out error, errorRecipient, source: source);
    }

    private InternetSoundDownloader.Settings BuildSettings(bool stereo)
    {
        return new InternetSoundDownloader.Settings(
            _cfg.GetCVar(InternetSoundCVars.YtDlpPath),
            _cfg.GetCVar(InternetSoundCVars.FfmpegPath),
            _cfg.GetCVar(InternetSoundCVars.MaxDuration),
            _cfg.GetCVar(InternetSoundCVars.Timeout),
            _cfg.GetCVar(InternetSoundCVars.MaxSizeMb),
            Math.Clamp(_cfg.GetCVar(InternetSoundCVars.SampleRate), 8000, 48000),
            stereo ? 2 : 1,
            Math.Clamp(_cfg.GetCVar(InternetSoundCVars.Bitrate), 8, 192),
            (long) Math.Max(1, _cfg.GetCVar(InternetSoundCVars.MaxDownloadMb)) * 1024 * 1024);
    }

    /// <summary>
    /// Reads a playlist link off the main thread and hands back up to <paramref name="max"/> playable entries.
    /// <paramref name="done"/> runs on the main thread with the result, or null and a loc string on failure.
    /// </summary>
    public void ExpandPlaylist(string url, int max, Action<InternetSoundDownloader.PlaylistResult?, string?> done)
    {
        if (!_cfg.GetCVar(InternetSoundCVars.Enabled))
        {
            done(null, Loc.GetString("wf-internet-sound-disabled"));
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443)
        {
            done(null, Loc.GetString("wf-internet-sound-invalid-url"));
            return;
        }

        var settings = BuildSettings(false);
        Task.Run(async () =>
        {
            try
            {
                var result = await InternetSoundDownloader.ExpandPlaylist(uri.AbsoluteUri, settings, max, CancellationToken.None);
                _task.RunOnMainThread(() => done(result, null));
            }
            catch (OperationCanceledException)
            {
                _task.RunOnMainThread(() => done(null, Loc.GetString("wf-internet-sound-error-timeout")));
            }
            catch (InternetSoundDownloader.FetchException e)
            {
                _task.RunOnMainThread(() => done(null, Loc.GetString(e.LocKey, ("detail", e.Message))));
            }
            catch (Exception e)
            {
                _task.RunOnMainThread(() =>
                {
                    Log.Error($"Playlist read for {uri.AbsoluteUri} failed: {e}");
                    done(null, Loc.GetString("wf-internet-sound-error-unknown"));
                });
            }
        });
    }

    /// <summary>Stops whatever this item is playing. True if there was something to stop.</summary>
    public bool StopForSource(EntityUid source)
    {
        if (!_bySource.TryGetValue(source, out var id) || !_tracks.TryGetValue(id, out var track))
            return false;

        Drop(track);
        SendState();
        return true;
    }

    /// <summary>True when this item has an internet sound fetching, sending or playing.</summary>
    public bool IsPlayingFrom(EntityUid source)
    {
        return _bySource.ContainsKey(source);
    }

    private bool StartSource(Track track, EntityUid source, string path)
    {
        if (TerminatingOrDeleted(source) || !TryComp(source, out InternetSoundSourceComponent? comp))
            return false;

        // Lead time lets nearby clients that already have the audio start together.
        comp.Path = path;
        comp.TrackId = track.Id;
        comp.Start = _timing.CurTime + TimeSpan.FromSeconds(ShipPaPlaybackPolicy.StartLeadSeconds);
        comp.Length = (float) track.Length.TotalSeconds;
        comp.Title = track.Title;
        Dirty(source, comp);

        track.Ends = comp.Start + track.Length;

        var ev = new InternetSoundSourceChangedEvent(true);
        RaiseLocalEvent(source, ref ev);
        return true;
    }

    private void StopSource(EntityUid source, int id)
    {
        if (TerminatingOrDeleted(source) || !TryComp(source, out InternetSoundSourceComponent? comp))
            return;

        if (comp.TrackId == id && comp.Path != null)
        {
            comp.Path = null;
            comp.Title = null;
            Dirty(source, comp);
        }

        var ev = new InternetSoundSourceChangedEvent(false);
        RaiseLocalEvent(source, ref ev);
    }

    /// <summary>Offers the audio to everyone within earshot of the item, plus a margin for walking up to it.</summary>
    private void SendToSourceAudience(Track track)
    {
        if (track.Source is not { } source || TerminatingOrDeleted(source)
            || !TryComp(source, out InternetSoundSourceComponent? comp)
            || !TryComp(source, out TransformComponent? xform)
            || xform.MapID == MapId.Nullspace)
            return;

        var margin = _cfg.GetCVar(InternetSoundCVars.PaPrefetchMargin);
        margin = float.IsFinite(margin) ? Math.Clamp(margin, 0f, 1024f) : 64f;
        var reach = comp.Range + margin;
        var map = xform.MapID;
        var position = _xform.GetWorldPosition(xform);

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || track.Recipients.Contains(session.Channel))
                continue;

            if (IsNear(session.AttachedEntity) || session.ViewSubscriptions.Any(viewer => IsNear(viewer)))
                SendOnce(track, session);
        }

        bool IsNear(EntityUid? viewer)
        {
            if (!TryComp(viewer, out TransformComponent? viewerXform) || viewerXform.MapID != map)
                return false;

            var at = _xform.GetWorldPosition(viewerXform);
            if (TryComp(viewer, out EyeComponent? eye))
                at += eye.Offset;

            return Vector2.DistanceSquared(at, position) <= reach * reach;
        }
    }
}
