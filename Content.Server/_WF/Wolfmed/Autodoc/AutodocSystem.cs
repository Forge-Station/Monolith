using Content.Shared.Body.Organ;
using Content.Shared.Item;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Medical;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.EntitySystems;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DragDrop;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Content.Shared.StatusEffect;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SharedToolSystem = Content.Shared.Tools.Systems.SharedToolSystem;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Bed.Sleep;
using Content.Shared.DoAfter;

namespace Content.Server._WF.Wolfmed.Autodoc;

/// <summary>S.A.M., the surgical pod: runs real Shitmed surgery steps on its occupant with built-in tools.</summary>
// Every wound, condition and side effect of a hand-performed surgery still applies. It can only perform surgeries
// and push reagents from its reservoir; it never bandages, heals by fiat or applies a topical.
public sealed partial class AutodocSystem : EntitySystem
{
    /// <summary>The status effect key every sleep chem in the game uses, so the pod stacks with them.</summary>
    private const string SleepKey = "ForcedSleep";

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private Life.WolfmedLifeSystem _life = default!; // BRAIN
    [Dependency] private Life.WolfmedRevivalSystem _revival = default!; // BRAIN
    [Dependency] private Consciousness.WolfmedConsciousnessSystem _consciousness = default!; // M1a: faints
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!; // Playtest 5: the tile sweep
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EmagSystem _emag = default!;
    [Dependency] private HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private WolfmedPainReliefSystem _relief = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SurgerySystem _surgery = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private WolfmedEmbeddedObjectSystem _embedded = default!;
    [Dependency] private WolfmedWoundDamageSyncSystem _damageSync = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedMapSystem _map = default!; // Playtest 3 SAM: eject beside the pod
    [Dependency] private TurfSystem _turf = default!; // Playtest 3 SAM

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AutodocComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AutodocComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        // Playtest 3 SAM: before construction's drag-drop and the climb, which both answer a drop on any machine.
        SubscribeLocalEvent<AutodocComponent, AutodocInsertDoAfterEvent>(OnInsertDoAfter);
        SubscribeLocalEvent<AutodocComponent, DragDropTargetEvent>(OnDragDrop,
            before: new[] { typeof(Content.Server._Goobstation.DragDrop.GoobDragDropSystem), typeof(Content.Shared.Climbing.Systems.ClimbSystem) });
        SubscribeLocalEvent<AutodocComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<AutodocComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<AutodocComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<AutodocComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<AutodocComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<AutodocComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<AutodocComponent, WolfmedSurgeryToolsEvent>(OnGetTools);
        SubscribeLocalEvent<AutodocComponent, DamageChangedEvent>(OnDamaged);

        SubscribeLocalEvent<Wounds.WolfmedWoundReplacedEvent>(OnWoundReplaced); // Playtest 3 SAM

