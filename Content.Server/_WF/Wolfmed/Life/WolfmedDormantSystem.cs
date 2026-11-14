using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Flags a long-helpless but stable body, and lets its player wait as a ghost that can return.</summary>
/// <remarks>
/// The ghost is made by the ghost system's own spawn with <c>canReturn</c> set, which visits from the living body, not
/// through <c>OnGhostAttempt</c>, so the marked ghost hook does not turn it into "left alive but empty".
/// </remarks>
// Stable: Unconscious or shut down, not in arrest or a faint, with no brain drain and no route running. After
// wolfmed.dormant_offer_seconds of that it is flagged in distress on medical HUDs and the ghost offer opens. The
// moment anything gets worse both are withdrawn, an open dialog included, and a waiting ghost is told; when the body
// wakes the ghost is offered the way back.
public sealed class WolfmedDormantSystem : EntitySystem
{
    public static readonly EntProtoId WaitAction = "WFActionWolfmedWaitAsGhost";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private WolfmedConditionAlertSystem _conditionAlerts = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedRevivalSystem _revival = default!;
    [Dependency] private WolfmedOverheatSystem _overheat = default!; // M4

    private readonly Dictionary<EntityUid, WolfmedChoiceEui> _open = new();
    private readonly List<EntityUid> _due = new();
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedDormantComponent, WolfmedWaitAsGhostActionEvent>(OnWaitAction);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _open.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + TickInterval;

        _due.Clear();
        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent>();
        while (query.MoveNext(out var body, out var consciousness))
        {
            if (consciousness.State == WolfmedConsciousness.Unconscious || HasComp<WolfmedDormantComponent>(body))
                _due.Add(body);
        }

