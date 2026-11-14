using System.Linq;
using Content.Server._WF.Audio.InternetSound;
using Content.Server._WF.ShipPa;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.Boombox;
using Content.Shared._WF.CCVar;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Boombox;

/// <summary>
/// The boombox's playlist: links queue on the box and play one after another out of it through the internet
/// sound system. A playlist link adds up to <see cref="WFBoomboxComponent.MaxQueued"/> playable tracks.
/// </summary>
public sealed partial class WFBoomboxSystem : EntitySystem
{
    [Dependency] private InternetSoundSystem _internetSound = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan SkipCooldown = TimeSpan.FromSeconds(3);

    /// <summary>Quietest the volume knob goes before it is off.</summary>
    private const float MinGain = 0.02f;

    private TimeSpan _nextUpdate;
    private int _nextId = 1;
    private readonly List<EntityUid> _boxes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFBoomboxComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WFBoomboxComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFBoomboxComponent, InternetSoundSourceChangedEvent>(OnSourceChanged);
        SubscribeLocalEvent<WFBoomboxComponent, ComponentShutdown>(OnShutdown);

        Subs.BuiEvents<WFBoomboxComponent>(WFBoomboxUiKey.Key, subs =>
        {
            subs.Event<WFBoomboxAddMessage>(OnAdd);
            subs.Event<WFBoomboxSkipMessage>(OnSkip);
            subs.Event<WFBoomboxStopMessage>(OnStop);
            subs.Event<WFBoomboxClearMessage>(OnClear);
            subs.Event<WFBoomboxShuffleMessage>(OnShuffle);
            subs.Event<WFBoomboxRepeatMessage>(OnRepeat);
            subs.Event<WFBoomboxRemoveMessage>(OnRemove);
            subs.Event<WFBoomboxMoveUpMessage>(OnMoveUp);
            subs.Event<WFBoomboxVolumeMessage>(OnVolume);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        _boxes.Clear();
        var query = EntityQueryEnumerator<WFBoomboxComponent>();
        while (query.MoveNext(out var uid, out var box))
        {
            if (box.Playing != null || box.Links.Count > 0)
                _boxes.Add(uid);
        }

        foreach (var uid in _boxes)
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out WFBoomboxComponent? box))
                continue;

            if (_internetSound.IsPlayingFrom(uid))
            {
                RefreshTitle((uid, box));
                continue;
            }