        InitializeUi();
        InitializeTriage();
        InitializeAtmosphere(); // Playtest 3: the pod's own air
        InitializeBlood(); // Playtest 4, IV: the blood reservoir
        InitializeAntibiotics(); // Playtest 5: the antibiotic course
    }

    private void OnMapInit(Entity<AutodocComponent> ent, ref MapInitEvent args)
    {
        var tools = _containers.EnsureContainer<Container>(ent, AutodocComponent.ToolContainerId);
        foreach (var proto in ent.Comp.Tools)
        {
            var tool = Spawn(proto, Transform(ent).Coordinates);
            if (!_containers.Insert(tool, tools))
                QueueDel(tool);
        }

        // Nothing in the tray until a step asks for something. A bare component (a test adds each one alone)
        // has no slots to lock.
        if (TryComp<ItemSlotsComponent>(ent, out var slots))
            _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, true, slots);
        UpdateAppearance(ent);
    }

    #region Occupancy

    public EntityUid? GetOccupant(Entity<AutodocComponent> ent) =>
        _containers.TryGetContainer(ent, AutodocComponent.BodyContainerId, out var container) &&
        container.ContainedEntities.Count > 0
            ? container.ContainedEntities[0]
            : null;

    /// <summary>Puts a body in the empty pod and closes the lid.</summary>
    public bool TryInsert(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (GetOccupant(ent) != null ||
            !HasComp<BodyComponent>(body) ||
            !_containers.TryGetContainer(ent, AutodocComponent.BodyContainerId, out var container) ||
            !_containers.Insert(body, container))
            return false;

        _audio.PlayPvs(ent.Comp.LidCloseSound, ent);
        return true;
    }

    /// <summary>Slides the occupant off the pod unless it is locked or <paramref name="force"/> is set.</summary>
    public bool TryEject(Entity<AutodocComponent> ent, bool force = false)
    {
        if (ent.Comp.Locked && !force)
            return false;

        if (GetOccupant(ent) is not { } body ||
            !_containers.TryGetContainer(ent, AutodocComponent.BodyContainerId, out var container))
            return false;

        _containers.Remove(body, container);
        // Playtest 3 SAM: off the lid, not onto it. The pod is not climbable, so the old climb was a no-op and the
        // body lay on the pod's own tile, under the lid of the next occupant, looking like a second patient.
        SlideOff(ent, body);
        _audio.PlayPvs(ent.Comp.LidOpenSound, ent);
        return true;
    }

    /// <summary>Seconds between sweeps of the pod's tile.</summary>
    private static readonly TimeSpan TileSweep = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Playtest 5: anything loose on the pod's own tile is put beside the pod. What comes off the patient inside (a
    /// limb, an organ, a spent round, a garment the tray could not take) drops where the patient is, under the pod's
    /// sprite and out of reach. Once a second, cheap: one lookup on one tile.
    /// </summary>
    private void SweepTile(Entity<AutodocComponent> ent)
    {
        if (_timing.CurTime < ent.Comp.NextTileSweep)
            return;

        ent.Comp.NextTileSweep = _timing.CurTime + TileSweep;
        var xform = Transform(ent.Owner);
        if (xform.GridUid == null)
            return;

        _onTile.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, 0.4f, _onTile, LookupFlags.Uncontained | LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var loose in _onTile)
        {
            if (loose == ent.Owner || TerminatingOrDeleted(loose) || _containers.IsEntityInContainer(loose) ||
                !(HasComp<ItemComponent>(loose) || HasComp<BodyPartComponent>(loose) || HasComp<OrganComponent>(loose)))
                continue;

            SlideOff(ent, loose);
        }

        // An organ the pod took out has no hands to go to, and the engine's drop puts it into the container its part
        // sat in: inside the patient, out of reach. Anything detached that is still in one of the patient's containers
        // comes out beside the pod too.
        if (GetOccupant(ent) is not { } occupant)
            return;

        _loose.Clear();
        foreach (var container in _containers.GetAllContainers(occupant))
        {
            foreach (var held in container.ContainedEntities)
            {
                if (CompOrNull<OrganComponent>(held)?.Body == null && HasComp<OrganComponent>(held) ||
                    CompOrNull<BodyPartComponent>(held)?.Body == null && HasComp<BodyPartComponent>(held))
                    _loose.Add((held, container));
            }
        }

        foreach (var (held, container) in _loose)
        {
            _containers.Remove(held, container, destination: xform.Coordinates);
            SlideOff(ent, held);
        }
    }

    private readonly List<(EntityUid, BaseContainer)> _loose = new();

    private readonly HashSet<EntityUid> _onTile = new();

    /// <summary>Playtest 5: whatever the pod stowed in its tray goes out with the patient, so nothing stays locked inside.</summary>
    private void ReturnTray(Entity<AutodocComponent> ent)
    {
        if (_slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId) is not { } item)
            return;

        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, false);
        if (_slots.TryEject(ent.Owner, AutodocComponent.TraySlotId, null, out _))
            SlideOff(ent, item);
    }

    /// <summary>The pod's front first, then its sides, then behind it.</summary>
    private static readonly Direction[] EjectDirections =
        { Direction.South, Direction.East, Direction.West, Direction.North };

    /// <summary>
    /// Puts an ejected body on the first open floor tile beside the pod. Space and anything a mob would bump into
    /// are skipped; with every side blocked, or off a grid, the body stays where the container put it.
    /// </summary>
    private void SlideOff(Entity<AutodocComponent> ent, EntityUid body)
    {
        var xform = Transform(ent.Owner);
        if (xform.GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return;

        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        foreach (var direction in EjectDirections)
        {
            var target = tile.Offset(xform.LocalRotation.RotateDir(direction));
            if (!_map.TryGetTileRef(grid, gridComp, target, out var turf) || turf.Tile.IsEmpty ||
                _turf.IsSpace(turf) || _turf.IsTileBlocked(turf, CollisionGroup.MobMask))
                continue;

            _xform.SetCoordinates(body, _map.GridTileToLocal(grid, gridComp, target));
            return;
        }
    }

    private void OnGetVerbs(Entity<AutodocComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        var occupant = GetOccupant(ent);

        if (occupant == null && HasComp<BodyComponent>(user))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => EnterPod(ent, user),
                Text = Loc.GetString("wolfmed-autodoc-verb-enter"),
                Priority = 2,
            });
        }

        if (occupant != null && !ent.Comp.Locked)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => TryEject(ent),
                Text = Loc.GetString("wolfmed-autodoc-verb-eject"),
                Priority = 1,
            });
        }
    }

    private void EnterPod(Entity<AutodocComponent> ent, EntityUid user)
    {
        // An unconscious body cannot climb in by itself; the action blocker has already refused the verb for
        // one. Downed is deliberately allowed, which is what WolfmedDownedReachable on the pod buys.
        if (!TryInsert(ent, user))
            return;

        ent.Comp.SelfService = true;
    }

    /// <summary>How long somebody takes to lift a conscious body into the pod.</summary>
    private static readonly TimeSpan InsertDelay = TimeSpan.FromSeconds(3);

    private void OnDragDrop(Entity<AutodocComponent> ent, ref DragDropTargetEvent args)
    {
        if (args.Handled)
            return;

        // Playtest 3 SAM: a body dropped on the pod goes in or nowhere. A refused drop used to fall through to
        // construction's drag-drop, which "uses" the body on the pod and fills an open delivery tray with it.
        if (HasComp<BodyComponent>(args.Dragged))
            args.Handled = true;

        // Pre-merge review: the lock is the server's to enforce. A downed, unconscious, dead or sleeping body goes
        // straight in; a conscious one lifted by somebody else gets a few seconds to walk away first, like a cryo pod.
        if (ent.Comp.Locked)
            return;

        if (args.Dragged != args.User && !Helpless(args.Dragged))
        {
            _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, InsertDelay, new AutodocInsertDoAfterEvent(), ent, target: args.Dragged, used: ent)
            {
                BreakOnDamage = true,
                BreakOnMove = true,
                NeedHand = false,
                DistanceThreshold = SharedInteractionSystem.InteractionRange,
            });
            return;
        }

        if (!TryInsert(ent, args.Dragged))
            return;

        ent.Comp.SelfService = args.Dragged == args.User;
        args.Handled = true;
    }

    /// <summary>A body that cannot climb in or out by itself.</summary>
    private bool Helpless(EntityUid body) =>
        _mobState.IsIncapacitated(body) || HasComp<WolfmedDownedComponent>(body) || HasComp<SleepingComponent>(body);

    private void OnInsertDoAfter(Entity<AutodocComponent> ent, ref AutodocInsertDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } body || ent.Comp.Locked || !TryInsert(ent, body))
            return;

        ent.Comp.SelfService = false;
        args.Handled = true;
    }

    private void OnInserted(Entity<AutodocComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == AutodocComponent.DiskSlotId)
            Speak(ent, AutodocVoiceEvent.DiskInserted);

        if (args.Container.ID == AutodocComponent.BodyContainerId)
        {
            SetOccupantLying(args.Entity, true);
            AtmosphereOccupantEntered(ent, args.Entity); // Playtest 3: breathes the pod's air while sealed
            ent.Comp.DefibWarned = false;
            ent.Comp.DefibAttempt = 0;
            ent.Comp.DefibBlocked = null;
            ent.Comp.DefibNext = TimeSpan.Zero;
            ent.Comp.AutoSaidNothing = false;
            ent.Comp.AutoNextPlan = TimeSpan.Zero;

            // Everything the pod learned about the last patient goes with them.
            ent.Comp.FailedProcedures.Clear();
            ent.Comp.AutoSignature = null;
            ent.Comp.AutoReplans = 0;
            ent.Comp.PreProcedureWounds.Clear();
            ent.Comp.OccupantWasDead = false;
            ent.Comp.LeftoverReleaseAt = null; // Playtest 5
            ent.Comp.LeftoverPasses = 0;

            // Playtest 3 SAM: a new patient is a new run, with its own counts and nothing stuck on them yet.
            ent.Comp.AutoSession = false;
            ent.Comp.VoiceEvents.Clear();
            ent.Comp.ProceduresAnnounced = 0;
            ent.Comp.BlockingGarment = null;
            ent.Comp.BlockingSlot = null;
            ent.Comp.GarmentStuck = false;

            // The pod greets whoever ends up inside, however they got in.
            Speak(ent, AutodocVoiceEvent.Greeting);
        }

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void OnRemoved(Entity<AutodocComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (args.Container.ID == AutodocComponent.DiskSlotId)
            Speak(ent, AutodocVoiceEvent.DiskRemoved);

        if (args.Container.ID == AutodocComponent.BodyContainerId)
        {
            SetOccupantLying(args.Entity, false);
            ReturnTray(ent); // Playtest 5
            AtmosphereOccupantLeft(ent, args.Entity); // Playtest 3: back to the room's air
            WakeOccupant(ent, args.Entity);
            ent.Comp.FailedProcedures.Clear();
            ent.Comp.AutoSignature = null;
            ent.Comp.AutoReplans = 0;
            ent.Comp.PreProcedureWounds.Clear();
            ent.Comp.OccupantWasDead = false;
            ent.Comp.LeftoverReleaseAt = null; // Playtest 5
            ent.Comp.LeftoverPasses = 0;
            Reset(ent);
            _ui.CloseUis(ent.Owner);
        }

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>The lid can be forced open with a prying tool while it is locked.</summary>
    private void OnInteractUsing(Entity<AutodocComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Locked)
            return;

        args.Handled = _tool.UseTool(args.Used, args.User, ent.Owner, 3f, "Prying", new AutodocPryDoAfterEvent());
    }

    private void OnExamined(Entity<AutodocComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(AutodocComponent)))
        {
            args.PushMarkup(Loc.GetString($"wolfmed-autodoc-examine-{ent.Comp.State.ToString().ToLowerInvariant()}"));
            if (ent.Comp.EmagRevealed)
                args.PushMarkup(Loc.GetString("wolfmed-autodoc-examine-emagged"));
            ExamineAtmosphere(ent, args); // Playtest 3: whether the patient's air is protected
        }
    }

    private void OnDamaged(Entity<AutodocComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.State is AutodocState.Idle or AutodocState.Faulted ||
            !TryComp(ent, out DamageableComponent? damage) ||
            damage.TotalDamage.Float() < ent.Comp.FaultDamage)
            return;

        Fault(ent);
    }

    #endregion

    #region Power and emag

    /// <summary>
    /// A receiver that needs no power counts as powered at once: the power net only refreshes its flag on its own
    /// interval, so a freshly built pod would otherwise refuse to start for up to a second.
    /// </summary>
    public bool IsPowered(EntityUid uid) =>
        !TryComp<ApcPowerReceiverComponent>(uid, out var receiver) || !receiver.NeedsPower || receiver.Powered;

    private void OnPowerChanged(Entity<AutodocComponent> ent, ref PowerChangedEvent args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!args.Powered)
        {
            if (ent.Comp.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting)
            {
                ent.Comp.State = AutodocState.Paused;
                ent.Comp.PowerPaused = true;
                Speak(ent, AutodocVoiceEvent.PowerLost);
            }
        }
        else
        {
            Speak(ent, ent.Comp.PowerPaused ? AutodocVoiceEvent.PowerRestored : AutodocVoiceEvent.Boot);
            if (ent.Comp.PowerPaused)
            {
                ent.Comp.PowerPaused = false;
                ent.Comp.State = AutodocState.Step;
            }
        }

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void OnEmagged(Entity<AutodocComponent> ent, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        args.Handled = true;
        ent.Comp.Queue.Clear();
        ent.Comp.Locked = true;
        ent.Comp.State = AutodocState.Idle;
        Speak(ent, AutodocVoiceEvent.Emag);
        ent.Comp.EmagRevealed = true;
        UpdateUi(ent);
    }

    public bool IsEmagged(EntityUid uid) => _emag.CheckFlag(uid, EmagType.Interaction);

    #endregion

    private void OnGetTools(Entity<AutodocComponent> ent, ref WolfmedSurgeryToolsEvent args)
    {
        var tools = new List<EntityUid>();
        if (_containers.TryGetContainer(ent, AutodocComponent.ToolContainerId, out var container))
            tools.AddRange(container.ContainedEntities);

        // The tray is part of the toolset: add-part and add-organ steps take the item out of it.
        if (_slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId) is { } tray)
            tools.Add(tray);

        args.Tools = tools;
    }

    /// <summary>
    /// The occupant lies on the bed rather than standing on it. Same appearance key standing up and lying
    /// down use, so leaving the pod hands the sprite straight back to whatever state the body is in.
    /// </summary>
    private void SetOccupantLying(EntityUid body, bool lying)
    {
        if (TerminatingOrDeleted(body) || !HasComp<RotationVisualsComponent>(body))
            return;

        _appearance.SetData(body, RotationVisuals.RotationState,
            lying || _standing.IsDown(body) ? RotationState.Horizontal : RotationState.Vertical);
    }

    private void UpdateAppearance(Entity<AutodocComponent> ent)
    {
        var state = !IsPowered(ent) ? AutodocVisualState.Unpowered
            : ent.Comp.State is AutodocState.Preparing or AutodocState.Step ? AutodocVisualState.Operating
            : GetOccupant(ent) != null ? AutodocVisualState.Closed
            : AutodocVisualState.Open;

        _appearance.SetData(ent, AutodocVisuals.State, state);
        SetOccupantShown(ent, state is AutodocVisualState.Open or AutodocVisualState.Unpowered);
    }

    /// <summary>
    /// The lid is opaque. With it open the occupant lies on the bed and is drawn; with it closed or working
    /// they are inside the machine and nothing of them shows.
    /// </summary>
    private void SetOccupantShown(Entity<AutodocComponent> ent, bool shown)
    {
        if (!_containers.TryGetContainer(ent, AutodocComponent.BodyContainerId, out var container) ||
            container.ShowContents == shown)
            return;

        container.ShowContents = shown;
        if (TryComp(ent, out ContainerManagerComponent? manager))
            Dirty(ent.Owner, manager);
    }

    /// <summary>Puts the pod back to Idle without touching the occupant.</summary>
    private void Reset(Entity<AutodocComponent> ent)
    {
        ent.Comp.State = AutodocState.Idle;
        ent.Comp.Queue.Clear();
        ent.Comp.CurrentStep = null;
        ent.Comp.Pending = null;
        ent.Comp.Operator = null;
        ent.Comp.SelfService = false;
        ent.Comp.AnaestheticGiven = false;
        ent.Comp.PowerPaused = false;
        ent.Comp.Locked = IsEmagged(ent);
        ent.Comp.SpokenFamilies.Clear();
        ent.Comp.StallStep = null;
        ent.Comp.StallSignature = null;
        ent.Comp.StallCount = 0;
        ent.Comp.StepRuns.Clear();
        ent.Comp.BlockedReason = null;
        ent.Comp.CutClothingRequested = false;
        ent.Comp.Transfusing = false;
        ent.Comp.PreProcedureWounds.Clear();
        ent.Comp.AutoSession = false; // Playtest 3 SAM
        ent.Comp.BlockingGarment = null; // Playtest 3 SAM
        ent.Comp.BlockingSlot = null;
        ent.Comp.GarmentStuck = false;
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, true);
    }

    /// <summary>True while a procedure is under way, which is what makes an eject an emergency one.</summary>
    public bool IsRunning(Entity<AutodocComponent> ent) =>
        ent.Comp.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting or AutodocState.Paused;

    private void Fault(Entity<AutodocComponent> ent)
    {
        ent.Comp.State = AutodocState.Faulted;
        ent.Comp.Locked = false;
        ent.Comp.CurrentStep = null;
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, true);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>The body part entity behind one doll slot, or null when the body has not got it.</summary>
    public EntityUid? ResolvePart(EntityUid body, TargetBodyPart target)
    {
        foreach (var (id, part) in _body.GetBodyChildren(body))
        {
            if (_body.GetTargetBodyPart(part.PartType, part.Symmetry) == target)
                return id;
        }

        return null;
    }
}
