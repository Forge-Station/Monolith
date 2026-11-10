using Content.Server.Chat.Systems;
using Content.Shared._WF.ShipPa;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipPa;

/// <summary>
/// Runs a ship's public address network: every speaker anchored to a grid carries that grid's
/// broadcasts, announcements and alarms. Membership is the grid the speaker sits on, so there is
/// nothing to wire up.
/// </summary>
public sealed partial class ShipPaSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private ChatSystem _chat = default!;

    /// <summary>How often expired broadcasts and queued speaker counts are cleaned up.</summary>
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.25);

    private static readonly SoundSpecifier DefaultChime = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");

    /// <summary>Grids whose speaker counts changed this tick, refreshed in bulk so power flicker is cheap.</summary>
    private readonly HashSet<EntityUid> _pendingCounts = new();

    /// <summary>Reused when updating all speakers' broadcast indicators.</summary>
    private readonly List<Entity<ShipPaSpeakerComponent>> _speakerBuffer = new();

    private TimeSpan _nextUpdate;
    private int _nextBroadcastId = 1;

    public override void Initialize()
    {
        base.Initialize();

        InitializeSpeakers();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        ClearExpiredBroadcasts();
        FlushPendingCounts();
        UpdateBroadcasts();
    }

    /// <summary>One ship timeline, rendered independently by each listener.</summary>
    public int? Broadcast(EntityUid grid, SoundSpecifier sound, AudioParams? audioParams = null,
        int priority = ShipPaPlaybackPolicy.AnnouncementPriority, string key = "announcement")
    {
        if (!Exists(grid) || CountSpeakers(grid).Online == 0)
            return null;

        return StartBroadcast(grid, key, sound, false, ShipPaBroadcastKind.Announcement, priority, audioParams)?.Id;
    }

    /// <summary>Chimes the PA and sends a text announcement to everyone in range of a speaker; false if no speaker is online or the chime can't play.</summary>
    public bool Announce(EntityUid grid, string message, SoundSpecifier? sound = null, string? sender = null, Color? color = null)
    {
        if (!Exists(grid) || CountSpeakers(grid).Online == 0)
            return false;

        var chime = sound ?? CompOrNull<ShipAlertComponent>(grid)?.AnnouncementChime ?? DefaultChime;
        if (StartBroadcast(grid, "announcement", chime, false, ShipPaBroadcastKind.Announcement,
                ShipPaPlaybackPolicy.AnnouncementPriority, caption: message, color: color) == null)
            return false;

        // One history entry per listener; no per-speaker bubbles or periodic re-announcements.
        _chat.DispatchFilteredAnnouncement(GetListeners(grid), message, source: grid,
            sender: sender ?? GetShipName(grid), playSound: false, announcementSound: null, colorOverride: color);
        return true;
    }

    /// <summary>Legacy callers get one subtitle per listener instead of a bubble at every speaker.</summary>
    public void Bubble(EntityUid grid, string message, Color? color = null)
    {
        if (!Exists(grid))
            return;

        var state = EnsureComp<ShipPaBroadcastComponent>(grid);
        state.Broadcasts.RemoveAll(b => b.Key == "caption");
        state.Broadcasts.Add(new ShipPaBroadcast
        {
            Id = NextBroadcastId(), Key = "caption", Start = _timing.CurTime,
            Caption = message, Color = color ?? Color.White, Kind = ShipPaBroadcastKind.Announcement, Priority = 30,
            RetainUntil = _timing.CurTime + TimeSpan.FromSeconds(8),
        });
        Dirty(grid, state);
    }

    /// <summary>
    /// Players whose attached entity is within Range of a working speaker on the grid (same map).
    /// </summary>
    public Filter GetListeners(EntityUid grid)
    {
        var filter = Filter.Empty();

        foreach (var speaker in GetSpeakers(grid))
        {
            if (IsFunctional(speaker))
                filter.AddInRange(_xform.GetMapCoordinates(speaker.Owner), speaker.Comp.Range);
        }

        return filter;
    }

    /// <summary>
    /// Every speaker anchored to the grid, working or not.
    /// </summary>
    public List<Entity<ShipPaSpeakerComponent>> GetSpeakers(EntityUid grid)
    {
        var speakers = new List<Entity<ShipPaSpeakerComponent>>();
        GatherSpeakers(grid, speakers);
        return speakers;
    }

    /// <summary>
    /// Anchored, on a grid, not broken, and powered (or has no ApcPowerReceiver).
    /// </summary>
    public bool IsFunctional(Entity<ShipPaSpeakerComponent> speaker)
    {
        if (speaker.Comp.Broken || TerminatingOrDeleted(speaker.Owner))
            return false;

        if (!TryComp(speaker.Owner, out TransformComponent? xform) || !xform.Anchored || xform.GridUid == null)
            return false;

        return IsSpeakerPowered(speaker.Owner);
    }

    /// <summary>
    /// Counts the speakers carrying the ship's PA. Fallback is set when those are air alarms standing in.
    /// </summary>
    public (int Online, int Total, bool Fallback) CountSpeakers(EntityUid grid)
    {
        var online = 0;
        var total = 0;
        var fallback = false;

        foreach (var speaker in GetSpeakers(grid))
        {
            total++;
            fallback |= speaker.Comp.Fallback;

            if (IsFunctional(speaker))
                online++;
        }

        return (online, total, fallback);
    }

    /// <summary>
    /// Recounts speakers into the grid's ShipAlertComponent and dirties it if changed.
    /// </summary>
    public void RefreshCounts(EntityUid grid)
    {
        if (!Exists(grid) || TerminatingOrDeleted(grid))
            return;

        RefreshCoverage(grid);
        if (TryComp(grid, out ShipPaBroadcastComponent? broadcast))
            UpdateBroadcastLights(grid, broadcast);
        var (online, total, fallback) = CountSpeakers(grid);

        if (!TryComp(grid, out ShipAlertComponent? alert))
        {
            // Every station has air alarms; only a dedicated speaker makes a grid worth tracking on its
            // own. Ships with just air alarms get the component when their console is first opened.
            if (total == 0 || fallback)
                return;

            alert = EnsureComp<ShipAlertComponent>(grid);
        }

        if (alert.SpeakersOnline == online && alert.SpeakersTotal == total && alert.SpeakersFallback == fallback)
            return;

        alert.SpeakersOnline = online;
        alert.SpeakersTotal = total;
        alert.SpeakersFallback = fallback;
        Dirty(grid, alert);
    }

    /// <summary>
    /// The ship's name for announcements, or a stand-in for unnamed hulls.
    /// </summary>
    public string GetShipName(EntityUid grid)
    {
        if (TryComp(grid, out MetaDataComponent? meta) && !string.IsNullOrWhiteSpace(meta.EntityName))
            return meta.EntityName;

        return Loc.GetString("ship-pa-unknown-ship");
    }

    /// <summary>
    /// Fills the list with the speakers carrying the grid's PA: every dedicated speaker anchored to it,
    /// or, when there is none at all, its fallback units (air alarms). Membership is indexed by grid.
    /// </summary>
    private void GatherSpeakers(EntityUid grid, List<Entity<ShipPaSpeakerComponent>> into)
    {
        if (!Exists(grid))
            return;

        var first = into.Count;
        var dedicated = false;
        if (!_gridSpeakers.TryGetValue(grid, out var members))
            return;

        foreach (var uid in members)
        {
            if (!TryComp(uid, out ShipPaSpeakerComponent? speaker) || TerminatingOrDeleted(uid)
                || !TryComp(uid, out TransformComponent? xform) || !xform.Anchored || xform.GridUid != grid)
                continue;

            dedicated |= !speaker.Fallback;
            into.Add((uid, speaker));
        }

        if (!dedicated)
            return;

        for (var i = into.Count - 1; i >= first; i--)
        {
            if (into[i].Comp.Fallback)
                into.RemoveAt(i);
        }
    }

    /// <summary>
    /// Replicate power and fallback membership, including dedicated speakers outside the client's PVS.
    /// </summary>
    private void RefreshCoverage(EntityUid grid)
    {
        if (!_gridSpeakers.TryGetValue(grid, out var members))
            return;
        var dedicated = false;
        foreach (var uid in members)
        {
            if (TryComp(uid, out ShipPaSpeakerComponent? speaker) && !speaker.Fallback && !TerminatingOrDeleted(uid))
                dedicated = true;
        }

        foreach (var uid in members)
        {
            if (!TryComp(uid, out ShipPaSpeakerComponent? speaker) || TerminatingOrDeleted(uid))
                continue;
            var enabled = (!dedicated || !speaker.Fallback) && IsFunctional((uid, speaker));
            if (speaker.Enabled == enabled)
                continue;
            speaker.Enabled = enabled;
            Dirty(uid, speaker);
        }
    }

    private int NextBroadcastId()
    {
        return _nextBroadcastId++;
    }

    /// <summary>
    /// Queues a speaker recount so a burst of power or damage events only costs one sweep.
    /// </summary>
    private void QueueRefresh(EntityUid? grid)
    {
        if (grid is { } uid && uid.IsValid())
            _pendingCounts.Add(uid);
    }

    private void FlushPendingCounts()
    {
        if (_pendingCounts.Count == 0)
            return;

        foreach (var grid in _pendingCounts)
        {
            RefreshCounts(grid);
        }

        _pendingCounts.Clear();
    }
}