            // Whatever was playing is over: it finished, failed or was stopped from elsewhere.
            Finish((uid, box));
            Advance((uid, box));
        }

        _boxes.Clear();
    }

    /// <summary>True when a new link would play at once rather than wait.</summary>
    public bool IsIdle(Entity<WFBoomboxComponent> ent) =>
        !_internetSound.IsPlayingFrom(ent) && ent.Comp.Links.Count == 0 && ent.Comp.Playing == null;

    /// <summary>
    /// Plays a link at once on a quiet box, otherwise queues it. A playlist link is read first and adds its
    /// playable tracks. Error is a loc string for the requester when this returns false.
    /// </summary>
    public bool TryAdd(Entity<WFBoomboxComponent> ent, string url, string requester, ICommonSession? session, out string? error)
    {
        error = null;

        if (!_cfg.GetCVar(InternetSoundCVars.PlayerRequests))
        {
            error = Loc.GetString("wf-boombox-disabled");
            return false;
        }

        url = url.Trim();
        if (url.Length == 0 || url.Length > ent.Comp.MaxUrlLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            error = Loc.GetString("wf-internet-sound-invalid-url");
            return false;
        }

        var idle = IsIdle(ent);
        if (idle && _timing.CurTime < ent.Comp.NextRequest)
        {
            var seconds = (int) Math.Ceiling((ent.Comp.NextRequest - _timing.CurTime).TotalSeconds);
            error = Loc.GetString("wf-boombox-cooldown", ("seconds", seconds));
            return false;
        }

        var room = WFBoomboxComponent.MaxQueued - ent.Comp.Links.Count + (idle ? 1 : 0);
        if (room <= 0)
        {
            error = Loc.GetString("wf-boombox-full");
            return false;
        }

        if (InternetSoundDownloader.IsPlaylistLink(uri))
            return TryReadPlaylist(ent, uri.AbsoluteUri, Math.Min(room, WFBoomboxComponent.MaxQueued), requester, session, out error);

        var link = NewLink(uri.AbsoluteUri, ShipPaPlaylistText.ShortenLink(uri.AbsoluteUri), 0, requester, session);

        if (!idle)
        {
            ent.Comp.Links.Add(link);
            Sync(ent);
            return true;
        }

        if (!Start(ent, link, out error))
            return false;

        ent.Comp.NextRequest = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(InternetSoundCVars.RequestCooldown));
        return true;
    }

    private bool TryReadPlaylist(Entity<WFBoomboxComponent> ent, string url, int max, string requester, ICommonSession? session, out string? error)
    {
        error = null;
        if (ent.Comp.Reading)
        {
            error = Loc.GetString("wf-boombox-reading");
            return false;
        }

        ent.Comp.Reading = true;
        Dirty(ent);

        var uid = ent.Owner;
        _internetSound.ExpandPlaylist(url, max, (result, failure) =>
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out WFBoomboxComponent? box))
                return;

            box.Reading = false;
            Entity<WFBoomboxComponent> target = (uid, box);

            if (result == null)
            {
                Tell(session, failure ?? Loc.GetString("wf-boombox-refused"));
                Sync(target, true);
                return;
            }

            var room = WFBoomboxComponent.MaxQueued - box.Links.Count + (IsIdle(target) ? 1 : 0);
            var added = 0;
            foreach (var entry in result.Entries)
            {
                if (added >= room)
                    break;

                box.Links.Add(NewLink(entry.Url, ShipPaPlaylistText.Label(entry.Title), entry.Seconds, requester, session));
                added++;
            }

            Tell(session, Loc.GetString("wf-boombox-playlist-added",
                ("count", added), ("skipped", result.Skipped + result.Entries.Count - added)));

            if (added > 0 && box.Playing == null && !_internetSound.IsPlayingFrom(uid))
                box.NextRequest = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(InternetSoundCVars.RequestCooldown));

            Advance(target);
            Sync(target, true);
        });

        return true;
    }

    private WFBoomboxLink NewLink(string url, string label, int seconds, string requester, ICommonSession? session)
    {
        return new WFBoomboxLink
        {
            Id = _nextId++,
            Url = url,
            Label = label,
            Seconds = seconds,
            Requester = requester,
            Session = session,
        };
    }

    private bool Start(Entity<WFBoomboxComponent> ent, WFBoomboxLink link, out string? error)
    {
        error = null;
        if (!TryComp(ent, out InternetSoundSourceComponent? source)
            || !_internetSound.PlayFromSource(link.Requester, link.Url, (ent, source), out error, link.Session))
            return false;

        ent.Comp.Playing = link;
        Sync(ent);
        return true;
    }

    /// <summary>The playing track is done; with repeat on it goes back to the end of the queue.</summary>
    private void Finish(Entity<WFBoomboxComponent> ent)
    {
        if (ent.Comp.Playing is not { } done)
            return;

        ent.Comp.Playing = null;
        if (ent.Comp.Repeat && ent.Comp.Links.Count < WFBoomboxComponent.MaxQueued)
            ent.Comp.Links.Add(done);
    }

    /// <summary>
    /// Starts the first waiting link that will play. A refused link is dropped and its requester told why;
    /// at the server's track cap the queue waits.
    /// </summary>
    private void Advance(Entity<WFBoomboxComponent> ent)
    {
        while (ent.Comp.Playing == null && ent.Comp.Links.Count > 0
               && !_internetSound.IsPlayingFrom(ent) && _internetSound.HasFreeSlot(ent))
        {
            var next = ent.Comp.Links[0];
            ent.Comp.Links.RemoveAt(0);

            string? error = null;
            if (!_cfg.GetCVar(InternetSoundCVars.PlayerRequests))
                error = Loc.GetString("wf-boombox-disabled");
            else if (Start(ent, next, out error))
                break;

            Log.Info($"Dropped boombox link from {next.Requester}: {error}");
            Tell(next.Session, error ?? Loc.GetString("wf-boombox-refused"));
        }

        Sync(ent);
    }

    private void Tell(ICommonSession? session, string message)
    {
        if (session is { Status: SessionStatus.InGame })
            _popup.PopupCursor(message, session, PopupType.Medium);
    }

    private void RefreshTitle(Entity<WFBoomboxComponent> ent)
    {
        if (ent.Comp.Playing is not { } playing
            || !TryComp(ent, out InternetSoundSourceComponent? source)
            || string.IsNullOrWhiteSpace(source.Title))
            return;

        var label = ShipPaPlaylistText.Label(source.Title);
        var seconds = (int) Math.Ceiling(source.Length);
        if (label == playing.Label && seconds == playing.Seconds)
            return;

        playing.Label = label;
        playing.Seconds = seconds;
        Sync(ent);
    }

    /// <summary>Copies the server's lists into the networked labels.</summary>
    private void Sync(Entity<WFBoomboxComponent> ent, bool force = false)
    {
        WFBoomboxEntry? current = ent.Comp.Playing is { } playing
            ? new WFBoomboxEntry(playing.Id, playing.Label, playing.Seconds)
            : null;
        var queue = ent.Comp.Links.Select(link => new WFBoomboxEntry(link.Id, link.Label, link.Seconds)).ToList();

        if (!force && current == ent.Comp.Current && queue.SequenceEqual(ent.Comp.Queue))
            return;

        ent.Comp.Current = current;
        ent.Comp.Queue = queue;
        Dirty(ent);
    }

    private void OnAdd(Entity<WFBoomboxComponent> ent, ref WFBoomboxAddMessage args)
    {
        _players.TryGetSessionByEntity(args.Actor, out var session);
        if (!TryAdd(ent, args.Url, Name(args.Actor), session, out var error))
            _popup.PopupEntity(error ?? Loc.GetString("wf-boombox-refused"), ent, args.Actor);
    }

    private void OnSkip(Entity<WFBoomboxComponent> ent, ref WFBoomboxSkipMessage args)
    {
        if (_timing.CurTime < ent.Comp.NextSkip)
            return;

        ent.Comp.NextSkip = _timing.CurTime + SkipCooldown;
        _internetSound.StopForSource(ent);
        Finish(ent);
        Advance(ent);
    }

    private void OnStop(Entity<WFBoomboxComponent> ent, ref WFBoomboxStopMessage args)
    {
        Stop(ent);
    }

    /// <summary>Ends the playing track and empties the queue.</summary>
    public void Stop(Entity<WFBoomboxComponent> ent)
    {
        ent.Comp.Links.Clear();
        ent.Comp.Playing = null;
        _internetSound.StopForSource(ent);
        Sync(ent);
    }

    private void OnClear(Entity<WFBoomboxComponent> ent, ref WFBoomboxClearMessage args)
    {
        ent.Comp.Links.Clear();
        Sync(ent);
    }

    private void OnShuffle(Entity<WFBoomboxComponent> ent, ref WFBoomboxShuffleMessage args)
    {
        _random.Shuffle(ent.Comp.Links);
        Sync(ent);
    }

    private void OnRepeat(Entity<WFBoomboxComponent> ent, ref WFBoomboxRepeatMessage args)
    {
        ent.Comp.Repeat = args.Repeat;
        Dirty(ent);
    }

    private void OnRemove(Entity<WFBoomboxComponent> ent, ref WFBoomboxRemoveMessage args)
    {
        var id = args.Id;
        if (ent.Comp.Links.RemoveAll(link => link.Id == id) > 0)
            Sync(ent);
    }

    private void OnMoveUp(Entity<WFBoomboxComponent> ent, ref WFBoomboxMoveUpMessage args)
    {
        var id = args.Id;
        var index = ent.Comp.Links.FindIndex(link => link.Id == id);
        if (index <= 0)
            return;

        (ent.Comp.Links[index - 1], ent.Comp.Links[index]) = (ent.Comp.Links[index], ent.Comp.Links[index - 1]);
        Sync(ent);
    }

    private void OnVolume(Entity<WFBoomboxComponent> ent, ref WFBoomboxVolumeMessage args)
    {
        if (!float.IsFinite(args.Volume) || !TryComp(ent, out InternetSoundSourceComponent? source))
            return;

        var gain = Math.Clamp(args.Volume, 0f, 1f);
        source.Volume = gain < MinGain ? -80f : SharedAudioSystem.GainToVolume(gain);
        Dirty(ent, source);
    }

    private void OnGetVerbs(Entity<WFBoomboxComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || IsIdle(ent))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-boombox-stop"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/close.svg.192dpi.png")),
            Act = () => Stop(ent),
        });
    }

    private void OnExamined(Entity<WFBoomboxComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (TryComp(ent, out InternetSoundSourceComponent? source) && source.IsPlaying(_timing.CurTime) && source.Title != null)
            args.PushMarkup(Loc.GetString("wf-boombox-examine-playing", ("title", FormattedMessage.EscapeText(source.Title))));
        else if (_internetSound.IsPlayingFrom(ent))
            args.PushMarkup(Loc.GetString("wf-boombox-examine-fetching"));
    }

    private void OnSourceChanged(Entity<WFBoomboxComponent> ent, ref InternetSoundSourceChangedEvent args)
    {
        _appearance.SetData(ent, WFBoomboxVisuals.Playing, args.Playing);
    }

    private void OnShutdown(Entity<WFBoomboxComponent> ent, ref ComponentShutdown args)
    {
        _internetSound.StopForSource(ent);
    }
}
