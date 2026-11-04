using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ShipPa;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared.Alert;
using Content.Shared.Camera;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Docking clamps on raiders: a ship one docks with is held, unable to undock from it or jump, until someone releases
/// the clamps at the raider's helm or the raider has no powered helm left.
/// </summary>
public sealed partial class WFDockingClampSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private ShuttleConsoleSystem _consoles = default!;

    /// <summary>How long clamps released by hand stay open, so the ship that got free is not bitten again at once.</summary>
    public static readonly TimeSpan OpenTime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan AlertInterval = TimeSpan.FromSeconds(1);
    private static readonly ProtoId<AlertPrototype> ClampedAlert = "WFDockingClamped";
    private static readonly SoundSpecifier ClampSound = new SoundPathSpecifier("/Audio/Effects/metal_slam1.ogg",
        AudioParams.Default.WithVolume(4f));
    private static readonly SoundSpecifier BoltSound = new SoundPathSpecifier("/Audio/Machines/boltsdown.ogg");
    private static readonly SoundSpecifier ReleaseSound = new SoundPathSpecifier("/Audio/Machines/boltsup.ogg");
    private static readonly SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Machines/airlock_deny.ogg");
    private static readonly Color PaColor = Color.FromHex("#ff6040");

    /// <summary>Everyone currently shown the clamped alert.</summary>
    private readonly HashSet<EntityUid> _alerted = new();
    private TimeSpan _nextAlert;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnFtlAttempt);
        SubscribeLocalEvent<Content.Server._WF.Planets.Hop.WFPlanetHopAttemptEvent>(OnHopAttempt);
        SubscribeLocalEvent<GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ShuttleConsoleComponent, ExaminedEvent>(OnConsoleExamined);
        SubscribeLocalEvent<DockingComponent, ExaminedEvent>(OnDockExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextAlert)
            return;

        _nextAlert = _timing.CurTime + AlertInterval;
        UpdateAlerts();
    }

    /// <summary>Shows the clamped alert to everyone aboard a held ship and clears it from everyone who is not.</summary>
    private void UpdateAlerts()
    {
        var held = new HashSet<EntityUid>();
        var raiders = EntityQueryEnumerator<WFDockingClampComponent>();
        while (raiders.MoveNext(out var raider, out var clamps))
        {
            if (Holding((raider, clamps)))
                held.UnionWith(clamps.Held);
        }

        var aboard = new HashSet<EntityUid>();
        if (held.Count > 0)
        {
            var players = EntityQueryEnumerator<ActorComponent, AlertsComponent, TransformComponent>();
            while (players.MoveNext(out var uid, out _, out _, out var xform))
            {
                if (xform.GridUid is { } grid && held.Contains(grid))
                    aboard.Add(uid);
            }
        }

        foreach (var uid in _alerted)
        {
            if (!aboard.Contains(uid) && !TerminatingOrDeleted(uid))
                _alerts.ClearAlert(uid, ClampedAlert);
        }

        foreach (var uid in aboard)
        {
            if (!_alerted.Contains(uid))
                _alerts.ShowAlert(uid, ClampedAlert);
        }

        _alerted.Clear();
        _alerted.UnionWith(aboard);
    }

    private void OnDock(DockEvent args)
    {
#pragma warning disable CS0618 // DockEvent carries the dock components without their uids.
        Bite(args.GridAUid, args.GridBUid, args.DockA.Owner);
        Bite(args.GridBUid, args.GridAUid, args.DockB.Owner);
#pragma warning restore CS0618
    }

    private void OnUndock(UndockEvent args)
    {
        LetGo(args.GridAUid, args.GridBUid);
        LetGo(args.GridBUid, args.GridAUid);
    }

    /// <summary>A raider's clamps take hold of the ship it has docked with, if they are shut and it has power.</summary>
    private void Bite(EntityUid raider, EntityUid prey, EntityUid port)
    {
        _consoles.WFForgetDocks();
        if (!TryComp<WFDockingClampComponent>(raider, out var clamps) || HasComp<WFDockingClampComponent>(prey)
            || _timing.CurTime < clamps.OpenUntil || !HasHelm(raider) || !clamps.Held.Add(prey))
            return;

        // Everyone aboard either ship hears the clamps slam home; those on the prey feel it and are told.
        var aboard = Filter.Empty().AddInGrid(prey).AddInGrid(raider);
        _audio.PlayGlobal(ClampSound, aboard, true);
        _audio.PlayPvs(BoltSound, port);

        var engaged = Loc.GetString("wf-clamps-engaged", ("ship", Name(raider)));
        foreach (var session in Filter.Empty().AddInGrid(prey).Recipients)
        {
            if (session.AttachedEntity is not { } mob)
                continue;

            _recoil.KickCamera(mob, _random.NextVector2(0.6f, 1f));
            _popup.PopupEntity(Loc.GetString("wf-clamps-engaged-popup"), mob, mob, PopupType.LargeCaution);
        }

        Tell(prey, engaged);
        _nextAlert = TimeSpan.Zero;
        _consoles.RefreshShuttleConsoles();
    }

    private void LetGo(EntityUid raider, EntityUid prey)
    {
        _consoles.WFForgetDocks();
        if (TryComp<WFDockingClampComponent>(raider, out var clamps))
            clamps.Held.Remove(prey);
    }

    /// <summary>Announces to a ship over its PA, or straight into the chat of everyone aboard when it has no speaker.</summary>
    private void Tell(EntityUid ship, string message)
    {
        if (_pa.Announce(ship, message, sender: Loc.GetString("wf-clamps-sender"), color: PaColor))
            return;

        _chat.DispatchFilteredAnnouncement(Filter.Empty().AddInGrid(ship), message, ship,
            Loc.GetString("wf-clamps-sender"), playSound: false, colorOverride: PaColor);
    }

    /// <summary>Whether a ship is held by some raider's clamps right now. Clamps on a raider with no powered helm let go.</summary>
    public bool IsHeld(EntityUid ship)
    {
        return HeldBy(ship) != null;
    }

    /// <summary>The raider whose clamps hold a ship right now, if any.</summary>
    public EntityUid? HeldBy(EntityUid ship)
    {
        var query = EntityQueryEnumerator<WFDockingClampComponent>();
        while (query.MoveNext(out var raider, out var clamps))
        {
            if (clamps.Held.Contains(ship) && Holding((raider, clamps)))
                return raider;
        }

        return null;
    }

    /// <summary>
    /// Whether an undock asked for at a console is refused: the port joins a raider with a ship its clamps hold, and
    /// the console is not the raider's own. The person at the console is told why.
    /// </summary>
    public bool Refuses(EntityUid console, Entity<DockingComponent> dock)
    {
        if (dock.Comp.DockedWith is not { } other || Transform(dock).GridUid is not { } here
            || Transform(other).GridUid is not { } there || Transform(console).GridUid is not { } asking)
            return false;

        if (!Clamped(here, there, asking) && !Clamped(there, here, asking))
            return false;

        _popup.PopupEntity(Loc.GetString("wf-clamps-refuse"), console, PopupType.LargeCaution);
        _audio.PlayPvs(DenySound, console);
        return true;
    }

    /// <summary>
    /// The raider whose powered clamps hold the joint a port is part of, if any. Unlike <see cref="HeldBy"/> it never
    /// releases anything, so it is safe while the docking screens are being built.
    /// </summary>
    public EntityUid? Holder(Entity<DockingComponent> dock)
    {
        if (dock.Comp.DockedWith is not { } other || Transform(dock).GridUid is not { } here
            || Transform(other).GridUid is not { } there)
            return null;

        if (Grips(here, there))
            return here;

        return Grips(there, here) ? there : null;
    }

    private bool Grips(EntityUid raider, EntityUid prey)
    {
        return TryComp<WFDockingClampComponent>(raider, out var clamps) && clamps.Held.Contains(prey) && HasHelm(raider);
    }

    private bool Clamped(EntityUid raider, EntityUid prey, EntityUid asking)
    {
        return asking != raider && TryComp<WFDockingClampComponent>(raider, out var clamps) && clamps.Held.Contains(prey)
            && Holding((raider, clamps));
    }

    /// <summary>Whether a raider's clamps still have power; without it they open and everything held is let go.</summary>
    private bool Holding(Entity<WFDockingClampComponent> raider)
    {
        if (raider.Comp.Held.Count == 0)
            return false;

        if (HasHelm(raider))
            return true;

        Release(raider, null);
        return false;
    }

    /// <summary>Opens a raider's clamps. Released by hand they stay open a while; a power loss only frees what is held.</summary>
    public void Release(Entity<WFDockingClampComponent> raider, EntityUid? user)
    {
        _consoles.WFForgetDocks();
        var held = raider.Comp.Held.Count > 0;
        foreach (var prey in raider.Comp.Held)
        {
            if (TerminatingOrDeleted(prey))
                continue;

            _audio.PlayGlobal(ReleaseSound, Filter.Empty().AddInGrid(prey), true);
            Tell(prey, Loc.GetString("wf-clamps-released"));
        }

        raider.Comp.Held.Clear();
        _nextAlert = TimeSpan.Zero;
        if (held)
            _consoles.RefreshShuttleConsoles();

        if (user == null)
            return;

        raider.Comp.OpenUntil = _timing.CurTime + OpenTime;
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user.Value):player} released the docking clamps of {ToPrettyString(raider):grid}");
    }

    private bool HasHelm(EntityUid grid)
    {
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && this.IsPowered(uid, EntityManager))
                return true;
        }

        return false;
    }

    /// <summary>A clamped ship can't hop into orbit either.</summary>
    private void OnHopAttempt(ref Content.Server._WF.Planets.Hop.WFPlanetHopAttemptEvent args)
    {
        if (args.Cancelled || !IsHeld(args.Uid))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-clamps-refuse");
    }

    private void OnFtlAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (args.Cancelled || !IsHeld(args.Uid))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-clamps-refuse");
    }

    /// <summary>A helm says which raider holds its ship, or which ships its own clamps hold.</summary>
    private void OnConsoleExamined(Entity<ShuttleConsoleComponent> ent, ref ExaminedEvent args)
    {
        if (Transform(ent).GridUid is not { } grid)
            return;

        if (HeldBy(grid) is { } raider)
            args.PushMarkup(Loc.GetString("wf-clamps-examine-held", ("ship", Name(raider))));

        if (TryComp<WFDockingClampComponent>(grid, out var clamps) && Holding((grid, clamps)))
        {
            var ships = string.Join(", ", clamps.Held.Where(Exists).Select(uid => Name(uid)));
            args.PushMarkup(Loc.GetString("wf-clamps-examine-holding", ("ships", ships)));
        }
    }

    /// <summary>A port joining a raider and the ship its clamps hold says it is clamped.</summary>
    private void OnDockExamined(Entity<DockingComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.DockedWith is not { } other || Transform(ent).GridUid is not { } here
            || Transform(other).GridUid is not { } there)
            return;

        if (HeldBy(here) == there || HeldBy(there) == here)
            args.PushMarkup(Loc.GetString("wf-clamps-examine-port"));
    }

    private void OnGetVerbs(GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<ShuttleConsoleComponent>(args.Target)
            || Transform(args.Target).GridUid is not { } grid || !TryComp<WFDockingClampComponent>(grid, out var clamps)
            || clamps.Held.Count == 0)
            return;

        var user = args.User;
        var target = args.Target;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-clamps-release-verb"),
            Priority = 5,
            Act = () =>
            {
                Release((grid, clamps), user);
                _popup.PopupEntity(Loc.GetString("wf-clamps-release-done"), target, user);
            },
        });
    }
}
