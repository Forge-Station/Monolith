using System.Globalization;
using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Radio;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>What the radio officer says; each line is a Fluent message with numbered variants.</summary>
public enum WFRadioLine : byte
{
    Docking,
    Undocking,
    Jumping,
    Arriving,
    OnStation,
    DockAborted,
    Mayday,
    Boarded,
    CaptainDown,
    HelmDown,
    AllClear,
    Approach,
    DockWarning,
    BoardWarning,
}

/// <summary>One line the radio officer put on the air.</summary>
public readonly record struct WFRadioTransmission(
    WFRadioLine Line,
    ProtoId<RadioChannelPrototype> Channel,
    string Text);

/// <summary>
/// Radio officers report what happens to their ship: routine traffic on the local channel, attacks and losses on the
/// alert channel. Driven by ship and crew events plus a once-a-second look for boarders, so it works while the
/// officer's HTN sleeps. He transmits as himself and only while he is up, so downing him ends the broadcasts.
/// </summary>
public sealed partial class WFRadioOperatorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;

    /// <summary>How many transmissions <see cref="WFRadioOperatorComponent.Sent"/> keeps.</summary>
    private const int SentKept = 20;

    /// <summary>Mayday coordinates are rounded to this many metres.</summary>
    private const float PositionRounding = 10f;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextPoll;

    // DockingSystem.GetDocks reuses a set that UndockDocks is iterating when it raises UndockEvent.
    private readonly HashSet<Entity<DockingComponent>> _docks = new();

    private readonly List<(Entity<WFRadioOperatorComponent> Op, EntityUid Grid)> _polled = new();
    private readonly List<EntityUid> _witnesses = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCrewComponent, BeforeDamageChangedEvent>(OnCrewDamaged,
            before: [typeof(Content.Shared._Onyx.Wounds.WoundDamageRoutingSystem)]);
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
        SubscribeLocalEvent<Content.Server._WF.Planets.Hop.WFPlanetHopCompletedEvent>(OnHopCompleted);
        SubscribeLocalEvent<WFPilotOrdersCompletedEvent>(OnOrdersCompleted);
        SubscribeLocalEvent<WFPilotDockFailedEvent>(OnDockFailed);
        SubscribeLocalEvent<WFCrewMemberDownEvent>(OnCrewDown);
        SubscribeLocalEvent<WFCrewHullHitEvent>(OnHullHit, after: [typeof(WFCrewAlertSystem)]);
        SubscribeLocalEvent<WFCrewAlertEvent>(OnCrewAlert);
        SubscribeLocalEvent<WFCrewSecurityIncidentEvent>(OnSecurityIncident);
        SubscribeLocalEvent<WFPilotOrdersChangedEvent>(OnOrdersChanged);
    }

    /// <summary>Once a second: boarders, the drive spooling up, and the all-clear.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextPoll)
            return;

        _nextPoll = now + PollInterval;

        _polled.Clear();
        var operators = EntityQueryEnumerator<WFRadioOperatorComponent, TransformComponent>();
        while (operators.MoveNext(out var uid, out var radio, out var xform))
        {
            if (xform.GridUid is { } grid)
                _polled.Add(((uid, radio), grid));
        }

        foreach (var (op, grid) in _polled)
        {
            if (op.Comp.DownReported.Count > 0)
                op.Comp.DownReported.RemoveWhere(reported => TerminatingOrDeleted(reported));
            if (!IsSpokesman(op))
                continue;

            // The farthest corner of the grid from the officer reaches all of it.
            var range = 0f;
            if (TryComp<MapGridComponent>(grid, out var gridComp))
            {
                var box = gridComp.LocalAABB;
                var opTransform = Transform(op);
                var at = opTransform.ParentUid == grid ? opTransform.LocalPosition : box.Center;
                range = MathF.Max(MathF.Max((box.BottomLeft - at).Length(), (box.TopRight - at).Length()),
                    MathF.Max((box.TopLeft - at).Length(), (box.BottomRight - at).Length()));
            }
            foreach (var hostile in _faction.GetNearbyHostiles(op.Owner, range))
            {
                if (Transform(hostile).GridUid != grid
                    || !_security.IsBoardingCandidate(hostile)
                    || !(EntityManager.System<WFCrewWeaponSystem>().CanSee(op, hostile)
                         || EntityManager.System<WFCrewCommsSystem>().Knows(op, hostile))
                    || InCrew(hostile, GroupOf(op), HomeGridOf(op)))
                {
                    continue;
                }

                Boarded(op, hostile);
                break;
            }

            // Said while the drive spools: FTLStartedEvent comes once the ship is in FTL space, out of reach.
            var spooling = TryComp<FTLComponent>(grid, out var ftl) && ftl.State == FTLState.Starting
                           || TryComp<Content.Server._WF.Planets.Hop.WFPlanetHopComponent>(grid, out var hop)
                           && hop.Phase == Content.Server._WF.Planets.Hop.WFPlanetHopPhase.Warm;
            if (!spooling)
                op.Comp.JumpAnnounced = false;
            else if (!op.Comp.JumpAnnounced)
                op.Comp.JumpAnnounced = TrySend(op, WFRadioLine.Jumping);

            if (op.Comp.Alerted && now >= op.Comp.LastHostileActivity + op.Comp.AllClearDelay)
                AllClear(op);
        }
    }

    /// <summary>Sets what the ship is called on the air; null or blank goes back to the grid's name.</summary>
    public void SetCallsign(Entity<WFRadioOperatorComponent?> ent, string? callsign)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Callsign = string.IsNullOrWhiteSpace(callsign) ? null : callsign.Trim();
    }

    /// <summary>
    /// A crewman hurt by someone outside the crew is a hostile act for the crew's radio officers. Before the damage
    /// lands, because Wolfmed routes a body's damage through its parts and the body's own DamageChangedEvent then has
    /// no origin.
    /// </summary>
    private void OnCrewDamaged(Entity<WFCrewComponent> ent, ref BeforeDamageChangedEvent args)
    {
        var (uid, component) = ent;
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } source || source == uid)
            return;

        // A beam names the gun as its origin; the contact is whoever holds it.
        var origin = EntityManager.System<WFCrewSystem>().Wielder(source);
        // A shot can land after the gun that fired it is gone.
        if (origin == uid || TerminatingOrDeleted(origin))
            return;

        var home = HomeGridOf(uid);
        if (InCrew(origin, component.Group, home))
            return;

        // A ship's gun, or its shot, is nobody aboard: the fire is from the vessel it stands on, and anything of
        // the crew's own ship that hurts him is an accident and no attack.
        if (!HasComp<Content.Shared.Mobs.Components.MobStateComponent>(origin))
        {
            if (Transform(origin).GridUid is not { } vessel || vessel == home
                || home is { } own && EntityManager.System<WFCrewAlertSystem>().IsExcused(own, vessel))
                return;
            origin = vessel;
        }

        _witnesses.Clear();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var member, out var crew, out var xform))
        {
            if (crew.Group == component.Group && (crew.Post?.EntityId ?? xform.GridUid) == home && _mobState.IsAlive(member))
                _witnesses.Add(member);
        }

        // Officers who saw it take it in; one crewman's report on the radio is all the crew needs to send.
        var weapons = EntityManager.System<WFCrewWeaponSystem>();
        foreach (var witness in _witnesses)
        {
            if (HasComp<WFRadioOperatorComponent>(witness) && (witness == uid || weapons.CanSee(witness, uid)))
                ReceiveIncident(witness, origin, WFRadioLine.Mayday);
        }

        var comms = EntityManager.System<WFCrewCommsSystem>();
        if (_witnesses.Contains(uid) && comms.Report(uid, origin, WFRadioLine.Mayday))
            return;
        foreach (var witness in _witnesses)
        {
            if (witness != uid && weapons.CanSee(witness, uid) && comms.Report(witness, origin, WFRadioLine.Mayday))
                return;
        }
    }

    private void OnDock(DockEvent args)
    {
        if (args.GridAUid == args.GridBUid)
            return;

        // Each dock is docked with the other side's port by now.
        GridDocked(args.GridAUid, args.GridBUid, args.DockA.DockedWith);
        GridDocked(args.GridBUid, args.GridAUid, args.DockB.DockedWith);
    }

    private void OnHullHit(ref WFCrewHullHitEvent args)
    {
        // A stray shot from the ship's own side is no cause for a mayday.
        if (EntityManager.System<WFCrewEscortSystem>().AreInFormation(args.Grid, args.AttackerGrid)
            || EntityManager.System<WFCrewAlertSystem>().IsExcused(args.Grid, args.AttackerGrid))
            return;
        foreach (var op in OperatorsOn(args.Grid))
            HostileAct(op, args.AttackerGrid);
    }

    private void OnCrewAlert(ref WFCrewAlertEvent args)
    {
        // A patrol zone's warning is not an attack, so no mayday and no later all-clear.
        if (args.Hostiles.Length == 0 || EntityManager.System<WFCrewAlertSystem>().InZoneReport)
            return;
        foreach (var op in OperatorsOn(args.Grid))
        {
            if (GroupOf(op) == args.Group && (EntityManager.System<WFCrewAlertSystem>().IsVessel(args.Hostiles[0])
                || EntityManager.System<WFCrewWeaponSystem>().CanSee(op, args.Hostiles[0])
                || EntityManager.System<WFCrewCommsSystem>().Knows(op, args.Hostiles[0])))
                HostileAct(op, args.Hostiles[0]);
        }
    }

    private void OnUndock(UndockEvent args)
    {
        if (args.GridAUid == args.GridBUid)
            return;

        GridUndocked(args.GridAUid, args.GridBUid);
        GridUndocked(args.GridBUid, args.GridAUid);
    }

    private void OnFtlCompleted(ref FTLCompletedEvent args)
    {
        foreach (var op in OperatorsOn(args.Entity))
        {
            TrySend(op, WFRadioLine.Arriving);
        }
    }

    private void OnHopCompleted(ref Content.Server._WF.Planets.Hop.WFPlanetHopCompletedEvent args)
    {
        foreach (var op in OperatorsOn(args.Hull))
        {
            TrySend(op, WFRadioLine.Arriving);
        }
    }

    private void OnOrdersCompleted(ref WFPilotOrdersCompletedEvent args)
    {
        foreach (var op in OperatorsOfCrew(args.Mob))
        {
            TrySend(op, WFRadioLine.OnStation);
        }
    }

    private void OnDockFailed(ref WFPilotDockFailedEvent args)
    {
        foreach (var op in OperatorsOfCrew(args.Mob))
        {
            TrySend(op, WFRadioLine.DockAborted);
        }
    }

    /// <summary>The captain or the pilot going down gets one line per crewman.</summary>
    private void OnCrewDown(ref WFCrewMemberDownEvent args)
    {
        WFRadioLine line;
        if (args.Role == WFCrewRoles.Captain)
            line = WFRadioLine.CaptainDown;
        else if (args.Role == WFCrewRoles.Pilot)
            line = WFRadioLine.HelmDown;
        else
            return;

        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var witness, out var crew, out var xform))
        {
            if (witness == args.Mob || !_mobState.IsAlive(witness)
                || !InCrew(args.Mob, crew.Group, crew.Post?.EntityId ?? xform.GridUid)
                || !EntityManager.System<WFCrewWeaponSystem>().CanSee(witness, args.Mob))
                continue;
            ReceiveIncident(witness, args.Mob, line);
            EntityManager.System<WFCrewCommsSystem>().Report(witness, args.Mob, line);
        }
    }

    /// <summary>Announces an incident personally witnessed or delivered through an equipped radio.</summary>
    public void ReceiveIncident(EntityUid recipient, EntityUid subject, WFRadioLine line)
    {
        if (!TryComp<WFRadioOperatorComponent>(recipient, out var radio) || !_mobState.IsAlive(recipient)
            || !IsSpokesman((recipient, radio)))
            return;
        if (line == WFRadioLine.Mayday)
        {
            HostileAct((recipient, radio), subject);
            return;
        }
        if (line is not (WFRadioLine.CaptainDown or WFRadioLine.HelmDown)
            || recipient == subject || radio.DownReported.Contains(subject))
            return;
        if (TrySend((recipient, radio), line))
            radio.DownReported.Add(subject);
    }

    /// <summary>Reports a docking, once per grid docked with however many ports connect.</summary>
    private void GridDocked(EntityUid grid, EntityUid other, EntityUid? port)
    {
        if (DocksBetween(grid, other) > 1)
            return;

        foreach (var op in OperatorsOn(grid))
        {
            if (!_security.IsOutboundDock(grid, GroupOf(op), other))
                continue;
            TrySend(op,
                WFRadioLine.Docking,
                ("station", NameOrUnknown(other)),
                ("port", port is { } dock ? Name(dock) : string.Empty));
        }
    }

    private void OnOrdersChanged(ref WFPilotOrdersChangedEvent args)
    {
        if (args.Orders != WFPilotOrder.Dock || !TryComp<WFPilotDutyComponent>(args.Mob, out var duty)
            || duty.DockTarget is not { } target)
            return;
        foreach (var op in OperatorsOfCrew(args.Mob))
            TrySend(op, WFRadioLine.Approach, ("station", NameOrUnknown(target)));
    }

    private void OnSecurityIncident(ref WFCrewSecurityIncidentEvent args)
    {
        foreach (var op in OperatorsOn(args.Grid))
        {
            if (GroupOf(op) != args.Group || !args.Docking
                && !EntityManager.System<WFCrewWeaponSystem>().CanSee(op, args.Visitor)
                && !EntityManager.System<WFCrewCommsSystem>().Knows(op, args.Visitor))
                continue;
            // One warning a visitor: the crew turning on him later is not said again on the air.
            var warned = op.Comp.Warned;
            if ((!warned.TryGetValue(args.Visitor, out var until) || _timing.CurTime >= until)
                && TrySend(op, args.Docking ? WFRadioLine.DockWarning : WFRadioLine.BoardWarning,
                    ("visitor", NameOrUnknown(args.Visitor))))
                warned[args.Visitor] = _timing.CurTime + WarningRepeat;
            if (args.Response == WFCrewSecurityResponse.Hostile)
                HostileAct(op, args.Visitor);
        }
    }

    /// <summary>How long before the same visitor is warned over the radio again.</summary>
    private static readonly TimeSpan WarningRepeat = TimeSpan.FromMinutes(5);

    /// <summary>Reports leaving a grid once its last port lets go.</summary>
    private void GridUndocked(EntityUid grid, EntityUid other)
    {
        if (DocksBetween(grid, other) > 0)
            return;

        foreach (var op in OperatorsOn(grid))
        {
            TrySend(op, WFRadioLine.Undocking, ("station", NameOrUnknown(other)));
        }
    }

    /// <summary>Starts or extends the attack episode; its first act gets the mayday.</summary>
    private void HostileAct(Entity<WFRadioOperatorComponent> ent, EntityUid hostile)
    {
        if (!IsSpokesman(ent))
            return;
        ent.Comp.Alerted = true;
        ent.Comp.LastHostileActivity = _timing.CurTime;
        if (ent.Comp.MaydaySent || Transform(ent).GridUid is not { } grid)
            return;

        var vessel = string.Empty;
        if (TryComp(hostile, out TransformComponent? hostileXform)
            && (HasComp<MapGridComponent>(hostile) ? hostile : hostileXform.GridUid) is { } hostileGrid
            && hostileGrid != grid
            && Name(hostileGrid) is { Length: > 0 } hostileName)
        {
            vessel = _loc.GetString("wf-crew-radio-mayday-vessel", ("hostile", hostileName));
        }

        var position = _transform.GetWorldPosition(grid);
        ent.Comp.MaydaySent = TrySend(ent,
            WFRadioLine.Mayday,
            ("x", Rounded(position.X)),
            ("y", Rounded(position.Y)),
            ("vessel", vessel));
    }

    private void Boarded(Entity<WFRadioOperatorComponent> ent, EntityUid boarder)
    {
        HostileAct(ent, boarder);
        if (!ent.Comp.BoardedSent)
            ent.Comp.BoardedSent = TrySend(ent, WFRadioLine.Boarded);
    }

    /// <summary>Ends the episode, with the all-clear if a mayday went out.</summary>
    private void AllClear(Entity<WFRadioOperatorComponent> ent)
    {
        if (ent.Comp.MaydaySent)
            TrySend(ent, WFRadioLine.AllClear);

        ent.Comp.Alerted = false;
        ent.Comp.MaydaySent = false;
        ent.Comp.BoardedSent = false;
    }

    /// <summary>
    /// Puts a random variant of a line on the air, with the callsign added to its arguments. False when the officer
    /// is down or off any grid, or said the same line within the cooldown.
    /// </summary>
    private bool TrySend(Entity<WFRadioOperatorComponent> ent, WFRadioLine line, params (string, object)[] args)
    {
        var (uid, radio) = ent;
        if (TerminatingOrDeleted(uid)
            || _mobState.IsIncapacitated(uid)
            || Transform(uid).GridUid is not { } grid
            || TerminatingOrDeleted(grid))
        {
            return false;
        }

        if (!IsSpokesman(ent))
            return false;

        // A crew that doesn't call for help keeps all of its troubles off the air.
        var callsForHelp = radio.CallsForHelp && (!TryComp<WFCrewComponent>(uid, out var crewman) || crewman.CallsForHelp);
        if (!callsForHelp && line is (WFRadioLine.Mayday or WFRadioLine.Boarded or WFRadioLine.CaptainDown
                or WFRadioLine.HelmDown or WFRadioLine.AllClear))
            return false;

        var now = _timing.CurTime;
        if (radio.LastSent.TryGetValue(line, out var last) && now < last + radio.Cooldown)
            return false;

        var id = $"wf-crew-radio-{LineId(line)}";
        var variants = 0;
        while (_loc.HasString($"{id}-{variants + 1}"))
        {
            variants++;
        }

        if (variants == 0)
        {
            Log.Error($"No Fluent variants for {id}");
            return false;
        }

        var callsign = string.IsNullOrWhiteSpace(radio.Callsign) ? NameOrUnknown(grid) : radio.Callsign;
        var text = _loc.GetString($"{id}-{_random.Next(1, variants + 1)}", args.Append(("callsign", callsign)).ToArray());
        radio.LastSent[line] = now;

        // Each line goes out once. A warning is for the ship alongside, so it is always local; the rest is the
        // faction's own business when the ship has a faction channel, else alerts go wide and routine stays local.
        if (line is WFRadioLine.DockWarning or WFRadioLine.BoardWarning or WFRadioLine.Approach)
            Transmit(ent, line, radio.LocalChannel, text);
        else if (radio.FactionChannel is { } faction)
            Transmit(ent, line, faction, text);
        else
            Transmit(ent, line, IsAlert(line) ? radio.AlertChannel : radio.LocalChannel, text);

        if (radio.SpeakAloud)
            _chat.TrySendInGameICMessage(uid, text, InGameICChatType.Speak, hideChat: false, checkRadioPrefix: false);

        return true;
    }

    private void Transmit(Entity<WFRadioOperatorComponent> ent,
        WFRadioLine line,
        ProtoId<RadioChannelPrototype> channel,
        string text)
    {
        _radio.SendRadioMessage(ent, text, channel, ent);
        ent.Comp.Sent.Add(new WFRadioTransmission(line, channel, text));
        if (ent.Comp.Sent.Count > SentKept)
            ent.Comp.Sent.RemoveAt(0);
    }

    /// <summary>
    /// Whether the entity belongs to the crew of this home grid and group.
    /// </summary>
    private bool InCrew(EntityUid uid, string group, EntityUid? homeGrid)
    {
        return TryComp<WFCrewComponent>(uid, out var crew)
               && crew.Group == group
               && (crew.Post?.EntityId ?? Transform(uid).GridUid) == homeGrid;
    }

    /// <summary>The grid a crewman belongs to: the one his post is on, else the one he stands on.</summary>
    private EntityUid? HomeGridOf(EntityUid uid)
    {
        return TryComp<WFCrewComponent>(uid, out var crew) && crew.Post is { } post
            ? post.EntityId
            : Transform(uid).GridUid;
    }

    /// <summary>
    /// Whether this operator is the one that speaks for its crew: a living radio officer, else the captain, else
    /// nobody, so a crew with both does not say every line twice.
    /// </summary>
    private bool HeadsetOn(EntityUid uid)
    {
        return EntityManager.System<Content.Shared.Inventory.InventorySystem>().TryGetSlotEntity(uid, "ears", out var headset)
            && TryComp<Content.Shared.Radio.Components.HeadsetComponent>(headset, out var set) && set.Enabled;
    }

    private bool IsSpokesman(Entity<WFRadioOperatorComponent> ent)
    {
        if (!TryComp<WFCrewComponent>(ent, out var own))
            return true;

        var home = own.Post?.EntityId ?? Transform(ent).GridUid;
        EntityUid? chosen = null;
        var chosenRank = int.MaxValue;
        var query = EntityQueryEnumerator<WFRadioOperatorComponent, WFCrewComponent>();
        while (query.MoveNext(out var uid, out _, out var crew))
        {
            if (crew.Group != own.Group || (crew.Post?.EntityId ?? Transform(uid).GridUid) != home
                || TerminatingOrDeleted(uid) || _mobState.IsIncapacitated(uid))
                continue;

            if (crew.Role != WFCrewRoles.RadioOperator && crew.Role != WFCrewRoles.Captain)
                continue;

            // The radio officer before the captain, and of two alike the one whose headset is switched on; the
            // lower entity id settles the rest, so the choice doesn't depend on enumeration order.
            var rank = (crew.Role == WFCrewRoles.RadioOperator ? 0 : 2) + (HeadsetOn(uid) ? 0 : 1);
            if (rank < chosenRank || rank == chosenRank && chosen is { } current && uid.Id < current.Id)
            {
                chosen = uid;
                chosenRank = rank;
            }
        }

        return chosen == ent.Owner;
    }

    private string GroupOf(EntityUid op)
    {
        return TryComp<WFCrewComponent>(op, out var crew) ? crew.Group : string.Empty;
    }

    /// <summary>Radio officers of the crewman's crew.</summary>
    private List<Entity<WFRadioOperatorComponent>> OperatorsOfCrew(EntityUid crewman)
    {
        var found = new List<Entity<WFRadioOperatorComponent>>();
        var query = EntityQueryEnumerator<WFRadioOperatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var radio, out var xform))
        {
            if (InCrew(crewman, GroupOf(uid), HomeGridOf(uid)))
                found.Add((uid, radio));
        }

        return found;
    }

    /// <summary>Radio officers aboard the grid.</summary>
    private List<Entity<WFRadioOperatorComponent>> OperatorsOn(EntityUid grid)
    {
        var found = new List<Entity<WFRadioOperatorComponent>>();
        var query = EntityQueryEnumerator<WFRadioOperatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var radio, out var xform))
        {
            if (xform.GridUid == grid)
                found.Add((uid, radio));
        }

        return found;
    }

    /// <summary>Ports on the grid docked with ports on the other grid.</summary>
    private int DocksBetween(EntityUid grid, EntityUid other)
    {
        _docks.Clear();
        _lookup.GetChildEntities(grid, _docks);
        var count = 0;
        foreach (var dock in _docks)
        {
            if (dock.Comp.DockedWith is { } with && Transform(with).GridUid == other)
                count++;
        }

        return count;
    }

    private string NameOrUnknown(EntityUid uid)
    {
        return Name(uid) is { Length: > 0 } name ? name : _loc.GetString("wf-crew-radio-unnamed");
    }

    private static string Rounded(float value)
    {
        return ((int) MathF.Round(value / PositionRounding) * (int) PositionRounding).ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsAlert(WFRadioLine line)
    {
        return line is WFRadioLine.Mayday
            or WFRadioLine.Boarded
            or WFRadioLine.CaptainDown
            or WFRadioLine.HelmDown
            or WFRadioLine.BoardWarning
            or WFRadioLine.DockWarning
            or WFRadioLine.AllClear;
    }

    private static string LineId(WFRadioLine line)
    {
        return line switch
        {
            WFRadioLine.Docking => "docking",
            WFRadioLine.Undocking => "undocking",
            WFRadioLine.Jumping => "jumping",
            WFRadioLine.Arriving => "arriving",
            WFRadioLine.OnStation => "on-station",
            WFRadioLine.DockAborted => "dock-aborted",
            WFRadioLine.Mayday => "mayday",
            WFRadioLine.Boarded => "boarded",
            WFRadioLine.CaptainDown => "captain-down",
            WFRadioLine.HelmDown => "helm-down",
            WFRadioLine.AllClear => "all-clear",
            WFRadioLine.Approach => "approach",
            WFRadioLine.DockWarning => "dock-warning",
            WFRadioLine.BoardWarning => "board-warning",
            _ => throw new ArgumentOutOfRangeException(nameof(line), line, null),
        };
    }
}
