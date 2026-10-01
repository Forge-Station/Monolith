using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server.Administration;
using Content.Server.Chat.Systems;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Actions;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Chat;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Which honest dialog a body has open.</summary>
public enum WolfmedEndingChoice : byte
{
    None,

    /// <summary>Dying: let go now, catastrophic brain injury, revivable, returnable ghost.</summary>
    Succumb,

    /// <summary>Not dying: leave the body alive but empty, no way back.</summary>
    LeaveAlive,
}

/// <summary>Holds the Dying actions a body has been granted. Server only.</summary>
[RegisterComponent]
public sealed partial class WolfmedDyingActionsComponent : Component
{
    [DataField]
    public EntityUid? Succumb;

    [DataField]
    public EntityUid? LastWords;
}

/// <summary>Succumb, Last Words and the ghost command on a wound host, each doing what its dialog says.</summary>
/// <remarks>
/// Granted and revoked by direct calls from <see cref="WolfmedLifeSystem.StartArrest"/>,
/// <see cref="WolfmedLifeSystem.EndArrest"/> and consciousness's death handler, never by new subscriptions.
/// </remarks>
// Succumb and Last Words exist only while Dying: the brain organ goes to 0 in place, the body dies, and the ghost can
// return after brain repair and a defibrillator. The ghost command reaches the same dialog in arrest, and a "left
// alive but empty" one with no way back in any other living state.
public sealed class WolfmedDyingActionsSystem : EntitySystem
{
    public static readonly EntProtoId SuccumbAction = "WFActionWolfmedSuccumb";
    public static readonly EntProtoId LastWordsAction = "WFActionWolfmedLastWords";

    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!; // Playtest 5
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private WolfmedConditionAlertSystem _conditionAlerts = default!;
    [Dependency] private WolfmedExecutionSystem _execution = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedOverheatSystem _overheat = default!; // M4

