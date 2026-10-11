using System.Linq;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._WF.CombatConsole;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.Cockpit;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WF.Cockpit;

/// <summary>Links a seated cockpit pilot to a reachable, authorized gunnery console without opening another window.</summary>
public sealed partial class WFCockpitGunnerySystem : EntitySystem
{
    [Dependency] private SharedWFCockpitSystem _cockpit = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private FireControlSystem _fireControl = default!;
    [Dependency] private WFCombatConsoleSystem _combat = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    private readonly Dictionary<EntityUid, Session> _sessions = new();
    private float _accumulator;

    /// <summary>One pilot's gun link; Controlling means the pilot has GUNS open or weapons selected and may command them.</summary>
    private sealed class Session(EntityUid helm)
    {
        public readonly EntityUid Helm = helm;
        public EntityUid? Console;
        public bool Controlling;
        public bool SentState;
        public EntityUid? SentConsole;
        public FireControlConsoleBoundInterfaceState? State;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerDetachedEvent>(args =>
        {
            if (_sessions.TryGetValue(args.Entity, out var session))
                EndSession(args.Entity, session);
        });
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WFCockpitGunnerySessionMessage>(OnSession);
            subs.Event<WFCockpitGunneryCommandMessage>(OnCommand);
        });
    }

    private void OnSession(Entity<ShuttleConsoleComponent> ent, ref WFCockpitGunnerySessionMessage args) =>
        SetSession(args.Actor, ent, args.Active, args.Controlling);

    private void OnCommand(Entity<ShuttleConsoleComponent> ent, ref WFCockpitGunneryCommandMessage args) =>
        TryCommand(args.Actor, ent, args.Console, args.Command);

    /// <summary>Starts a validated helm session or releases only the requesting helm's existing link.</summary>
    public bool SetSession(EntityUid actor, EntityUid helm, bool active, bool controlling = false)
    {
        if (!active)
        {
            if (_sessions.TryGetValue(actor, out var previous) && previous.Helm == helm)
                EndSession(actor, previous);
            return true;
        }
        if (!CanMaintainSession(actor, helm))
            return false;
        if (!_sessions.TryGetValue(actor, out var session) || session.Helm != helm)
        {
            if (session != null)
                EndSession(actor, session);
            _sessions[actor] = session = new Session(helm);
            CaptureCamera(actor, helm);
        }
        session.Controlling = controlling;
        Refresh(actor, session);
        return true;
    }

    /// <summary>Returns a linked console only while the complete authorization still holds.</summary>
    public EntityUid? GetConsole(EntityUid actor) =>
        _sessions.TryGetValue(actor, out var session) && session.Console is { } console &&
        CanUseHelm(actor, session.Helm) && CanUseConsole(actor, session.Helm, console)
            ? console : null;

    /// <summary>Rechecks the seat, live helm subscription, reach, access and server ownership for every command.</summary>
    public bool CanOperate(EntityUid actor, EntityUid console) =>
        _sessions.TryGetValue(actor, out var session) && session.Console == console && session.Controlling &&
        CanUseHelm(actor, session.Helm) && CanUseConsole(actor, session.Helm, console);

    /// <summary>Lets existing gunnery telemetry and NPC handoff recognize authorized cockpit operators.</summary>
    public IEnumerable<EntityUid> GetActors(EntityUid console)
    {
        foreach (var actor in _sessions.Keys)
        {
            if (CanOperate(actor, console))
                yield return actor;
        }
    }

    /// <summary>Identifies telemetry subscribers without repeating physical authorization for a shared snapshot.</summary>
    public bool HasViewers(EntityUid console) => _sessions.Values.Any(session => session.Console == console);

    /// <summary>Dispatches a whitelisted native gunnery command only to the console advertised to this pilot.</summary>
    public bool TryCommand(EntityUid actor, EntityUid helm, NetEntity expectedConsole, BoundUserInterfaceMessage command)
    {
        if (!_sessions.TryGetValue(actor, out var session) || session.Helm != helm ||
            session.Console is not { } console || GetNetEntity(console) != expectedConsole || !session.Controlling)
            return false;
        if (!_fireControl.WfCockpitCommand(console, actor, command))
            return false;
        if (command is not FireControlConsoleFireMessage)
            SendState(actor, session, false);
        return true;
    }

    private bool CanMaintainSession(EntityUid actor, EntityUid helm) =>
        TryComp<ActorComponent>(actor, out var player) && player.PlayerSession.AttachedEntity == actor &&
        player.PlayerSession.Status == SessionStatus.InGame &&
        _cockpit.CanEnter(actor, helm) && _ui.IsUiOpen(helm, ShuttleConsoleUiKey.Key, actor);

    private bool CanUseHelm(EntityUid actor, EntityUid helm) =>
        CanMaintainSession(actor, helm) &&
        Transform(helm).Anchored && _power.IsPowered(helm) && HasAccess(actor, helm) &&
        _blocker.CanInteract(actor, helm) && _blocker.CanComplexInteract(actor) && InReach(actor, helm);

    private bool CanUseConsole(EntityUid actor, EntityUid helm, EntityUid console) =>
        CanAccessConsole(actor, helm, console) &&
        TryComp<FireControlConsoleComponent>(console, out var control) && _combat.TryGetServer(console, control, out _, out _);

    private bool CanAccessConsole(EntityUid actor, EntityUid helm, EntityUid console)
    {
        if (TerminatingOrDeleted(console) || !HasComp<FireControlConsoleComponent>(console) ||
            !TryComp<TransformComponent>(helm, out var helmTransform) || helmTransform.GridUid is not { } grid ||
            Transform(actor).GridUid != grid || Transform(console).GridUid != grid ||
            !Transform(console).Anchored || !_power.IsPowered(console) ||
            !_ui.HasUi(console, FireControlConsoleUiKey.Key) || !_blocker.CanInteract(actor, console) ||
            !InReach(actor, console) || !HasAccess(actor, console))
            return false;
        if (TryComp<ActivatableUIComponent>(console, out var activation) &&
            (activation.AdminOnly || activation.InHandsOnly || activation.RequiredItems != null ||
             activation.SingleUser && (activation.CurrentSingleUser is { } user && user != actor ||
                _sessions.Any(other => other.Key != actor && other.Value.Controlling && other.Value.Console == console && CanUseHelm(other.Key, other.Value.Helm)))))
            return false;
        return true;
    }

    /// <summary>Rechecks the native access rules without filling the audit history on every telemetry tick.</summary>
    private bool HasAccess(EntityUid actor, EntityUid target)
    {
        if (!TryComp<AccessReaderComponent>(target, out var reader) || !reader.Enabled)
            return true;
        var items = _access.FindPotentialAccessItems(actor);
        var tags = _access.FindAccessTags(actor, items);
        _access.FindStationRecordKeys(actor, out var keys, items);
        return _access.IsAllowed(tags, keys, target, reader);
    }

    /// <summary>Uses body fixtures and line of sight without allowing remote range overrides.</summary>
    private bool InReach(EntityUid actor, EntityUid target)
    {
        var transform = Transform(target);
        return _interaction.IsAccessible(actor, target) &&
            _interaction.InRangeUnobstructed(actor, target, transform.Coordinates, transform.LocalRotation);
    }

    private EntityUid? FindConsole(EntityUid actor, EntityUid helm)
    {
        EntityUid? nearest = null;
        var best = float.MaxValue;
        var origin = _transform.GetWorldPosition(actor);
        var query = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var control, out var transform))
        {
            if (!CanAccessConsole(actor, helm, uid))
                continue;
            if (!_combat.TryGetServer(uid, control, out _, out _))
                _fireControl.WfCockpitDiscoverConsole(uid, control);
            if (!_combat.TryGetServer(uid, control, out _, out _))
                continue;
            var distance = Vector2.DistanceSquared(origin, _transform.GetWorldPosition(transform));
            if (distance >= best)
                continue;
            best = distance;
            nearest = uid;
        }
        return nearest;
    }

    private void Refresh(EntityUid actor, Session session)
    {
        if (!CanUseHelm(actor, session.Helm))
        {
            session.Console = null;
            SendState(actor, session, false);
            return;
        }
        var previous = session.Console;
        if (session.Console is not { } console || !CanUseConsole(actor, session.Helm, console))
            session.Console = FindConsole(actor, session.Helm);
        if (session.Console is { } acquired && previous != acquired)
        {
            _access.IsAllowed(actor, session.Helm);
            _access.IsAllowed(actor, acquired);
        }
        SendState(actor, session);
    }

    private void SendState(EntityUid actor, Session session, bool refresh = true)
    {
        var console = session.Console;
        var state = console is { } uid ? _fireControl.WfCockpitState(uid, refresh) : null;
        if (session.SentState && session.SentConsole == console && ReferenceEquals(session.State, state))
            return;
        session.SentState = true;
        session.SentConsole = console;
        session.State = state;
        _ui.ServerSendUiMessage(session.Helm, ShuttleConsoleUiKey.Key,
            new WFCockpitGunneryStateMessage(GetNetEntity(console), state), actor);
    }

    private void EndSession(EntityUid actor, Session session)
    {
        _sessions.Remove(actor);
        RestoreCamera(actor);
        if (!TerminatingOrDeleted(session.Helm))
            _ui.ServerSendUiMessage(session.Helm, ShuttleConsoleUiKey.Key, new WFCockpitGunneryStateMessage(null, null), actor);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < 0.25f)
            return;
        _accumulator = 0;
        foreach (var (actor, session) in _sessions.ToArray())
        {
            if (!CanMaintainSession(actor, session.Helm))
            {
                EndSession(actor, session);
                continue;
            }
            Refresh(actor, session);
        }
    }
}