        foreach (var body in _due)
        {
            if (!TerminatingOrDeleted(body))
                Tick(body, (float) TickInterval.TotalSeconds);
        }
    }

    /// <summary>
    /// Stable: Unconscious or shut down, alive, not Dying, not a faint, no drain on a brain and no route running.
    /// Blood-loss and hypoxic unconsciousness never qualify, because the brain drains under both.
    /// </summary>
    public bool IsStable(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness) || _mobState.IsDead(body) ||
            consciousness.State != WolfmedConsciousness.Unconscious || _life.InArrest(body) ||
            _overheat.InThermalShutdown(body) || // M4: Dying, and Succumb covers it
            WolfmedCauses.IsFaint(consciousness.Cause))
            return false;

        if (_life.GetBrain(body) is { } brain && _life.DrainRate(body, brain) > 0f)
            return false;

        return _life.GetActiveRoutes(body) == WolfmedRoutes.None;
    }

    /// <summary>One body's share of the tick. Public so a test can run the clock forward.</summary>
    public void Tick(EntityUid body, float seconds)
    {
        var dead = _mobState.IsDead(body);
        var comp = CompOrNull<WolfmedDormantComponent>(body);
        var state = CompOrNull<WolfmedConsciousnessComponent>(body)?.State;

        if (dead || state != WolfmedConsciousness.Unconscious)
        {
            if (comp == null)
                return;

            // Awake again (or gone): the ghost is offered the way back, and the flag and offer go.
            if (!dead && comp.Waiting)
                _revival.OfferReturn(body);

            Withdraw((body, comp));
            RemComp<WolfmedDormantComponent>(body);
            return;
        }

        comp ??= EnsureComp<WolfmedDormantComponent>(body);
        RefreshWaiting((body, comp));

        if (!IsStable(body))
        {
            comp.StableSeconds = 0f;
            Withdraw((body, comp));
            if (comp.Waiting)
                TellWorse((body, comp));
            return;
        }

        comp.Told = WolfmedRoutes.None;
        comp.StableSeconds += seconds;
        if (comp.StableSeconds >= MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.DormantOfferSeconds)))
            Offer((body, comp));
    }

    /// <summary>Distress on the HUD and the action, once.</summary>
    private void Offer(Entity<WolfmedDormantComponent> body)
    {
        if (!body.Comp.Distress)
        {
            body.Comp.Distress = true;
            Dirty(body);
        }

        if (body.Comp.Waiting || body.Comp.Action is { } held && Exists(held))
            return;

        body.Comp.Action = null;
        _actions.AddAction(body, ref body.Comp.Action, WaitAction);
    }

    /// <summary>The flag, the action and any open dialog all go.</summary>
    private void Withdraw(Entity<WolfmedDormantComponent> body)
    {
        if (body.Comp.Distress)
        {
            body.Comp.Distress = false;
            Dirty(body);
        }

        if (body.Comp.Action is { } action)
        {
            body.Comp.Action = null;
            Del(action);
        }

        if (_open.Remove(body, out var eui))
            eui.Withdraw();
    }

    /// <summary>Whether the offer stands right now: the action exists.</summary>
    public bool IsOffered(EntityUid body) =>
        TryComp(body, out WolfmedDormantComponent? comp) && comp.Action is { } action && Exists(action);

    public bool IsDistressed(EntityUid body) => TryComp(body, out WolfmedDormantComponent? comp) && comp.Distress;

    /// <summary>A ghost that went back on its own is no longer waiting.</summary>
    private void RefreshWaiting(Entity<WolfmedDormantComponent> body)
    {
        if (!body.Comp.Waiting)
            return;

        if (!_mind.TryGetMind(body, out _, out var mind) || mind.VisitingEntity == null)
        {
            body.Comp.Waiting = false;
            body.Comp.Told = WolfmedRoutes.None;
        }
    }

    /// <summary>"Your body is getting worse: {route}. You can return to it now." Once per route.</summary>
    private void TellWorse(Entity<WolfmedDormantComponent> body)
    {
        // Anything unstable and not a faint is a route: every drain on the brain is one, and so is arrest.
        var fresh = _life.GetActiveRoutes(body) & ~body.Comp.Told;
        if (fresh == WolfmedRoutes.None)
            return;

        body.Comp.Told |= fresh;
        var names = new List<string>();
        for (var bit = 0; bit < 16; bit++)
        {
            var route = (WolfmedRoutes) (1 << bit);
            if ((fresh & route) != 0)
                names.Add(Loc.GetString($"wolfmed-dormant-route-{route.ToString().ToLowerInvariant()}"));
        }

        var line = Loc.GetString("wolfmed-dormant-worse", ("route", string.Join(", ", names)));
        body.Comp.LastLine = line;

        if (!_mind.TryGetMind(body, out _, out var mind) || mind.UserId is not { } user ||
            !_player.TryGetSessionById(user, out var session))
            return;

        var wrapped = Loc.GetString("wolfmed-condition-chat", ("message", FormattedMessage.EscapeText(line)));
        _chat.ChatMessageToOne(ChatChannel.Notifications, line, wrapped, mind.CurrentEntity ?? body, false,
            session.Channel);
    }

    private void OnWaitAction(Entity<WolfmedDormantComponent> ent, ref WolfmedWaitAsGhostActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        OpenDialog(ent);
    }

    /// <summary>The exact text, then a returnable ghost on yes. Only while the offer stands.</summary>
    public void OpenDialog(EntityUid body)
    {
        if (!IsOffered(body) || !TryComp(body, out ActorComponent? actor) ||
            !TryComp(body, out WolfmedConsciousnessComponent? consciousness))
            return;

        if (_open.Remove(body, out var old))
            old.Withdraw();

        var cause = _conditionAlerts.GetCauseName(consciousness.Cause, consciousness.CauseSource);
        var eui = new WolfmedChoiceEui(new WolfmedChoiceEuiState(
            Loc.GetString("wolfmed-dormant-dialog-title"),
            Loc.GetString("wolfmed-dormant-dialog-text", ("cause", cause)),
            Loc.GetString("wolfmed-dormant-dialog-accept"),
            Loc.GetString("wolfmed-dormant-dialog-deny")), accepted =>
        {
            _open.Remove(body);
            if (accepted)
                WaitAsGhost(body);
        });

        _open[body] = eui;
        _eui.OpenEui(eui, actor.PlayerSession);
    }

    /// <summary>
    /// The player leaves the stable body as a ghost that can return; the body is untouched. False when the offer no
    /// longer stands. Public so a test can take it.
    /// </summary>
    public bool WaitAsGhost(EntityUid body)
    {
        if (!IsOffered(body) || !IsStable(body) || !TryComp(body, out WolfmedDormantComponent? comp) ||
            !_mind.TryGetMind(body, out var mindId, out var mind) || mind.OwnedEntity != body || mind.VisitingEntity != null)
            return false;

        if (_ghost.SpawnGhost((mindId, mind), body, canReturn: true) == null)
            return false;

        comp.Waiting = true;
        comp.Told = WolfmedRoutes.None;
        if (comp.Action is { } action)
        {
            comp.Action = null;
            Del(action);
        }

        return true;
    }
}