    /// <summary>The open dialog per body. WordsMax is set when Last Words asked: the whisper comes after a yes.</summary>
    private readonly Dictionary<EntityUid, (WolfmedEndingChoice Choice, WolfmedChoiceEui? Eui, int? WordsMax)> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedDyingActionsComponent, WolfmedSuccumbActionEvent>(OnSuccumbAction);
        SubscribeLocalEvent<WolfmedDyingActionsComponent, WolfmedLastWordsActionEvent>(OnLastWordsAction);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _pending.Clear());
        SubscribeLocalEvent<WolfmedConsciousnessComponent, WolfmedEndingEvent>(OnEnding); // M2
    }

    /// <summary>M2 (OD17): the marked execution lines hand a wound host's ending over.</summary>
    private void OnEnding(Entity<WolfmedConsciousnessComponent> ent, ref WolfmedEndingEvent args)
    {
        if (args.Handled)
            return;

        // A blade or a bludgeon that came with the event leaves its tier's gore on top of the kill.
        if (args.Weapon is { } weapon)
        {
            var strength = _execution.MeasureMelee(weapon, args.Attacker ?? ent.Owner);
            if (_execution.Apply(ent, args.Attacker, weapon, strength, args.Ending))
            {
                args.Handled = true;
                return;
            }
        }

        args.Handled = EndDeliberately(ent);
    }

    /// <summary>Kills a wound host by an execution or a suicide, through its brain, so it stays revivable.</summary>
    // Same order as Succumb: the brain (or positronic core) goes to 0 in place, the body dies, then an arrest in
    // progress ends on the corpse, so brain repair and a shock (or core repair and a restart) bring it back. The ghost
    // and whether it can return are the caller's; a suicide has already ghosted for good.
    public bool EndDeliberately(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !_life.OwnsDeath(body) || _mobState.IsDead(body))
            return false;

        Withdraw(body);
        if (_life.GetBrainOrgan(body) is { } brain)
            _organs.SetHealth(brain, FixedPoint2.Zero);

        _life.Kill(body);
        _life.EndArrest(body);
        _overheat.EndThermalShutdown(body); // M4
        return true;
    }

    #region Queries

    /// <summary>
    /// Dying on a wound host: the heart has stopped, or (M4, OD3 (b)) a machine is in thermal shutdown, or (playtest 5)
    /// a machine is shut down for want of power or a pump, and the body is not dead yet. The shutdown runs nothing
    /// out, but a chassis nobody comes for has to be able to let go too.
    /// </summary>
    public bool IsDying(EntityUid body) =>
        !TerminatingOrDeleted(body) && _life.OwnsDeath(body) &&
        (_life.InArrest(body) || _overheat.InThermalShutdown(body) || _shutdown.IsShutDown(body)) &&
        !_mobState.IsDead(body);

    /// <summary>Wolfmed, not upstream's kill-crit branch, decides how this body is left.</summary>
    public bool OwnsEnding(EntityUid? body) => body is { } uid && _life.OwnsDeath(uid);

    /// <summary>The dialog this body has open, if any. What the tests read.</summary>
    public WolfmedEndingChoice GetPendingChoice(EntityUid body) =>
        _pending.TryGetValue(body, out var pending) ? pending.Choice : WolfmedEndingChoice.None;

    #endregion

    #region Grant and revoke

    /// <summary>Gives a Dying body Succumb and Last Words. Called when the heart stops or a shutdown starts.</summary>
    public void Grant(EntityUid body)
    {
        if (!IsDying(body))
            return;

        var comp = EnsureComp<WolfmedDyingActionsComponent>(body);
        _actions.AddAction(body, ref comp.Succumb, SuccumbAction);
        _actions.AddAction(body, ref comp.LastWords, LastWordsAction);
    }

    /// <summary>
    /// Takes them away again, and withdraws an open Succumb dialog. Heart restarted, or dead; on death any open
    /// dialog goes, since "left alive but empty" no longer describes the body.
    /// </summary>
    public void Revoke(EntityUid body)
    {
        if (_pending.TryGetValue(body, out var pending) &&
            (pending.Choice == WolfmedEndingChoice.Succumb || _mobState.IsDead(body)))
            Withdraw(body);

        if (TerminatingOrDeleted(body) || !TryComp(body, out WolfmedDyingActionsComponent? comp))
            return;

        if (comp.Succumb is { } succumb)
            Del(succumb);

        if (comp.LastWords is { } lastWords)
            Del(lastWords);

        RemComp<WolfmedDyingActionsComponent>(body);
    }

    #endregion

    #region Actions

    private void OnSuccumbAction(Entity<WolfmedDyingActionsComponent> ent, ref WolfmedSuccumbActionEvent args)
    {
        if (args.Handled || !IsDying(ent))
            return;

        args.Handled = true;
        OpenSuccumbDialog(ent);
    }

    private void OnLastWordsAction(Entity<WolfmedDyingActionsComponent> ent, ref WolfmedLastWordsActionEvent args)
    {
        if (args.Handled || !IsDying(ent) || !TryComp(ent, out ActorComponent? actor))
            return;

        // Playtest 3: "Let go?" comes first; the whisper is asked for only after a yes, so nobody whispers their
        // last words and then keeps fighting.
        args.Handled = true;
        OpenSuccumbDialog(ent, args.MaxLength);
    }

    /// <summary>The whisper prompt after a yes to Let go. The words are optional: cancel or empty still lets go.</summary>
    private void OpenLastWordsPrompt(EntityUid body, int maxLength)
    {
        if (!TryComp(body, out ActorComponent? actor))
        {
            Succumb(body);
            return;
        }

        var session = actor.PlayerSession;
        _quickDialog.OpenDialog(session, Loc.GetString("wolfmed-last-words-title"),
            Loc.GetString("wolfmed-last-words-prompt", ("max", maxLength)),
            (string words) =>
            {
                if (session.AttachedEntity == body)
                    SayLastWords(body, words, maxLength);
            },
            () =>
            {
                if (session.AttachedEntity == body)
                    Succumb(body);
            });
    }

    /// <summary>
    /// Whispers the words, cut to the limit, then lets go (Succumb). Dying only: a body revived while the prompt
    /// was open says nothing and stays. Public so a test can answer the prompt.
    /// </summary>
    public bool SayLastWords(EntityUid body, string words, int maxLength)
    {
        if (!IsDying(body))
            return false;

        words = words.Trim();
        if (words.Length > maxLength)
            words = words[..Math.Max(0, maxLength)];

        if (words.Length > 0)
        {
            _chat.TrySendInGameICMessage(body, Loc.GetString("wolfmed-last-words-whisper", ("words", words)),
                InGameICChatType.Whisper, ChatTransmitRange.Normal, checkRadioPrefix: false,
                ignoreActionBlocker: true);
        }

        return Succumb(body);
    }

    #endregion

    #region Dialogs

    /// <summary>
    /// The marked GhostSystem hook's hand-off: a wound host's own <c>ghost</c> command. Opens the Succumb
    /// dialog while Dying and the "left alive but empty" dialog in any other living state; true means the
    /// attempt was handled here and nobody has been ghosted yet.
    /// </summary>
    public bool TryOpenGhostDialog(EntityUid body)
    {
        if (!_life.OwnsDeath(body) || _mobState.IsDead(body) || TerminatingOrDeleted(body))
            return false;

        if (IsDying(body))
            OpenSuccumbDialog(body);
        else
            OpenLeaveDialog(body);

        return true;
    }

    /// <summary>
    /// "Let go?": the exact consequences (OD1 wording), then a revivable death on yes. With <paramref name="wordsMax"/>
    /// (Last Words), a yes asks for the whisper first.
    /// </summary>
    public void OpenSuccumbDialog(EntityUid body, int? wordsMax = null)
    {
        if (!IsDying(body))
            return;

        var minutes = RotMinutes(body);
        string text;
        if (_overheat.InThermalShutdown(body))
        {
            // M4 (plan §5.4): a machine's core failure, core repair and the restart button.
            text = minutes is { } rot
                ? Loc.GetString("wolfmed-succumb-dialog-text-core", ("minutes", rot))
                : Loc.GetString("wolfmed-succumb-dialog-text-core-no-decay");
        }
        else if (_shutdown.IsShutDown(body))
        {
            // Playtest 5: a chassis with no power or no pump. Core failure, like thermal shutdown, and the same way back.
            var reason = Loc.GetString(CompOrNull<WolfmedShutdownComponent>(body)?.Reason == WolfmedCauseSource.Pump
                ? "wolfmed-cause-shutdown-source-pump"
                : "wolfmed-cause-shutdown-source-power");
            text = minutes is { } rot
                ? Loc.GetString("wolfmed-succumb-dialog-text-shutdown", ("reason", reason), ("minutes", rot))
                : Loc.GetString("wolfmed-succumb-dialog-text-shutdown-no-decay", ("reason", reason));
        }
        else if (CompOrNull<WolfmedConsciousnessComponent>(body)?.Heartless == true)
        {
            // M4 (OD16): a species with no heart has circulatory collapse, not a stopped heart.
            text = minutes is { } rot
                ? Loc.GetString("wolfmed-succumb-dialog-text-collapse", ("cause", ArrestCauseName(body)), ("minutes", rot))
                : Loc.GetString("wolfmed-succumb-dialog-text-collapse-no-decay", ("cause", ArrestCauseName(body)));
        }
        else
        {
            text = minutes is { } left
                ? Loc.GetString("wolfmed-succumb-dialog-text", ("cause", ArrestCauseName(body)), ("minutes", left))
                : Loc.GetString("wolfmed-succumb-dialog-text-no-decay", ("cause", ArrestCauseName(body)));
        }

        Open(body, WolfmedEndingChoice.Succumb, new WolfmedChoiceEuiState(
            Loc.GetString("wolfmed-succumb-dialog-title"), text,
            Loc.GetString("wolfmed-succumb-dialog-accept"), Loc.GetString("wolfmed-succumb-dialog-deny")), wordsMax);
    }

    /// <summary>The <c>ghost</c> command while not Dying: the body stays alive, the player cannot return.</summary>
    public void OpenLeaveDialog(EntityUid body)
    {
        Open(body, WolfmedEndingChoice.LeaveAlive, new WolfmedChoiceEuiState(
            Loc.GetString("wolfmed-leave-dialog-title"), Loc.GetString("wolfmed-leave-dialog-text"),
            Loc.GetString("wolfmed-leave-dialog-accept"), Loc.GetString("wolfmed-leave-dialog-deny")));
    }

    private void Open(EntityUid body, WolfmedEndingChoice choice, WolfmedChoiceEuiState state, int? wordsMax = null)
    {
        Withdraw(body);

        WolfmedChoiceEui? eui = null;
        if (TryComp(body, out ActorComponent? actor))
        {
            eui = new WolfmedChoiceEui(state, accepted =>
            {
                if (accepted)
                    Confirm(body);
                else
                    Decline(body);
            });
        }

        _pending[body] = (choice, eui, wordsMax);
        if (eui != null)
            _eui.OpenEui(eui, actor!.PlayerSession);
    }

    /// <summary>Closes the body's open dialog, if any, without acting on it.</summary>
    private void Withdraw(EntityUid body)
    {
        if (!_pending.Remove(body, out var pending))
            return;

        pending.Eui?.Withdraw();
    }

    /// <summary>The player said yes to the body's open dialog. Public so a test can answer it.</summary>
    public bool Confirm(EntityUid body)
    {
        if (!_pending.Remove(body, out var pending))
            return false;

        if (pending is { Choice: WolfmedEndingChoice.Succumb, WordsMax: { } max })
        {
            if (!IsDying(body))
                return false;

            OpenLastWordsPrompt(body, max);
            return true;
        }

        return pending.Choice switch
        {
            WolfmedEndingChoice.Succumb => Succumb(body),
            WolfmedEndingChoice.LeaveAlive => LeaveAlive(body),
            _ => false,
        };
    }

    /// <summary>The player said no, or closed the window.</summary>
    public void Decline(EntityUid body)
    {
        _pending.Remove(body);
    }

    #endregion

    #region Endings

    /// <summary>
    /// Succumb (OD2 (a)), in this order: the brain organ to 0 where it sits, so the body and the mind stay and
    /// brain repair plus a defibrillator brings the same person back; death now, because the organ system
    /// would only apply it on its next update; the arrest ended on the corpse; then a returnable ghost.
    /// </summary>
    public bool Succumb(EntityUid body)
    {
        if (!IsDying(body))
            return false;

        if (_life.GetBrainOrgan(body) is { } brain)
            _organs.SetHealth(brain, FixedPoint2.Zero);

        // Kill before EndArrest: ending the arrest on a living body would announce a heartbeat for a moment.
        _life.Kill(body);
        _life.EndArrest(body);
        _overheat.EndThermalShutdown(body); // M4: on the corpse, for the same reason

        if (_mind.TryGetMind(body, out var mindId, out var mind) && mind.OwnedEntity == body)
            _ghost.OnGhostAttempt(mindId, canReturnGlobal: true, mind: mind);

        return true;
    }

    /// <summary>
    /// Leaves a living body empty, with no way back. The body is not touched. Dying goes to Succumb; a body
    /// that died while the dialog was open is refused, so the ghost never comes back returnable.
    /// </summary>
    public bool LeaveAlive(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || _mobState.IsDead(body))
            return false;

        if (IsDying(body))
        {
            OpenSuccumbDialog(body);
            return false;
        }

        if (!_mind.TryGetMind(body, out var mindId, out var mind) || mind.OwnedEntity != body)
            return false;

        return _ghost.OnGhostAttempt(mindId, canReturnGlobal: false, mind: mind);
    }

    #endregion

    /// <summary>"blood loss", "no oxygen": what stopped the heart, from the arrest cause's own names.</summary>
    private string ArrestCauseName(EntityUid body)
    {
        var source = TryComp(body, out WolfmedConsciousnessComponent? consciousness) &&
                     consciousness.Cause == WolfmedCause.Arrest
            ? consciousness.CauseSource
            : WolfmedCauseSource.ArrestOther;

        return _conditionAlerts.GetCausePrototype(WolfmedCause.Arrest) is { } proto &&
               proto.Sources.TryGetValue(source, out var name)
            ? Loc.GetString(name)
            : Loc.GetString("wolfmed-condition-source-unknown");
    }

    /// <summary>Whole minutes until the body rots, or null for a body that never does.</summary>
    private int? RotMinutes(EntityUid body)
    {
        if (!TryComp(body, out PerishableComponent? perishable))
            return null;

        var left = perishable.RotAfter - perishable.RotAccumulator;
        return (int) Math.Ceiling(Math.Max(0, left.TotalMinutes));
    }
}
