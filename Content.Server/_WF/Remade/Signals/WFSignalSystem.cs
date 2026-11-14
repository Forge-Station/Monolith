using System.Linq;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Ghost;
using Content.Server.Ghost.Roles.Events;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Signals;
using Content.Shared.Actions;
using Content.Shared.Database;
using Content.Shared.Eye;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Signals;

/// <summary>
/// The Signals: dead players a Monolith holds as unseen eyes. A stone at the Hymn or above offers a ghost role through a gate;
/// whoever takes it flies freely near the stone, its ship and its corruption, spends Psi on tricks and can drive a Remade.
/// Split into partials: leash, tricks, possession.
/// </summary>
public sealed partial class WFSignalSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VisibilitySystem _visibility = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;

    public const string GateProto = "WFSignalGate";
    public const string PossessAction = "WFSignalActionPossess";
    public const string ReleaseAction = "WFSignalActionRelease";

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeashRate = TimeSpan.FromSeconds(0.25);

    private TimeSpan _nextTick;
    private TimeSpan _nextLeash;
    private readonly List<EntityUid> _eject = new();

    public override void Initialize()
    {
        base.Initialize();
        InitializeLeash();
        InitializeTricks();
        InitializePossession();

        SubscribeLocalEvent<WFSignalComponent, ComponentStartup>(OnSignalStartup);
        SubscribeLocalEvent<WFSignalComponent, GetVisMaskEvent>(OnSignalVisMask);
        SubscribeLocalEvent<WFSignalComponent, GhostRoleSpawnerUsedEvent>(OnSpawned);
        SubscribeLocalEvent<WFSignalComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<WFSignalComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<WFSignalComponent, EntityTerminatingEvent>(OnSignalTerminating);
        SubscribeLocalEvent<WFSignalStoneComponent, EntityTerminatingEvent>(OnStoneTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextTick)
        {
            _nextTick = now + TickRate;
            Tick();
        }

        if (now >= _nextLeash)
        {
            _nextLeash = now + LeashRate;
            Leash(now);
        }

        UpdateDriven(frameTime);
        UpdateBolts(now);
    }

    /// <summary>An unseen thing: the living don't see it, and it sees the ghosts.</summary>
    private void OnSignalStartup(Entity<WFSignalComponent> ent, ref ComponentStartup args)
    {
        var visibility = EnsureComp<VisibilityComponent>(ent);
        _visibility.AddLayer((ent, visibility), (int) VisibilityFlags.Ghost, false);
        _visibility.RemoveLayer((ent, visibility), (int) VisibilityFlags.Normal, false);
        _visibility.RefreshVisibility(ent, visibilityComponent: visibility);
        _eye.RefreshVisibilityMask(ent.Owner);
    }

    private void OnSignalVisMask(Entity<WFSignalComponent> ent, ref GetVisMaskEvent args)
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.VisibilityMask |= (int) VisibilityFlags.Ghost;
    }

    /// <summary>The gate spawned a Signal: bind it to the stone, give it its tricks, and shut the gate if the stone is now full.</summary>
    private void OnSpawned(EntityUid uid, WFSignalComponent signal, GhostRoleSpawnerUsedEvent args)
    {
        if (!TryComp<WFSignalGateComponent>(args.Spawner, out var gate)
            || gate.Stone is not { } stone
            || !TryComp<WFSignalStoneComponent>(stone, out var held)
            || !TryComp<WFMonolithComponent>(stone, out var monolith))
        {
            QueueDel(uid);
            return;
        }

        signal.Stone = stone;
        signal.Stage = monolith.Stage;
        held.Signals.Add(uid);
        GrantTricks((uid, signal));
        Dirty(uid, signal);

        if (held.Signals.Count >= WFSignalMath.Cap(monolith.Stage))
            SetGateOpen(args.Spawner, false);
    }

    private void OnMindAdded(EntityUid uid, WFSignalComponent signal, MindAddedMessage args)
    {
        if (signal.Stone is not { } stone)
            return;

        _adminLog.Add(LogType.Mind, LogImpact.Medium, $"{ToPrettyString(uid):player} joined {ToPrettyString(stone):stone} as a signal");
    }

    /// <summary>The player left the Signal for good, such as by ghosting: the Signal goes and its place opens.</summary>
    private void OnMindRemoved(EntityUid uid, WFSignalComponent signal, MindRemovedMessage args)
    {
        if (TerminatingOrDeleted(uid))
            return;

        _adminLog.Add(LogType.Mind, LogImpact.Low, $"{ToPrettyString(uid):player} stopped being a signal");
        Forget(uid, signal);
        QueueDel(uid);
    }

    private void OnSignalTerminating(EntityUid uid, WFSignalComponent signal, ref EntityTerminatingEvent args)
    {
        Forget(uid, signal);
    }

    /// <summary>A stone going leaves nobody to answer to: its Signals go back to being ghosts.</summary>
    private void OnStoneTerminating(EntityUid uid, WFSignalStoneComponent held, ref EntityTerminatingEvent args)
    {
        if (held.Gate is { } gate)
            QueueDel(gate);

        foreach (var signal in held.Signals.ToArray())
        {
            Eject(signal, "wf-signal-stone-gone");
        }
    }

    private void Forget(EntityUid uid, WFSignalComponent signal)
    {
        if (signal.Stone is { } stone && TryComp<WFSignalStoneComponent>(stone, out var held))
            held.Signals.Remove(uid);
    }

    /// <summary>Whether the Signal still answers to a stone that is there and can hold it.</summary>
    public bool Bound(Entity<WFSignalComponent> ent, out Entity<WFMonolithComponent> stone)
    {
        stone = default;
        if (ent.Comp.Stone is not { } uid || TerminatingOrDeleted(uid) || !TryComp<WFMonolithComponent>(uid, out var monolith))
            return false;

        stone = (uid, monolith);
        return true;
    }

    /// <summary>Whether a stone can take Signals now: switched on, at the Hymn or above, and not on or near a main station.</summary>
    public bool CanHold(EntityUid stone, WFMonolithComponent monolith)
    {
        return _config.GetCVar(WFSignalCVars.Enabled)
               && !monolith.Local
               && monolith.Stage >= WFMonolithStage.Hymn
               && !_corruption.IsSanctuaryAt(stone);
    }

    private void Tick()
    {
        var stones = EntityQueryEnumerator<WFMonolithComponent>();
        while (stones.MoveNext(out var uid, out var monolith))
        {
            if (!CanHold(uid, monolith))
            {
                if (TryComp<WFSignalStoneComponent>(uid, out var closed))
                    Close((uid, closed));

                continue;
            }

            var held = EnsureComp<WFSignalStoneComponent>(uid);
            held.Signals.RemoveWhere(signal => TerminatingOrDeleted(signal));
            var gate = EnsureGate((uid, held));
            SetGateOpen(gate, held.Signals.Count < WFSignalMath.Cap(monolith.Stage));
        }

        var gates = EntityQueryEnumerator<WFSignalGateComponent>();
        while (gates.MoveNext(out var uid, out var gate))
        {
            if (gate.Stone is not { } stone || TerminatingOrDeleted(stone))
                QueueDel(uid);
        }

        _eject.Clear();
        var signals = EntityQueryEnumerator<WFSignalComponent>();
        while (signals.MoveNext(out var uid, out var signal))
        {
            if (!Bound((uid, signal), out var stone))
            {
                _eject.Add(uid);
                continue;
            }

            signal.Psi = Math.Min(signal.Psi + WFSignalMath.PsiRate(stone.Comp.Stage), WFSignalMath.MaxPsi);
            signal.Stage = stone.Comp.Stage;
            Dirty(uid, signal);
        }

        foreach (var uid in _eject)
        {
            Eject(uid, "wf-signal-stone-gone");
        }
    }

    /// <summary>A stone that can no longer hold Signals lets them all go and shuts its gate.</summary>
    private void Close(Entity<WFSignalStoneComponent> stone)
    {
        foreach (var signal in stone.Comp.Signals.ToArray())
        {
            Eject(signal, "wf-signal-stone-quiet");
        }

        if (stone.Comp.Gate is { } gate && !TerminatingOrDeleted(gate))
            SetGateOpen(gate, false);
    }

    private EntityUid EnsureGate(Entity<WFSignalStoneComponent> stone)
    {
        if (stone.Comp.Gate is { } existing && !TerminatingOrDeleted(existing))
        {
            // Signals appear where the gate is, so it follows the stone if the stone is moved. It isn't parented to it:
            // a child of a Scion upsets the corruption's spread.
            var at = Transform(stone).Coordinates;
            if (Transform(existing).Coordinates != at)
                _transform.SetCoordinates(existing, at);

            return existing;
        }

        var gate = Spawn(GateProto, Transform(stone).Coordinates);
        EnsureComp<WFSignalGateComponent>(gate).Stone = stone;
        stone.Comp.Gate = gate;
        return gate;
    }

    /// <summary>Opens or shuts the ghost role. A paused role isn't listed and can't be taken.</summary>
    private void SetGateOpen(EntityUid gate, bool open)
    {
        if (!TerminatingOrDeleted(gate) && MetaData(gate).EntityPaused == open)
            _meta.SetEntityPaused(gate, !open);
    }

    /// <summary>Lets a Signal go: its player becomes a ghost where it was, and the Signal is gone.</summary>
    public void Eject(EntityUid signal, string reason)
    {
        if (!TryComp<WFSignalComponent>(signal, out var comp) || TerminatingOrDeleted(signal))
            return;

        if (comp.Possessing is { } driven)
            Release(driven);

        Forget(signal, comp);
        if (_mind.TryGetMind(signal, out var mindId, out var mind))
        {
            if (mind.UserId is { } userId && _players.TryGetSessionById(userId, out var session))
                _chat.DispatchServerMessage(session, Loc.GetString(reason), true);

            _adminLog.Add(LogType.Mind, LogImpact.Low, $"{ToPrettyString(signal):player} was let go as a signal");
            _ghost.SpawnGhost((mindId, mind), Transform(signal).Coordinates);
        }

        QueueDel(signal);
    }

    /// <summary>Tells the Signal why it can't, in words.</summary>
    private void Refuse(EntityUid signal, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), signal, signal);
    }
}
