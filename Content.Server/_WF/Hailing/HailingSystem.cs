using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Power.EntitySystems;
using Content.Server._WF.ShipPa;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Detection;
using Content.Shared._WF.Hailing;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Players.RateLimiting;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Hailing;

/// <summary>
/// Ship-to-ship hailing from the shuttle console. One ship rings another's consoles and pages its PA, and
/// whoever answers gets the caller on a screen with a chat line between the two helms.
/// </summary>
public sealed class HailingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private DetectionSystem _detection = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    /// <summary>Lines of a call each console keeps.</summary>
    public const int MaxLog = 60;

    /// <summary>A call holds this much further out than a hail can start.</summary>
    private const float DropRangeFactor = 1.25f;

    private static readonly TimeSpan RingInterval = TimeSpan.FromSeconds(3);

    /// <summary>God's ring is a seven second choir, so it comes round less often.</summary>
    private static readonly TimeSpan DivineRingInterval = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan RehailDelay = TimeSpan.FromSeconds(2);
    private static readonly Color PaColor = Color.FromHex("#ffbf40");

    /// <summary>A ship's PA announces hails this far apart at most, however often it is rung.</summary>
    private static readonly TimeSpan PageCooldown = TimeSpan.FromSeconds(30);

    private static readonly SoundSpecifier RingSound = new SoundPathSpecifier("/Audio/_WF/Hailing/hail_ring.ogg");
    private static readonly SoundSpecifier CallingSound = new SoundPathSpecifier("/Audio/_WF/Hailing/hail_calling.ogg");
    private static readonly SoundSpecifier OpenSound = new SoundPathSpecifier("/Audio/_WF/Hailing/hail_open.ogg");
    private static readonly SoundSpecifier CloseSound = new SoundPathSpecifier("/Audio/_WF/Hailing/hail_close.ogg");
    private static readonly SoundSpecifier MessageSound = new SoundPathSpecifier("/Audio/_WF/Hailing/hail_message.ogg");
    private static readonly SoundSpecifier DivineSound = new SoundPathSpecifier("/Audio/_WF/Hailing/hail_god.ogg");
    private static readonly Color DivineColor = Color.FromHex("#ffe28a");

    private float _range;
    private float _ringTimeout;
    private float _retryDelay;
    private TimeSpan _nextCheck;

    /// <summary>When each console may hail again.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextHail = new();

    /// <summary>When each ship's PA may announce a hail again.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextPage = new();

    private readonly List<(EntityUid Console, HailEndReason Reason, HailEndReason PeerReason)> _ending = new();
    private readonly List<EntityUid> _consoles = new();
    private readonly List<EntityUid> _expired = new();
    private readonly HashSet<EntityUid> _feed = new();
    private readonly Dictionary<EntityUid, bool> _ships = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, HailingCVars.Range, value => _range = value, true);
        Subs.CVar(_cfg, HailingCVars.RingTimeout, value => _ringTimeout = value, true);
        Subs.CVar(_cfg, HailingCVars.RetryDelay, value => _retryDelay = value, true);

        SubscribeLocalEvent<HailConsoleComponent, ComponentShutdown>(OnShutdown);

        // ShuttleConsoleSystem owns the open and close subscriptions for these consoles; only new message
        // types may be added here.
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<HailTargetsRequestMessage>(OnTargetsRequest);
            subs.Event<HailRequestMessage>(OnHailRequest);
            subs.Event<HailAnswerMessage>(OnAnswer);
            subs.Event<HailEndMessage>(OnEnd);
            subs.Event<HailSayMessage>(OnSay);
        });
    }

    private void OnShutdown(Entity<HailConsoleComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Viewer is not { } viewer)
            return;

        foreach (var uid in ent.Comp.Sent)
        {
            _pvs.RemoveForceSend(uid, viewer);
        }

        ent.Comp.Sent.Clear();
    }

    private void OnTargetsRequest(Entity<ShuttleConsoleComponent> ent, ref HailTargetsRequestMessage args)
    {
        _ui.ServerSendUiMessage(ent.Owner, ShuttleConsoleUiKey.Key, new HailTargetsMessage(GetTargets(ent)), args.Actor);
    }

    private void OnHailRequest(Entity<ShuttleConsoleComponent> ent, ref HailRequestMessage args)
    {
        if (TryGetEntity(args.Ship, out var ship))
            TryHail(ent, args.Actor, ship.Value);
    }

    private void OnAnswer(Entity<ShuttleConsoleComponent> ent, ref HailAnswerMessage args)
    {
        Answer(ent, args.Actor, args.Accept);
    }

    private void OnEnd(Entity<ShuttleConsoleComponent> ent, ref HailEndMessage args)
    {
        if (!TryComp<HailConsoleComponent>(ent, out var hail))
            return;

        // Anyone at a ringing console may decline, as anyone may answer. A hail or call under way belongs to
        // whoever is on it.
        if (hail.State == HailState.Ringing)
            Answer(ent, args.Actor, false);
        else if (hail.OperatorUid == args.Actor)
            End(ent);
    }

    private void OnSay(Entity<ShuttleConsoleComponent> ent, ref HailSayMessage args)
    {
        Say(ent, args.Actor, args.Text);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var check = now >= _nextCheck;

        if (check)
            _nextCheck = now + CheckInterval;

        var query = EntityQueryEnumerator<HailConsoleComponent>();

        while (query.MoveNext(out var uid, out var hail))
        {
            if (check && Failure(uid, hail, now) is { } failure)
            {
                _ending.Add((uid, failure.Reason, failure.PeerReason));
                continue;
            }

            if (check && hail.State == HailState.Connected)
                RefreshFeed(hail);

            // An admin's stand-in is nowhere, so there is nobody for it to ring at.
            if (hail.State == HailState.Connected || hail.Remote || now < hail.NextRing)
                continue;

            if (hail.State == HailState.Calling)
            {
                hail.NextRing = now + RingInterval;
                _audio.PlayPvs(CallingSound, uid);
                continue;
            }

            hail.NextRing = now + (hail.Divine ? DivineRingInterval : RingInterval);
            _audio.PlayPvs(hail.Divine ? DivineSound : RingSound, uid);
        }

        foreach (var (uid, reason, peerReason) in _ending)
        {
            End(uid, reason, peerReason);
        }

        _ending.Clear();

        if (!check)
            return;

        foreach (var (console, time) in _nextHail)
        {
            if (now >= time)
                _expired.Add(console);
        }

        foreach (var console in _expired)
        {
            _nextHail.Remove(console);
        }

        _expired.Clear();

        foreach (var (ship, time) in _nextPage)
        {
            if (now >= time)
                _expired.Add(ship);
        }

        foreach (var ship in _expired)
        {
            _nextPage.Remove(ship);
        }

        _expired.Clear();
    }

    /// <summary>
    /// What has gone wrong with a console's hail or call, if anything.
    /// </summary>
    private (HailEndReason Reason, HailEndReason PeerReason)? Failure(EntityUid uid, HailConsoleComponent hail, TimeSpan now)
    {
        EntityUid grid = default;

        if (!hail.Remote && !Usable(uid, out grid))
            return (HailEndReason.Lost, HailEndReason.Lost);

        switch (hail.State)
        {
            case HailState.Calling:
                if (!OperatorPresent(uid, hail))
                    return (HailEndReason.Closed, HailEndReason.PeerClosed);

                if (now > hail.StateTime + TimeSpan.FromSeconds(_ringTimeout))
                    return (HailEndReason.NoAnswer, HailEndReason.PeerClosed);

                if (hail.PeerGrid is not { } hailed || TerminatingOrDeleted(hailed) ||
                    !hail.Remote && !CanReach(uid, grid, hailed, _range * DropRangeFactor, out _))
                {
                    return (HailEndReason.Unavailable, HailEndReason.PeerClosed);
                }

                break;

            case HailState.Ringing:
                // The caller's own checks end the hail; this only catches a caller that vanished outright.
                if (!TryComp<HailConsoleComponent>(hail.Peer, out var caller) || caller.State != HailState.Calling)
                    return (HailEndReason.Lost, HailEndReason.Lost);

                break;

            case HailState.Connected:
                if (!OperatorPresent(uid, hail))
                    return (HailEndReason.Closed, HailEndReason.PeerClosed);

                if (!TryComp<HailConsoleComponent>(hail.Peer, out var peer) || peer.Peer != uid)
                    return (HailEndReason.Lost, HailEndReason.Lost);

                // Range only means something between two ships.
                if (!hail.Remote && !peer.Remote &&
                    (hail.PeerGrid is not { } other || !InRange(grid, other, _range * DropRangeFactor, out _)))
                {
                    return (HailEndReason.Lost, HailEndReason.Lost);
                }

                break;
        }

        return null;
    }

    /// <summary>
    /// The ships a console can hail right now, nearest first.
    /// </summary>
    public List<HailTarget> GetTargets(EntityUid console)
    {
        var targets = new List<HailTarget>();

        if (!Usable(console, out var grid))
            return targets;

        _ships.Clear();
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } ship || ship == grid || !this.IsPowered(uid, EntityManager))
                continue;

            _ships[ship] = _ships.GetValueOrDefault(ship) || !HasComp<HailConsoleComponent>(uid);
        }

        foreach (var (ship, free) in _ships)
        {
            if (!CanReach(console, grid, ship, _range, out var distance))
                continue;

            targets.Add(new HailTarget
            {
                Ship = GetNetEntity(ship),
                Name = ShipLabel(console, ship),
                Distance = distance,
                Busy = !free,
            });
        }

        targets.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return targets;
    }

    /// <summary>
    /// Whether a ship has a powered console that is free to ring.
    /// </summary>
    public bool HasFreeConsole(EntityUid ship)
    {
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == ship && this.IsPowered(uid, EntityManager) && !HasComp<HailConsoleComponent>(uid))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Lets a console hail a ship again at once, PA announcement included. For test callers.
    /// </summary>
    public void ClearCooldowns(EntityUid console, EntityUid ship)
    {
        _nextHail.Remove(console);
        _nextPage.Remove(ship);
    }

    /// <summary>
    /// Hails a ship from a console, ringing every free console aboard it. False if the hail never started.
    /// </summary>
    public bool TryHail(EntityUid console, EntityUid user, EntityUid ship)
    {
        if (HasComp<HailConsoleComponent>(console) || !Usable(console, out var grid))
            return false;

        var now = _timing.CurTime;

        if (_nextHail.TryGetValue(console, out var next) && now < next)
            return Refuse(console, HailEndReason.Cooldown);

        if (!CanReach(console, grid, ship, _range, out _))
            return Refuse(console, HailEndReason.Unavailable);

        _consoles.Clear();
        var busy = false;
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != ship || !this.IsPowered(uid, EntityManager))
                continue;

            if (HasComp<HailConsoleComponent>(uid))
                busy = true;
            else
                _consoles.Add(uid);
        }

        if (_consoles.Count == 0)
            return Refuse(console, busy ? HailEndReason.Busy : HailEndReason.Unavailable);

        var hail = AddComp<HailConsoleComponent>(console);
        hail.State = HailState.Calling;
        hail.PeerShip = ShipLabel(console, ship);
        hail.PeerGrid = ship;
        hail.OperatorUid = user;
        hail.Operator = GetNetEntity(user);
        hail.StateTime = now;
        hail.NextRing = now;
        Dirty(console, hail);

        // The hailed ship hears it over its PA, so someone can get to the helm. The PA carries the first
        // ring; with no speaker working the consoles ring at once instead.
        var paged = (!_nextPage.TryGetValue(ship, out var nextPage) || now >= nextPage) &&
                    _pa.Announce(ship,
                        Loc.GetString("hail-pa-incoming", ("ship", ShipLabel(_consoles[0], grid))),
                        RingSound,
                        Loc.GetString("hail-pa-sender", ("ship", _pa.GetShipName(ship))),
                        PaColor);

        if (paged)
            _nextPage[ship] = now + PageCooldown;

        foreach (var target in _consoles)
        {
            var ring = AddComp<HailConsoleComponent>(target);
            ring.State = HailState.Ringing;
            ring.PeerShip = ShipLabel(target, grid);
            ring.Peer = console;
            ring.PeerGrid = grid;
            ring.StateTime = now;
            ring.NextRing = paged ? now + RingInterval : now;
            Dirty(target, ring);
        }

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} hailed {ToPrettyString(ship):grid} from {ToPrettyString(grid):grid}");

        return true;
    }

    /// <summary>
    /// Hails a ship as God, from no ship at all: an admin's stand-in entity takes the caller's end, and the
    /// ship rings with a choir instead of the chime. Returns the stand-in, or null if no console could ring.
    /// </summary>
    public EntityUid? HailAsGod(EntityUid user, ICommonSession session, EntityUid ship)
    {
        if (TerminatingOrDeleted(ship))
            return null;

        _consoles.Clear();
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == ship && this.IsPowered(uid, EntityManager) && !HasComp<HailConsoleComponent>(uid))
                _consoles.Add(uid);
        }

        if (_consoles.Count == 0)
            return null;

        var now = _timing.CurTime;
        var god = Loc.GetString("hail-god-name");
        var endpoint = Spawn(null, MapCoordinates.Nullspace);
        _meta.SetEntityName(endpoint, god);

        var hail = AddComp<HailConsoleComponent>(endpoint);
        hail.Remote = true;
        hail.State = HailState.Calling;
        hail.PeerShip = Name(ship);
        hail.PeerGrid = ship;
        hail.OperatorUid = user;
        hail.Operator = GetNetEntity(user);
        hail.StateTime = now;
        Dirty(endpoint, hail);

        // The stand-in is nowhere, and the admin's window reads the hail off it.
        _pvs.AddSessionOverride(endpoint, session);

        var paged = _pa.Announce(ship, Loc.GetString("hail-pa-god", ("ship", Name(ship))), DivineSound,
            Loc.GetString("hail-pa-sender", ("ship", _pa.GetShipName(ship))), DivineColor);

        foreach (var target in _consoles)
        {
            var ring = AddComp<HailConsoleComponent>(target);
            ring.State = HailState.Ringing;
            ring.Divine = true;
            ring.PeerShip = god;
            ring.Peer = endpoint;
            ring.StateTime = now;
            ring.NextRing = paged ? now + DivineRingInterval : now;
            Dirty(target, ring);
        }

        _adminLogger.Add(LogType.AdminMessage, LogImpact.Medium,
            $"{ToPrettyString(user):player} hailed {ToPrettyString(ship):grid} as God");

        return endpoint;
    }

    /// <summary>
    /// Answers or declines the hail ringing at a console. Declining turns the caller away from the whole ship.
    /// </summary>
    public void Answer(EntityUid console, EntityUid user, bool accept)
    {
        if (!TryComp<HailConsoleComponent>(console, out var hail) || hail.State != HailState.Ringing ||
            hail.Peer is not { } caller)
        {
            return;
        }

        if (!TryComp<HailConsoleComponent>(caller, out var callerHail) || callerHail.State != HailState.Calling)
        {
            RemComp<HailConsoleComponent>(console);
            return;
        }

        if (!accept)
        {
            End(caller, HailEndReason.Declined);
            return;
        }

        StopRinging(caller, console);

        var callerOperator = callerHail.OperatorUid;
        Open((console, hail), user, caller, callerOperator);
        Open((caller, callerHail), callerOperator, console, user);

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} on {ToPrettyString(callerHail.PeerGrid):grid} answered a hail from {ToPrettyString(callerOperator):player} on {ToPrettyString(hail.PeerGrid):grid}");
    }

    /// <summary>
    /// Stops whatever a console is doing: cancels its hail, closes its channel or stops it ringing.
    /// </summary>
    public void End(EntityUid console, HailEndReason reason = HailEndReason.Closed, HailEndReason peerReason = HailEndReason.PeerClosed)
    {
        if (!TryComp<HailConsoleComponent>(console, out var hail))
            return;

        switch (hail.State)
        {
            case HailState.Ringing:
                // The hail goes on for as long as another console aboard still rings.
                var caller = hail.Peer;
                Remove(console, hail, null);

                if (caller != null && TryComp<HailConsoleComponent>(caller, out var callerHail) &&
                    callerHail.State == HailState.Calling && !AnyRinging(caller.Value))
                {
                    End(caller.Value, HailEndReason.NoAnswer);
                }

                break;

            case HailState.Calling:
                StopRinging(console, null);
                Remove(console, hail, reason);
                break;

            case HailState.Connected:
                var peer = hail.Peer;
                Remove(console, hail, reason);

                if (peer != null && TryComp<HailConsoleComponent>(peer, out var peerHail) && peerHail.Peer == console)
                    Remove(peer.Value, peerHail, peerReason);

                break;
        }
    }

    /// <summary>
    /// Sends a line down a console's open channel. Only the person on the call can.
    /// </summary>
    public bool Say(EntityUid console, EntityUid user, string text)
    {
        if (!TryComp<HailConsoleComponent>(console, out var hail) || hail.State != HailState.Connected ||
            hail.OperatorUid != user || hail.Peer is not { } peer || !TryComp<HailConsoleComponent>(peer, out var peerHail))
        {
            return false;
        }

        if (TryComp<ActorComponent>(user, out var actor) &&
            _chat.HandleRateLimit(actor.PlayerSession) != RateLimitStatus.Allowed)
        {
            return false;
        }

        text = SharedChatSystem.SanitizeAnnouncement(text, HailConsoleComponent.MaxLineLength)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (text.Length == 0)
            return false;

        var name = hail.Remote ? Loc.GetString("hail-god-name") : Identity.Name(user, EntityManager);
        AddLine((console, hail), name, text, true);
        AddLine((peer, peerHail), name, text, false);
        _audio.PlayPvs(MessageSound, peer);

        _adminLogger.Add(LogType.Chat, LogImpact.Low,
            $"Hail from {ToPrettyString(user):player} on {ToPrettyString(peerHail.PeerGrid):grid} to {ToPrettyString(hail.PeerGrid):grid}: {text}");

        return true;
    }

    private void AddLine(Entity<HailConsoleComponent> ent, string sender, string text, bool own)
    {
        ent.Comp.Log.Add(new HailLine
        {
            Sequence = ent.Comp.NextLine++,
            Sender = sender,
            Text = text,
            Own = own,
        });

        if (ent.Comp.Log.Count > MaxLog)
            ent.Comp.Log.RemoveAt(0);

        Dirty(ent);
    }

    /// <summary>
    /// Puts one end of an answered hail on the call, and sends its operator the person they will see.
    /// </summary>
    private void Open(Entity<HailConsoleComponent> ent, EntityUid? user, EntityUid peer, EntityUid? peerUser)
    {
        var hail = ent.Comp;
        hail.State = HailState.Connected;
        hail.Peer = peer;
        hail.OperatorUid = user;
        hail.Operator = GetNetEntity(user);
        hail.StateTime = _timing.CurTime;
        hail.Log.Clear();
        hail.NextLine = 0;

        // God has no face: the admin behind the voice is never named, shown or sent to the pilot.
        hail.Divine = TryComp<HailConsoleComponent>(peer, out var peerHail) && peerHail.Remote;

        if (hail.Divine)
        {
            hail.PeerOperator = null;
            hail.PeerName = Loc.GetString("hail-god-name");
        }
        else
        {
            hail.PeerOperator = GetNetEntity(peerUser);
            hail.PeerName = peerUser is { } other ? Identity.Name(other, EntityManager) : string.Empty;
        }

        // The other pilot is far outside this one's view, and the screen draws the real person.
        if (!hail.Divine && peerUser != null && TryComp<ActorComponent>(user, out var actor))
        {
            hail.Viewer = actor.PlayerSession;
            hail.Viewed = peerUser;
            RefreshFeed(hail);
        }

        Dirty(ent);

        if (!hail.Remote)
            _audio.PlayPvs(OpenSound, ent);
    }

    /// <summary>
    /// Keeps the viewer sent the person on their screen and everything that person wears and carries, which
    /// is what their picture is drawn from. An ordinary override sends the children only as the entry budget
    /// allows and drops the rest of them for the tick whenever it runs out, so limbs and clothes came and
    /// went; each one is force-sent instead, and the set follows what they pick up and put down.
    /// </summary>
    private void RefreshFeed(HailConsoleComponent hail)
    {
        if (hail.Viewer is not { } viewer || hail.Viewed is not { } viewed)
            return;

        _feed.Clear();

        if (!TerminatingOrDeleted(viewed))
            CollectFeed(viewed);

        _expired.Clear();

        foreach (var uid in hail.Sent)
        {
            if (!_feed.Contains(uid))
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
        {
            hail.Sent.Remove(uid);
            _pvs.RemoveForceSend(uid, viewer);
        }

        _expired.Clear();

        foreach (var uid in _feed)
        {
            if (hail.Sent.Add(uid))
                _pvs.AddForceSend(uid, viewer);
        }
    }

    private void CollectFeed(EntityUid uid)
    {
        if (!_feed.Add(uid))
            return;

        var children = Transform(uid).ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            CollectFeed(child);
        }
    }

    private void Remove(EntityUid console, HailConsoleComponent hail, HailEndReason? reason)
    {
        if (hail.State == HailState.Calling)
        {
            var delay = reason is HailEndReason.Declined or HailEndReason.NoAnswer
                ? TimeSpan.FromSeconds(_retryDelay)
                : RehailDelay;

            _nextHail[console] = _timing.CurTime + delay;
        }

        if (hail.Remote)
        {
            // Whoever stands behind it hears about it their own way.
            var ended = new HailEndedEvent(reason, hail.PeerShip);
            RaiseLocalEvent(console, ref ended);
        }
        else
        {
            if (hail.State == HailState.Connected)
                _audio.PlayPvs(CloseSound, console);

            if (reason != null)
                _ui.ServerSendUiMessage(console, ShuttleConsoleUiKey.Key, new HailEndedMessage(reason.Value, hail.PeerShip));
        }

        RemComp<HailConsoleComponent>(console);
    }

    private bool Refuse(EntityUid console, HailEndReason reason)
    {
        _ui.ServerSendUiMessage(console, ShuttleConsoleUiKey.Key, new HailEndedMessage(reason, string.Empty));
        return false;
    }

    /// <summary>
    /// Stops every console a caller is ringing, apart from the one that answered.
    /// </summary>
    private void StopRinging(EntityUid caller, EntityUid? except)
    {
        _consoles.Clear();
        var query = EntityQueryEnumerator<HailConsoleComponent>();

        while (query.MoveNext(out var uid, out var hail))
        {
            if (uid != except && hail.State == HailState.Ringing && hail.Peer == caller)
                _consoles.Add(uid);
        }

        foreach (var uid in _consoles)
        {
            RemComp<HailConsoleComponent>(uid);
        }
    }

    private bool AnyRinging(EntityUid caller)
    {
        var query = EntityQueryEnumerator<HailConsoleComponent>();

        while (query.MoveNext(out var hail))
        {
            if (hail.State == HailState.Ringing && hail.Peer == caller)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The call lasts only while the person on it stays at the console.
    /// </summary>
    private bool OperatorPresent(EntityUid console, HailConsoleComponent hail)
    {
        // An admin's stand-in lasts for as long as their window does, whatever body they are in.
        if (hail.Remote)
            return true;

        return hail.OperatorUid is { } user && Exists(user) && _ui.IsUiOpen(console, ShuttleConsoleUiKey.Key, user);
    }

    private bool Usable(EntityUid console, out EntityUid grid)
    {
        grid = default;

        if (TerminatingOrDeleted(console) || Transform(console).GridUid is not { } found ||
            !this.IsPowered(console, EntityManager))
        {
            return false;
        }

        grid = found;
        return true;
    }

    /// <summary>
    /// Whether a console can raise a ship: in range and something its radar would show.
    /// </summary>
    private bool CanReach(EntityUid console, EntityUid grid, EntityUid ship, float range, out float distance)
    {
        distance = 0f;

        if (ship == grid || !InRange(grid, ship, range, out distance))
            return false;

        if (!TryComp<IFFComponent>(ship, out var iff))
            return true;

        if ((iff.Flags & IFFFlags.Hide) != 0)
            return false;

        return (iff.Flags & IFFFlags.HideLabel) == 0 ||
               _detection.IsGridDetected(ship, console) != DetectionLevel.Undetected;
    }

    private bool InRange(EntityUid grid, EntityUid ship, float range, out float distance)
    {
        distance = 0f;

        if (TerminatingOrDeleted(ship) || TerminatingOrDeleted(grid))
            return false;

        var map = Transform(grid).MapID;

        if (map == MapId.Nullspace || Transform(ship).MapID != map)
            return false;

        distance = (Center(ship) - Center(grid)).Length();
        return distance <= range;
    }

    private Vector2 Center(EntityUid grid)
    {
        var local = TryComp<MapGridComponent>(grid, out var comp) ? comp.LocalAABB.Center : Vector2.Zero;
        return Vector2.Transform(local, _transform.GetWorldMatrix(grid));
    }

    /// <summary>
    /// A ship's name as a console's radar would give it, so a ship running dark stays unnamed.
    /// </summary>
    private string ShipLabel(EntityUid console, EntityUid ship)
    {
        if (TryComp<IFFComponent>(ship, out var iff))
        {
            if ((iff.Flags & IFFFlags.HideLabelAlways) != 0)
                return Loc.GetString("hail-unknown-vessel");

            if ((iff.Flags & IFFFlags.HideLabel) != 0 &&
                _detection.IsGridDetected(ship, console) != DetectionLevel.Detected)
            {
                return Loc.GetString("hail-unknown-vessel");
            }
        }

        var name = Name(ship);
        return string.IsNullOrWhiteSpace(name) ? Loc.GetString("hail-unknown-vessel") : name;
    }
}
