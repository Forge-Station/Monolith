using System;
using Content.Server.Administration.Logs;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.Prototypes;
using Content.Shared._WF.ShipPa;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipPa;

/// <summary>
/// The pilot's end of the PA: situation codes, general quarters and free-text announcements, driven
/// from the shuttle console's ship overview.
/// </summary>
public sealed partial class ShipAlertSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private ShipPaPlaylistSystem _playlist = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    /// <summary>Alarm loop key for the general quarters klaxon.</summary>
    public const string GeneralQuartersAlarm = "general-quarters";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipAlertComponent, ComponentStartup>(OnAlertStartup);

        // This is the sole helm-open subscription; other modules share its notification below.
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnConsoleOpened);
            subs.Event<ShipAlertCodeRequestMessage>(OnCodeRequest);
            subs.Event<ShipGeneralQuartersRequestMessage>(OnGeneralQuartersRequest);
            subs.Event<ShipPaAnnounceRequestMessage>(OnAnnounceRequest);
            subs.Event<ShipPaInternetSoundRequestMessage>(OnInternetSoundRequest);
            subs.Event<ShipPaInternetSoundStopMessage>(OnInternetSoundStop);
            subs.Event<ShipPaPlaylistRequestMessage>(OnPlaylistRequest);
        });
    }

    /// <summary>Moves a grid saved before the WF id prefix onto the renamed code.</summary>
    private void OnAlertStartup(Entity<ShipAlertComponent> ent, ref ComponentStartup args)
    {
        var code = WFLegacyPrototypeIds.Resolve(WFLegacyPrototypeIds.ShipAlertCodes, ent.Comp.Code.Id);
        if (code != ent.Comp.Code.Id)
            ent.Comp.Code = code;
    }

    /// <summary>
    /// A ship running on its air alarms has nothing that creates its alert state, so the console does
    /// it on first use and the panel gets real counts.
    /// </summary>
    private void OnConsoleOpened(Entity<ShuttleConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        EntityManager.System<Content.Server.Shuttles.Systems.ShuttleConsoleSystem>().WfCockpitConsoleOpened(ent, args.Actor);
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        EnsureComp<ShipAlertComponent>(grid);
        _pa.RefreshCounts(grid);
    }

    private void OnCodeRequest(Entity<ShuttleConsoleComponent> ent, ref ShipAlertCodeRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        if (!_proto.TryIndex<ShipAlertCodePrototype>(args.Code, out var proto) || !proto.Selectable)
            return;

        SetCode(grid, args.Code, args.Actor);
        WarnIfUnheard(ent, grid, args.Actor);
    }

    private void OnGeneralQuartersRequest(Entity<ShuttleConsoleComponent> ent, ref ShipGeneralQuartersRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        SetGeneralQuarters(grid, args.Active, args.Actor);
        WarnIfUnheard(ent, grid, args.Actor);
    }

    private void OnAnnounceRequest(Entity<ShuttleConsoleComponent> ent, ref ShipPaAnnounceRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        var alert = EnsureComp<ShipAlertComponent>(grid);
        var text = Sanitize(args.Text, alert.MaxAnnouncementLength);

        if (string.IsNullOrWhiteSpace(text))
            return;

        if (_timing.CurTime < alert.NextAnnouncement)
        {
            _popup.PopupEntity(Loc.GetString("ship-pa-announce-cooldown"), ent, args.Actor);
            return;
        }

        var sender = Loc.GetString("ship-pa-sender", ("ship", _pa.GetShipName(grid)));

        if (!_pa.Announce(grid, text, alert.AnnouncementChime, sender))
        {
            _popup.PopupEntity(Loc.GetString("ship-pa-announce-no-speakers"), ent, args.Actor);
            return;
        }

        alert.NextAnnouncement = _timing.CurTime + alert.AnnouncementCooldown;

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} announced on the PA of {ToPrettyString(grid):grid}: {text}");
    }

    /// <summary>
    /// Pilot pasted a link to play over the ship's own speakers. Everything about the link itself is the
    /// sound system's problem; this only decides whether this ship is allowed to ask right now. The link plays
    /// at once if the ship is quiet and waits in the ship's queue otherwise.
    /// </summary>
    private void OnInternetSoundRequest(Entity<ShuttleConsoleComponent> ent, ref ShipPaInternetSoundRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        if (!_cfg.GetCVar(InternetSoundCVars.PlayerRequests))
        {
            _popup.PopupEntity(Loc.GetString("ship-pa-sound-disabled"), ent, args.Actor);
            return;
        }

        var alert = EnsureComp<ShipAlertComponent>(grid);
        var url = args.Url.Trim();

        if (url.Length == 0 || url.Length > alert.MaxUrlLength)
            return;

        // The cooldown paces starting a track. Adding to a running queue is free of it; the queue has its own cap.
        if (_playlist.IsIdle(grid) && _timing.CurTime < alert.NextInternetSound)
        {
            var seconds = (int) Math.Ceiling((alert.NextInternetSound - _timing.CurTime).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("ship-pa-sound-cooldown", ("seconds", seconds)), ent, args.Actor);
            return;
        }

        var requester = Name(args.Actor);

        _players.TryGetSessionByEntity(args.Actor, out var recipient);
        if (!_playlist.TryAdd(grid, url, requester, recipient, out var position, out var error))
        {
            _popup.PopupEntity(error ?? Loc.GetString("ship-pa-sound-refused"), ent, args.Actor);
            return;
        }

        if (position == 0)
            alert.NextInternetSound = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(InternetSoundCVars.RequestCooldown));
        _popup.PopupEntity(position == 0
            ? Loc.GetString("ship-pa-sound-queued")
            : Loc.GetString("ship-pa-playlist-added", ("position", position)), ent, args.Actor);

        // Players can put arbitrary audio on a ship, so this wants to be findable after the fact.
        var origin = GetSafeOrigin(url);
        if (position == 0)
        {
            _adminLogger.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(args.Actor):player} queued internet sound {origin} on the PA of {ToPrettyString(grid):grid}");
        }
        else
        {
            _adminLogger.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(args.Actor):player} added internet sound {origin} to place {position} of the PA playlist of {ToPrettyString(grid):grid}");
        }
    }

    private void OnInternetSoundStop(Entity<ShuttleConsoleComponent> ent, ref ShipPaInternetSoundStopMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid || !_playlist.Stop(grid))
            return;

        // Stopping also releases the request slot, so the pilot can queue a replacement.
        if (TryComp<ShipAlertComponent>(grid, out var alert))
            alert.NextInternetSound = TimeSpan.Zero;

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} stopped the internet sound and emptied the playlist on the PA of {ToPrettyString(grid):grid}");
    }

    /// <summary>
    /// Skips the playing track, removes one waiting link or empties the queue. The ship's controls are the
    /// pilot's to use, but the client only names things; what exists and what is allowed is decided here.
    /// </summary>
    private void OnPlaylistRequest(Entity<ShuttleConsoleComponent> ent, ref ShipPaPlaylistRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        switch (args.Action)
        {
            case ShipPaPlaylistAction.Skip:
                switch (_playlist.Skip(grid, out var wait))
                {
                    case ShipPaSkipResult.TooSoon:
                        var seconds = (int) Math.Ceiling(wait.TotalSeconds);
                        _popup.PopupEntity(Loc.GetString("ship-pa-sound-cooldown", ("seconds", seconds)), ent, args.Actor);
                        break;
                    case ShipPaSkipResult.Skipped:
                        _adminLogger.Add(LogType.Action, LogImpact.Low,
                            $"{ToPrettyString(args.Actor):player} skipped the track on the PA of {ToPrettyString(grid):grid}");
                        break;
                }

                break;

            case ShipPaPlaylistAction.Remove:
                if (_playlist.Remove(grid, args.Id))
                {
                    _adminLogger.Add(LogType.Action, LogImpact.Low,
                        $"{ToPrettyString(args.Actor):player} removed a queued track from the PA playlist of {ToPrettyString(grid):grid}");
                }

                break;

            case ShipPaPlaylistAction.Clear:
                if (_playlist.ClearQueued(grid) > 0)
                {
                    _adminLogger.Add(LogType.Action, LogImpact.Low,
                        $"{ToPrettyString(args.Actor):player} cleared the PA playlist of {ToPrettyString(grid):grid}");
                }

                break;
        }
    }

    /// <summary>
    /// Sets the ship's situation code and reads it out over the PA.
    /// </summary>
    public void SetCode(EntityUid grid, ProtoId<ShipAlertCodePrototype> code, EntityUid? user = null, bool announce = true)
    {
        if (!Exists(grid) || !_proto.TryIndex(code, out var proto))
            return;

        // Codes a player can't pick are still settable by other systems and by admins.
        if (user != null && !proto.Selectable)
            return;

        var alert = EnsureComp<ShipAlertComponent>(grid);

        if (alert.Code == code)
            return;

        var old = alert.Code;
        alert.Code = code;
        Dirty(grid, alert);

        var ev = new ShipAlertCodeChangedEvent(grid, old, code, user);
        RaiseLocalEvent(grid, ref ev, broadcast: true);

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{(user is { } actor ? ToPrettyString(actor).ToString() : "Server")} set {ToPrettyString(grid)} to {code.Id}");

        if (announce)
            _pa.Announce(grid, Loc.GetString(proto.Announcement, ("ship", _pa.GetShipName(grid))), proto.Sound, color: proto.Color);
    }

    /// <summary>
    /// Sounds or secures from general quarters: the klaxon loop plus the matching announcement.
    /// </summary>
    public void SetGeneralQuarters(EntityUid grid, bool active, EntityUid? user = null)
    {
        if (!Exists(grid))
            return;

        var alert = EnsureComp<ShipAlertComponent>(grid);

        if (alert.GeneralQuarters == active)
            return;

        alert.GeneralQuarters = active;
        Dirty(grid, alert);

        if (active)
        {
            var message = Loc.GetString(alert.GeneralQuartersAnnouncement);
            _pa.StartAlarm(grid, GeneralQuartersAlarm, alert.GeneralQuartersAlarm, message: message, color: alert.GeneralQuartersColor);
            _pa.Announce(grid, message, alert.GeneralQuartersSound ?? alert.AnnouncementChime, color: alert.GeneralQuartersColor);
        }
        else
        {
            _pa.StopAlarm(grid, GeneralQuartersAlarm);
            _pa.Announce(grid,
                Loc.GetString(alert.GeneralQuartersSecureAnnouncement),
                alert.GeneralQuartersSecureSound ?? alert.AnnouncementChime);
        }

        var ev = new ShipGeneralQuartersChangedEvent(grid, active, user);
        RaiseLocalEvent(grid, ref ev, broadcast: true);

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{(user is { } actor ? ToPrettyString(actor).ToString() : "Server")} {(active ? "sounded" : "secured from")} general quarters on {ToPrettyString(grid)}");
    }

    /// <summary>
    /// Resolves what the console is actually flying, which is not its own grid for drone consoles.
    /// </summary>
    private EntityUid? GetConsoleGrid(EntityUid console)
    {
        var ev = new ConsoleShuttleEvent { Console = console };
        RaiseLocalEvent(console, ref ev);

        if (ev.Console is not { } target || !TryComp(target, out TransformComponent? xform))
            return null;

        return xform.GridUid;
    }

    /// <summary>
    /// The state still applies with the ship's speakers down, but the pilot should know nobody heard it.
    /// </summary>
    private void WarnIfUnheard(EntityUid console, EntityUid grid, EntityUid actor)
    {
        if (_pa.CountSpeakers(grid).Online == 0)
            _popup.PopupEntity(Loc.GetString("ship-pa-announce-no-speakers"), console, actor);
    }

    /// <summary>
    /// Trims, caps and flattens a typed announcement. Markup is escaped downstream by the chat system.
    /// </summary>
    private static string Sanitize(string text, int maxLength)
    {
        return SharedChatSystem.SanitizeAnnouncement(text, maxLength)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    internal static string GetSafeOrigin(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return "<invalid URL>";

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }
}
