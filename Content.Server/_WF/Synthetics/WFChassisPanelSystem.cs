using Content.Server._WF.Wolfmed.Hud;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Popups;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Synthetics;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// The chassis panel: every synthetic-biotype body gets the action that opens it, and a mechanic links in with a
/// multitool. Builds the panel's state and runs its controls (isolate, detach, reboot, overclock); the bay and
/// component controls on the same window are <c>WFChassisBaySystem</c>'s.
/// </summary>
public sealed partial class WFChassisPanelSystem : SharedWFSyntheticsSystem
{
    public static readonly EntProtoId ActionId = "WFActionChassisPanel";

    /// <summary>Client BUI class the panel's key is registered with at runtime.</summary>
    public const string BoundUserInterface = "WFChassisPanelBoundUserInterface";

    private static readonly TimeSpan StateInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan LinkDelay = TimeSpan.FromSeconds(2);
    private const string LinkQuality = "Pulsing";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFChassisIsolationSystem _isolation = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;
    [Dependency] private WolfmedSyntheticHudSystem _hud = default!;

    private TimeSpan _nextState;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HumanoidAppearanceComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFBiotypeAppliedEvent>(OnBiotypeApplied);
        SubscribeLocalEvent<WFChassisPanelActionsComponent, WFOpenChassisPanelActionEvent>(OnOpenAction);
        SubscribeLocalEvent<WFChassisComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<WFChassisComponent, WFChassisPanelLinkDoAfterEvent>(OnLinkDoAfter);

        Subs.BuiEvents<WFChassisComponent>(WFChassisPanelUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WFChassisIsolatePartMessage>(OnIsolateMessage);
            subs.Event<WFChassisDetachPartMessage>(OnDetachMessage);
            subs.Event<WFChassisRebootMessage>(OnRebootMessage);
            subs.Event<WFChassisOverclockMessage>(OnOverclockMessage);
        });
    }

    private void OnMapInit(Entity<HumanoidAppearanceComponent> ent, ref MapInitEvent args)
    {
        if (IsSynthetic(ent))
            Grant(ent);
    }

    /// <summary>A conversion after spawn: the marker is not written yet when this is raised, so the event's biotype decides.</summary>
    private void OnBiotypeApplied(ref WFBiotypeAppliedEvent args)
    {
        if (TerminatingOrDeleted(args.Mob) || LifeStage(args.Mob) < EntityLifeStage.MapInitialized)
            return;

        if (args.Biotype == WFBiotype.Synthetic)
            Grant(args.Mob);
        else
            Revoke(args.Mob);
    }

    /// <summary>Gives a synthetic body its chassis, the panel action and the panel's UI key. Safe to repeat.</summary>
    public void Grant(EntityUid body)
    {
        EnsureComp<WFChassisComponent>(body);
        var actions = EnsureComp<WFChassisPanelActionsComponent>(body);
        if (actions.Action is not { } existing || !Exists(existing))
        {
            actions.Action = null;
            _actions.AddAction(body, ref actions.Action, ActionId);
        }

        if (!_ui.HasUi(body, WFChassisPanelUiKey.Key))
            // No input validation: a Downed body cannot "interact", and the panel is the one thing it must still reach.
            // The sender still has to be an actor of this UI, which only the action and a multitool link make them.
            _ui.SetUi(body, WFChassisPanelUiKey.Key, new InterfaceData(BoundUserInterface, requireInputValidation: false));
    }

    /// <summary>Takes the chassis and the action off a body that is no longer synthetic.</summary>
    public void Revoke(EntityUid body)
    {
        if (TryComp(body, out WFChassisPanelActionsComponent? actions))
        {
            _actions.RemoveAction(body, actions.Action);
            RemComp<WFChassisPanelActionsComponent>(body);
        }

        if (!HasComp<WFChassisComponent>(body))
            return;

        _ui.CloseUi(body, WFChassisPanelUiKey.Key);
        RemComp<WFChassisRebootComponent>(body);
        RemComp<WFChassisOverclockComponent>(body);
        RemComp<WFChassisComponent>(body);
        _speed.RefreshMovementSpeedModifiers(body);
    }

    private void OnOpenAction(Entity<WFChassisPanelActionsComponent> ent, ref WFOpenChassisPanelActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!IsSynthetic(ent) || !HasComp<WFChassisComponent>(ent))
        {
            _popup.PopupEntity(Loc.GetString("wf-synthetics-not-synthetic"), ent, ent);
            return;
        }

        if (_mobState.IsDead(ent))
        {
            _popup.PopupEntity(Loc.GetString("wf-chassis-panel-refuse-dead"), ent, ent);
            return;
        }

        if (_shutdown.IsShutDown(ent))
        {
            _popup.PopupEntity(Loc.GetString("wf-chassis-panel-refuse-shutdown"), ent, ent);
            return;
        }

        _ui.OpenUi(ent.Owner, WFChassisPanelUiKey.Key, ent.Owner);
    }

    /// <summary>A multitool on someone else's chassis links into its panel after a short do-after.</summary>
    private void OnAfterInteractUsing(Entity<WFChassisComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || args.User == ent.Owner || !_tools.HasQuality(args.Used, LinkQuality))
            return;

        args.Handled = TryStartLink(ent, args.User, args.Used);
    }

    /// <summary>Starts a mechanic's link into this chassis's panel.</summary>
    public bool TryStartLink(EntityUid body, EntityUid user, EntityUid tool)
    {
        if (!IsSynthetic(body))
        {
            _popup.PopupEntity(Loc.GetString("wf-synthetics-not-synthetic"), body, user);
            return true;
        }

        _popup.PopupEntity(Loc.GetString("wf-chassis-link-start-user", ("target", body)), body, user);
        _popup.PopupEntity(Loc.GetString("wf-chassis-link-start-target", ("user", user)), body, body,
            PopupType.Medium);

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, LinkDelay,
            new WFChassisPanelLinkDoAfterEvent(), body, body, tool)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnLinkDoAfter(Entity<WFChassisComponent> ent, ref WFChassisPanelLinkDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        Link(ent, args.User);
    }

    /// <summary>Opens this chassis's panel for a mechanic, and tells the owner.</summary>
    public void Link(EntityUid body, EntityUid user)
    {
        Grant(body);
        _ui.OpenUi(body, WFChassisPanelUiKey.Key, user);
        _popup.PopupEntity(Loc.GetString("wf-chassis-link-done-target", ("user", user)), body, body,
            PopupType.MediumCaution);
    }

    private void OnUiOpened(Entity<WFChassisComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushState(ent);
    }

    /// <summary>Sends the panel's state to everyone who has it open.</summary>
    public void PushState(EntityUid body)
    {
        if (!_ui.IsUiOpen(body, WFChassisPanelUiKey.Key))
            return;

        _ui.SetUiState(body, WFChassisPanelUiKey.Key, BuildState(body));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        UpdateReboots(now);

        if (now < _nextState)
            return;

        _nextState = now + StateInterval;
        UpdateOverclock(now);

        var query = EntityQueryEnumerator<WFChassisComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (_ui.IsUiOpen((uid, ui), WFChassisPanelUiKey.Key))
                _ui.SetUiState((uid, ui), WFChassisPanelUiKey.Key, BuildState(uid));
        }
    }
}

/// <summary>The panel action a synthetic body was given, so it can be taken back.</summary>
[RegisterComponent]
public sealed partial class WFChassisPanelActionsComponent : Component
{
    [DataField]
    public EntityUid? Action;
}
