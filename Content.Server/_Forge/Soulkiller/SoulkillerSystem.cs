// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Server.Storage.Components;
using Content.Shared._CorvaxNext.Silicons.Borgs;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared._Forge.Soulkiller;
using Content.Shared.Actions;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Silicons.StationAi;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._Forge.Soulkiller;

/// <summary>
/// Implements the "Душегуб" mechanic: an IPC (КПБ) lies down inside a cryo-style capsule connector.
/// Closing the capsule moves their mind into a Station-AI core, turning them into a fully-functional
/// station AI (eye, cameras, jump-to-core, borg remote control) while their real body is sealed inside
/// the capsule. Unlike a real cryopod, anyone can crack the capsule open from outside to forcibly rip
/// the operator out (returning their mind and ejecting the body); the operator can also leave via the
/// "return to body" action. The connection also breaks if the core loses power, is destroyed, or the
/// body dies / enters crit.
///
/// Uses real mind transfer (<see cref="SharedMindSystem.TransferTo"/>) rather than visiting, so the
/// station-AI borg-control flow (which itself transfers the mind) works natively.
/// </summary>
public sealed class SoulkillerSystem : SharedSoulkillerSystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedAiRemoteControlSystem _aiRemote = default!;
    [Dependency] private readonly SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    private const string SoulkillerLinkPort = "SoulkillerLink";

    /// <summary>Connectors currently being opened from code (disconnect / extract) — skip the delay.</summary>
    private readonly HashSet<EntityUid> _instantOpenConnectors = new();

    public override void Initialize()
    {
        base.Initialize();

        // The connector is a cryo-style capsule: closing it on an IPC connects them; opening it
        // (by anyone) forcibly breaks the connection and ejects the body.
        SubscribeLocalEvent<SoulkillerConnectorComponent, StorageAfterCloseEvent>(OnPodClosed);
        SubscribeLocalEvent<SoulkillerConnectorComponent, StorageOpenAttemptEvent>(OnPodOpenAttempt);
        SubscribeLocalEvent<SoulkillerConnectorComponent, StorageBeforeOpenEvent>(OnPodOpening);
        SubscribeLocalEvent<SoulkillerConnectorComponent, StorageAfterOpenEvent>(OnPodOpened);
        SubscribeLocalEvent<SoulkillerConnectorComponent, SoulkillerExtractDoAfterEvent>(OnExtractDoAfter);
        SubscribeLocalEvent<SoulkillerConnectorComponent, EntityTerminatingEvent>(OnConnectorTerminating);

        // Multitool linking: a capsule only works with a core it's been explicitly linked to.
        SubscribeLocalEvent<SoulkillerConnectorComponent, NewLinkEvent>(OnConnectorLinked);
        SubscribeLocalEvent<SoulkillerConnectorComponent, PortDisconnectedEvent>(OnConnectorUnlinked);

        SubscribeLocalEvent<SoulkillerInhabitantComponent, SoulkillerReturnToBodyEvent>(OnReturnToBody);
        SubscribeLocalEvent<SoulkillerInhabitantComponent, SoulkillerJumpToServerEvent>(OnJumpToServer);

        SubscribeLocalEvent<SoulkillerComponent, EntityTerminatingEvent>(OnCoreTerminating);
        SubscribeLocalEvent<SoulkillerComponent, PowerChangedEvent>(OnCorePowerChanged);

        SubscribeLocalEvent<SoulkillerTetheredBodyComponent, MobStateChangedEvent>(OnBodyMobStateChanged);

        // Borg controlled via a Soulkiller AI dies → kick the mind back up to the AI core.
        SubscribeLocalEvent<AiRemoteControllerComponent, MobStateChangedEvent>(OnControlledBorgMobState);
    }

    // --- Capsule open/close drive the connection ---

    /// <summary>
    /// Capsule sealed shut: if an IPC is inside, move their mind into a Soulkiller core.
    /// </summary>
    private void OnPodClosed(Entity<SoulkillerConnectorComponent> ent, ref StorageAfterCloseEvent args)
    {
        var connected = false;
        var hasHumanoid = false;

        if (TryComp<EntityStorageComponent>(ent, out var storage))
        {
            foreach (var occupant in storage.Contents.ContainedEntities)
            {
                hasHumanoid |= HasComp<HumanoidAppearanceComponent>(occupant);

                if (TryConnect(ent, occupant))
                {
                    connected = true;
                    break;
                }
            }
        }

        // A person the capsule can't connect (non-КПБ, no linked core, core occupied) is spat
        // back out immediately instead of being sealed behind the extraction delay.
        if (!connected && hasHumanoid)
        {
            OpenCapsule(ent.Owner);
            return;
        }

        SetConnectorVisual(ent, connected ? SoulkillerConnectorState.Active : SoulkillerConnectorState.Closed);
    }

    /// <summary>
    /// Capsule is being cracked open: forcibly return any inhabiting mind before the body is ejected.
    /// This is how someone "rips" an operator out of the Soulkiller.
    /// </summary>
    private void OnPodOpening(Entity<SoulkillerConnectorComponent> ent, ref StorageBeforeOpenEvent args)
    {
        if (TryGetConnectedCore(ent, out var core))
            Disconnect(core, openPod: false);
    }

    private void OnPodOpened(Entity<SoulkillerConnectorComponent> ent, ref StorageAfterOpenEvent args)
    {
        SetConnectorVisual(ent, SoulkillerConnectorState.Open);
    }

    /// <summary>
    /// Trying to crack open a capsule with someone sealed inside → require a 30s extraction do-after.
    /// Opens initiated from code (return-to-body, disconnect, the finished do-after) bypass this.
    /// </summary>
    private void OnPodOpenAttempt(Entity<SoulkillerConnectorComponent> ent, ref StorageOpenAttemptEvent args)
    {
        if (args.Cancelled || _instantOpenConnectors.Contains(ent.Owner))
            return;

        // Empty capsule → open instantly.
        if (!TryComp<EntityStorageComponent>(ent, out var storage) || storage.Contents.ContainedEntities.Count == 0)
            return;

        // Occupied → ripping the operator out takes time.
        args.Cancelled = true;

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.ExtractTime, new SoulkillerExtractDoAfterEvent(), ent.Owner, target: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            CancelDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        if (!args.Silent)
            _popup.PopupEntity(Loc.GetString("soulkiller-capsule-extracting"), ent, args.User);

        // Warn the operator inhabiting the AI that someone is ripping them out.
        if (TryGetConnectedCore(ent, out var core) && core.Comp.SpawnedBrain is { } brain)
        {
            var at = TryComp<StationAiCoreComponent>(core, out var aiCore) && aiCore.RemoteEntity is { } eye
                ? eye
                : brain;
            _popup.PopupEntity(Loc.GetString("soulkiller-being-extracted"), at, brain, PopupType.LargeCaution);
        }
    }

    private void OnExtractDoAfter(Entity<SoulkillerConnectorComponent> ent, ref SoulkillerExtractDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        OpenCapsule(ent.Owner);
    }

    /// <summary>
    /// Opens the capsule from code, bypassing the extraction do-after gate.
    /// </summary>
    private void OpenCapsule(EntityUid connector)
    {
        if (!TryComp<EntityStorageComponent>(connector, out var storage) || storage.Open)
            return;

        _instantOpenConnectors.Add(connector);
        _entityStorage.OpenStorage(connector, storage);
        _instantOpenConnectors.Remove(connector);
    }

    /// <summary>
    /// Capsule destroyed while connected → break the connection (the body is ejected by the storage).
    /// </summary>
    private void OnConnectorTerminating(Entity<SoulkillerConnectorComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryGetConnectedCore(ent, out var core))
            Disconnect(core, openPod: false);
    }

    /// <summary>
    /// Capsule linked to a core with a multitool → remember that core as the connection target.
    /// </summary>
    private void OnConnectorLinked(EntityUid uid, SoulkillerConnectorComponent component, NewLinkEvent args)
    {
        if (args.SourcePort != SoulkillerLinkPort || !HasComp<SoulkillerComponent>(args.Sink))
            return;

        component.LinkedSoulkiller = args.Sink;
    }

    /// <summary>
    /// Capsule unlinked → forget the core (connecting is no longer possible until re-linked).
    /// </summary>
    private void OnConnectorUnlinked(EntityUid uid, SoulkillerConnectorComponent component, PortDisconnectedEvent args)
    {
        if (args.Port == SoulkillerLinkPort)
            component.LinkedSoulkiller = null;
    }

    /// <summary>
    /// Spawns a brain into the connector's linked (or nearest free) core and moves the user's mind
    /// into it. The user's body stays sealed inside the capsule. Returns true if a connection was made.
    /// </summary>
    private bool TryConnect(Entity<SoulkillerConnectorComponent> connector, EntityUid user)
    {
        if (!_mind.TryGetMind(user, out var mindId, out var mind))
            return false;

        if (mind.VisitingEntity != null)
            return false;

        if (!TryResolveSoulkiller(connector, out var core))
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-no-shell"), connector, user);
            return false;
        }

        // КПБ-only: the operator must be of the required species (IPC).
        if (!TryComp<HumanoidAppearanceComponent>(user, out var humanoid)
            || humanoid.Species != core.Comp.RequiredSpecies)
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-wrong-species"), connector, user);
            return false;
        }

        var container = _container.EnsureContainer<ContainerSlot>(core, core.Comp.MindSlotContainerId);
        if (core.Comp.InhabitingMind != null || container.ContainedEntity != null)
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-occupied"), connector, user);
            return false;
        }

        // Spawn a brain into the core — inserting it grants the AiHeld components (full station AI).
        var brain = Spawn(core.Comp.BrainProto, Transform(core).Coordinates);
        if (!_container.Insert(brain, container))
        {
            Del(brain);
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-occupied"), connector, user);
            return false;
        }

        var inhabitant = EnsureComp<SoulkillerInhabitantComponent>(brain);
        inhabitant.Core = core;

        core.Comp.SpawnedBrain = brain;
        core.Comp.InhabitingMind = mindId;
        core.Comp.TetheredBody = user;
        core.Comp.Connector = connector;
        Dirty(core);

        // Tag the sealed body so we can track its death / return it later (the capsule holds it).
        TagBody(user, core);

        // Move the mind into the AI brain (real transfer → borg control etc. work natively).
        _mind.TransferTo(mindId, brain, mind: mind);

        // Grant the "return to body" action on the brain so the inhabitant can leave.
        _actions.AddAction(brain, ref core.Comp.ReturnActionEntity, core.Comp.ReturnAction);

        _popup.PopupEntity(Loc.GetString("soulkiller-connector-connected"), core, user);
        return true;
    }

    /// <summary>
    /// Tags the sealed body so death / return tracking works. The capsule itself holds the body, so
    /// no anchoring is needed.
    /// </summary>
    private void TagBody(EntityUid body, Entity<SoulkillerComponent> core)
    {
        var tag = EnsureComp<SoulkillerTetheredBodyComponent>(body);
        tag.Core = core;
    }

    private void ReleaseBody(EntityUid body)
    {
        RemComp<SoulkillerTetheredBodyComponent>(body);
    }

    private void OnReturnToBody(Entity<SoulkillerInhabitantComponent> ent, ref SoulkillerReturnToBodyEvent args)
    {
        args.Handled = true;

        if (TryComp<SoulkillerComponent>(ent.Comp.Core, out var core))
            Disconnect((ent.Comp.Core, core));
    }

    /// <summary>
    /// Jumps the AI's eye to a server linked to its core (e.g. on a remote shuttle), letting the
    /// operator view and interact around the server. The "jump to core" action brings it back.
    /// </summary>
    private void OnJumpToServer(Entity<SoulkillerInhabitantComponent> ent, ref SoulkillerJumpToServerEvent args)
    {
        args.Handled = true;

        var core = ent.Comp.Core;

        // Find a server linked to this core through the multitool device-link.
        EntityUid? server = null;
        if (TryComp<DeviceLinkSinkComponent>(core, out var sink))
        {
            foreach (var source in sink.LinkedSources)
            {
                if (!Deleted(source) && HasComp<SoulkillerServerComponent>(source))
                {
                    server = source;
                    break;
                }
            }
        }

        if (server is not { } target)
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-no-server"), ent, ent);
            return;
        }

        if (!TryComp<StationAiCoreComponent>(core, out var aiCore) || aiCore.RemoteEntity is not { } eye)
            return;

        _xform.DropNextTo(eye, target);
    }

    private void OnCoreTerminating(Entity<SoulkillerComponent> ent, ref EntityTerminatingEvent args)
    {
        Disconnect((ent, ent.Comp), coreTerminating: true);
    }

    /// <summary>
    /// Core lost power → break the connection.
    /// </summary>
    private void OnCorePowerChanged(Entity<SoulkillerComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered && ent.Comp.InhabitingMind != null)
            Disconnect((ent, ent.Comp));
    }

    /// <summary>
    /// Operator's real body died or entered crit → break the connection.
    /// </summary>
    private void OnBodyMobStateChanged(Entity<SoulkillerTetheredBodyComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        if (TryComp<SoulkillerComponent>(ent.Comp.Core, out var core))
            Disconnect((ent.Comp.Core, core));
    }

    /// <summary>
    /// A borg controlled through a Soulkiller AI died/crit → return the mind up one level to the AI
    /// core (not all the way home). Vanilla AI-controlled borgs are left untouched.
    /// </summary>
    private void OnControlledBorgMobState(Entity<AiRemoteControllerComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        if (ent.Comp.LinkedMind == null
            || ent.Comp.AiHolder is not { } holder
            || !HasComp<SoulkillerInhabitantComponent>(holder))
            return;

        _aiRemote.ReturnMindIntoAi(ent);
    }

    /// <summary>
    /// Ends a connection: returns the inhabiting mind to its real body, releases the body, removes the
    /// spawned brain so the core empties, and (unless the capsule is already being opened) cracks the
    /// capsule open to eject the body. Handles the case where the mind is currently off in a
    /// remote-controlled borg by clearing that link first.
    /// </summary>
    private void Disconnect(Entity<SoulkillerComponent> core, bool coreTerminating = false, bool openPod = true)
    {
        var mindId = core.Comp.InhabitingMind;
        var body = core.Comp.TetheredBody;
        var brain = core.Comp.SpawnedBrain;
        var connector = core.Comp.Connector;

        if (mindId is { } mind)
        {
            // If the mind is currently controlling a borg (not sitting in the brain), clear that
            // borg's remote-control link so it isn't left half-possessed when we pull the mind home.
            if (TryComp<MindComponent>(mind, out var mindComp)
                && mindComp.CurrentEntity is { } current
                && current != brain
                && TryComp<AiRemoteControllerComponent>(current, out var remote))
            {
                remote.AiHolder = null;
                remote.LinkedMind = null;
            }

            // Return the mind to the operator's real body.
            if (body is { } bodyUid && !Deleted(bodyUid))
                _mind.TransferTo(mind, bodyUid, ghostCheckOverride: true);
        }

        if (body is { } b)
            ReleaseBody(b);

        if (brain is { } br && !Deleted(br))
            QueueDel(br);

        core.Comp.ReturnActionEntity = null;
        core.Comp.SpawnedBrain = null;
        core.Comp.InhabitingMind = null;
        core.Comp.TetheredBody = null;
        core.Comp.Connector = null;

        if (!coreTerminating && !Terminating(core))
            Dirty(core);

        // Crack the capsule open to release the body (skip when we're already mid-open).
        // Bypasses the 30s extraction delay — returning home / power loss / death is immediate.
        if (openPod
            && connector is { } conn
            && !Deleted(conn)
            && !Terminating(conn))
        {
            OpenCapsule(conn);
        }
    }

    private void SetConnectorVisual(EntityUid connector, SoulkillerConnectorState state)
    {
        _appearance.SetData(connector, SoulkillerConnectorVisuals.State, state);
    }

    /// <summary>
    /// Finds the core currently connected through this capsule, if any.
    /// </summary>
    private bool TryGetConnectedCore(EntityUid connector, out Entity<SoulkillerComponent> core)
    {
        core = default;

        // Resolve the core wired to this capsule via the device-link.
        if (!TryComp<SoulkillerConnectorComponent>(connector, out var conn)
            || conn.LinkedSoulkiller is not { } linked
            || !TryComp<SoulkillerComponent>(linked, out var comp))
            return false;

        // Only "connected" if a mind is actively inhabiting that core through this capsule.
        if (comp.InhabitingMind == null || comp.Connector != connector)
            return false;

        core = (linked, comp);
        return true;
    }

    /// <summary>
    /// Resolves the core to use: the explicit link if valid, otherwise the nearest free core.
    /// </summary>
    private bool TryResolveSoulkiller(Entity<SoulkillerConnectorComponent> connector, out Entity<SoulkillerComponent> core)
    {
        core = default;

        // Connection is only possible through an explicit multitool link to a core.
        if (connector.Comp.LinkedSoulkiller is { } linked
            && !Deleted(linked)
            && TryComp<SoulkillerComponent>(linked, out var linkedComp))
        {
            core = (linked, linkedComp);
            return true;
        }

        return false;
    }
}
