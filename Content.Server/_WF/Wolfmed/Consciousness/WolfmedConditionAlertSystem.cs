using System.Linq;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Alert;
using Content.Shared.Chat;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WF.Wolfmed.Consciousness;

/// <summary>Tells a wound host its condition: an alert per cause and state, and a line per transition.</summary>
/// <remarks>
/// The stock alerts read damage totals, which say nothing on a wound host, so they are switched off per
/// body through the marked <c>MobThresholdSystem.SetTriggersAlerts</c>. This system then owns the whole
/// Health category on that body, Dead included. Bodies that are not wound hosts keep the stock alerts.
/// </remarks>
// Transitions are down, out, waking, standing and the heart. Every line that names the cause also names whatever
// else is holding the body, and none promises waking or standing while something else is.
public sealed class WolfmedConditionAlertSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private WolfmedCardSystem _card = default!; // playtest 3
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;
    [Dependency] private WolfmedWoundTraitSystem _woundTraits = default!;

    /// <summary>Bodies whose limb-penalty wounds changed this tick; checked once in the update.</summary>
    private readonly HashSet<EntityUid> _limbChecks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedConsciousnessComponent, WolfmedConsciousnessChangedEvent>(OnChanged);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, WolfmedConditionAlertEvent>(OnAlertClicked);
        SubscribeLocalEvent<WolfmedAdrenalineEvent>(OnAdrenaline);
        SubscribeLocalEvent<WolfmedPainReliefTierChangedEvent>(OnPainReliefTier);
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_limbChecks.Count == 0)
            return;

        foreach (var body in _limbChecks)
            CheckLimbPenalties(body);

        _limbChecks.Clear();
    }

    /// <summary>Hands the body's health alerts to this system. Called from the wound host's startup.</summary>
    public void TakeOver(EntityUid body)
    {
        if (!_consciousness.OwnsMobState(body) || !TryComp(body, out MobThresholdsComponent? thresholds))
            return;

        _thresholds.SetTriggersAlerts(body, false, thresholds);
        Refresh(body);
    }

    /// <summary>Whether this system shows the body's health alerts.</summary>
    public bool Owns(EntityUid body) =>
        _consciousness.OwnsMobState(body) && TryComp(body, out MobThresholdsComponent? thresholds) &&
        !thresholds.TriggersAlerts;

    /// <summary>The alert the body should be showing right now, from its mob state, state and cause.</summary>
    public ProtoId<AlertPrototype>? GetAlert(EntityUid body)
    {
        if (!TryComp(body, out MobThresholdsComponent? thresholds) ||
            !TryComp(body, out WolfmedConsciousnessComponent? consciousness))
            return null;

        if (_mobState.IsDead(body))
            return thresholds.StateAlertDict.GetValueOrDefault(MobState.Dead);

        var cause = GetCausePrototype(consciousness.Cause, consciousness.Heartless);
        switch (consciousness.State)
        {
            case WolfmedConsciousness.Downed:
                if (cause?.AlertDownedMechanical is { } mechanical && _shutdown.IsMechanical(body))
                    return mechanical;

                return cause?.AlertDowned ?? CompOrNull<WolfmedDownedComponent>(body)?.Alert ??
                    new ProtoId<AlertPrototype>("WFWolfmedDowned");
            case WolfmedConsciousness.Unconscious:
                return cause?.AlertOut ?? thresholds.StateAlertDict.GetValueOrDefault(MobState.Critical);
            default:
                return thresholds.StateAlertDict.GetValueOrDefault(MobState.Alive);
        }
    }

    /// <summary>The alert in the body's health slot right now, whoever put it there.</summary>
    public ProtoId<AlertPrototype>? GetShownHealthAlert(EntityUid body)
    {
        if (!TryComp(body, out AlertsComponent? alerts))
            return null;

        ProtoId<AlertCategoryPrototype> category = CompOrNull<MobThresholdsComponent>(body)?.HealthAlertCategory ?? "Health";
        if (!alerts.Alerts.TryGetValue(AlertKey.ForCategory(category), out var state))
            return null;

        return state.Type;
    }

    /// <summary>Shows the alert the body should be showing. Alerts in one category replace each other.</summary>
    public void Refresh(EntityUid body)
    {
        if (!Owns(body) || GetAlert(body) is not { } alert)
            return;

        if (!_alerts.TryGet(alert, out var prototype))
            return;

        if (prototype.SupportsSeverity)
            ShowHealth(body, alert, prototype);
        else
            _alerts.ShowAlert(body, alert, cooldown: GetFaintCountdown(body));
    }

    /// <summary>
    /// Playtest 2: a faint's alert ticks down to waking (M3: the head blow's too). Only while faints are all that
    /// hold the body under; with another blocker the text says what else keeps them out instead of a countdown.
    /// Playtest 3: the same window the explanation card counts down (<see cref="WolfmedConsciousnessSystem.GetWakeWindow"/>).
    /// </summary>
    public (TimeSpan, TimeSpan)? GetFaintCountdown(EntityUid body) =>
        _consciousness.GetWakeWindow(body) is { } window ? (window.Start, window.End) : null;

    /// <summary>
    /// The health doll while Up: its severity from the worst Downed-level input, not from damage, so a
    /// bled-out patient no longer shows a healthy icon.
    /// </summary>
    public void RefreshHealthSeverity(EntityUid body)
    {
        if (!Owns(body) || _mobState.IsDead(body) || GetAlert(body) is not { } alert ||
            !_alerts.TryGet(alert, out var prototype) || !prototype.SupportsSeverity)
            return;

        ShowHealth(body, alert, prototype);
    }

    private void ShowHealth(EntityUid body, ProtoId<AlertPrototype> alert, AlertPrototype prototype)
    {
        var level = CompOrNull<WolfmedConsciousnessComponent>(body)?.DownLevel ?? 0f;
        var severity = (short) MathF.Round(MathHelper.Lerp(prototype.MinSeverity, prototype.MaxSeverity,
            Math.Clamp(level, 0f, 1f)));

        // BeforeAlertSeverityCheckEvent is not raised: its one reader, pain numbness, pins the doll at full
        // health, which would hide blood loss too. Numbness already zeroes pain in these inputs.
        _alerts.ShowAlert(body, alert, severity);
    }

    public WolfmedConsciousnessCausePrototype? GetCausePrototype(WolfmedCause cause) => GetCausePrototype(cause, false);

    /// <summary>M4 (OD16): the cause's prototype, circulatory collapse in place of arrest for a heartless body.</summary>
    public WolfmedConsciousnessCausePrototype? GetCausePrototype(WolfmedCause cause, bool heartless) =>
        cause != WolfmedCause.None &&
        _prototypes.TryIndex<WolfmedConsciousnessCausePrototype>(WolfmedCauses.PrototypeId(cause, heartless), out var proto)
            ? proto
            : null;

    /// <summary>The cause's short name, with its sub-source where it has one: "no oxygen (no air)".</summary>
    public string GetCauseName(WolfmedCause cause, WolfmedCauseSource source = WolfmedCauseSource.None)
    {
        if (GetCausePrototype(cause) is not { } proto)
            return Loc.GetString("wolfmed-condition-cause-unknown");

        var name = Loc.GetString(proto.BlockerName);
        return proto.Sources.TryGetValue(source, out var sourceName)
            ? Loc.GetString("wolfmed-condition-cause-with-source", ("cause", name), ("source", Loc.GetString(sourceName)))
            : name;
    }

    /// <summary>"blood loss, pain": what else is holding the body, in tie order.</summary>
    public string GetBlockerNames(WolfmedCauseFlags blockers) =>
        string.Join(", ", WolfmedCauses.Each(blockers).Select(cause => GetCauseName(cause)));

    /// <summary>"Downed: blood loss", "Passed out: pain", "Cardiac arrest: blood".</summary>
    public string GetTitle(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? comp))
            return string.Empty;

        if (_mobState.IsDead(body))
            return Loc.GetString("wolfmed-condition-title-dead");

        if (comp.State == WolfmedConsciousness.Up)
            return Loc.GetString("wolfmed-condition-title-up");

        var proto = GetCausePrototype(comp.Cause, comp.Heartless);
        var critical = comp.State == WolfmedConsciousness.Unconscious;
        var source = proto != null && proto.Sources.TryGetValue(comp.CauseSource, out var sourceName)
            ? Loc.GetString(sourceName)
            : Loc.GetString("wolfmed-condition-source-unknown");
        var name = proto != null ? Loc.GetString(proto.BlockerName) : GetCauseName(comp.Cause);

        if ((critical ? proto?.TitleOut : proto?.TitleDowned) is { } title)
            return Loc.GetString(title, ("cause", name), ("source", source));

        return Loc.GetString(critical ? "wolfmed-condition-title-out" : "wolfmed-condition-title-downed",
            ("cause", GetCauseName(comp.Cause, comp.CauseSource)));
    }

    /// <summary>
    /// The whole explanation: title, symptom, what helps (its conditional form while something else holds
    /// the body) and "Still holding you down: …". What the alert click and the tests read.
    /// </summary>
    public string GetConditionText(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? comp))
            return string.Empty;

        var parts = new List<string> { GetTitle(body) };
        if (comp.State != WolfmedConsciousness.Up && !_mobState.IsDead(body) &&
            GetCausePrototype(comp.Cause, comp.Heartless) is { } proto)
        {
            if (proto.Symptom is { } symptom)
                parts.Add(Loc.GetString(symptom));

            if (Help(body, proto, comp.State, comp.Blockers != WolfmedCauseFlags.None) is { } help)
                parts.Add(help);
        }

        if (BlockerLine(comp) is { } blockers)
            parts.Add(blockers);

        if (!_mobState.IsDead(body))
        {
            if (LimbPenaltyLine(body, false) is { } hands)
                parts.Add(hands);
            if (LimbPenaltyLine(body, true) is { } legs)
                parts.Add(legs);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Playtest 2: "Your hands are badly burned; everything takes longer." while the body's wounds slow its hands
    /// (or, for <paramref name="legs"/>, its legs). Null when nothing does.
    /// </summary>
    public string? LimbPenaltyLine(EntityUid body, bool legs)
    {
        var penalty = _woundTraits.GetBodyLimbPenalty(body, legs, out var burn);
        if (legs ? penalty >= 1f : penalty <= 1f)
            return null;

        return Loc.GetString((legs, burn) switch
        {
            (true, true) => "wolfmed-limb-penalty-legs-burn",
            (true, false) => "wolfmed-limb-penalty-legs",
            (false, true) => "wolfmed-limb-penalty-hands-burn",
            _ => "wolfmed-limb-penalty-hands",
        });
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (CompOrNull<BodyPartComponent>(args.Part)?.Body is { } body && HasComp<WolfmedConsciousnessComponent>(body))
            _limbChecks.Add(body);
    }

    /// <summary>
    /// Tells the patient once when a limb's penalty first bites, and forgets once it is gone so the next one is told
    /// too. A body that is out cannot hear it; it is told on coming round.
    /// </summary>
    public void CheckLimbPenalties(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !TryComp(body, out WolfmedConsciousnessComponent? comp) ||
            _mobState.IsDead(body))
            return;

        var awake = comp.State != WolfmedConsciousness.Unconscious;
        comp.HandsPenaltyTold = TellLimbPenalty((body, comp), false, comp.HandsPenaltyTold, awake);
        comp.LegsPenaltyTold = TellLimbPenalty((body, comp), true, comp.LegsPenaltyTold, awake);
    }

    private bool TellLimbPenalty(Entity<WolfmedConsciousnessComponent> body, bool legs, bool told, bool awake)
    {
        if (LimbPenaltyLine(body, legs) is not { } line)
            return false;

        if (told || !awake)
            return told;

        Tell(body, line);
        return true;
    }

    /// <summary>What helps, for the state the body is in; the blocked form while something else holds it.</summary>
    private string? Help(EntityUid body, WolfmedConsciousnessCausePrototype proto, WolfmedConsciousness state,
        bool blocked)
    {
        LocId? key;

        // Playtest 2: an unblocked faint says when it ends (M3: the text is the cause's own, for the head blow).
        if (state == WolfmedConsciousness.Unconscious && !blocked && proto.HelpOutTimed is { } timed &&
            _consciousness.GetFaintSecondsLeft(body) is { } seconds)
            return Loc.GetString(timed, ("seconds", seconds));

        if (state == WolfmedConsciousness.Unconscious)
            key = blocked && proto.HelpOutBlocked is { } outBlocked ? outBlocked : proto.HelpOut;
        else
            key = blocked && proto.HelpBlocked is { } helpBlocked ? helpBlocked
                : proto.HelpMechanical is { } mechanical && _shutdown.IsMechanical(body) ? mechanical
                : proto.Help;

        return key is { } found ? Loc.GetString(found) : null;
    }

    private string? BlockerLine(WolfmedConsciousnessComponent comp) =>
        comp.Blockers == WolfmedCauseFlags.None || comp.State == WolfmedConsciousness.Up
            ? null
            : Loc.GetString("wolfmed-condition-blockers", ("blockers", GetBlockerNames(comp.Blockers)));

    private void OnChanged(Entity<WolfmedConsciousnessComponent> body, ref WolfmedConsciousnessChangedEvent args)
    {
        Refresh(body);

        // Playtest 3: the card's countdown starts with the alert's, not on the card's next once-a-second pass.
        if (args.NewState == WolfmedConsciousness.Unconscious && !_mobState.IsDead(body))
            _card.Refresh(body);

        // Playtest 2: a penalty that bit while the body was out is told once it comes round, after the waking line.
        if (args.OldState == WolfmedConsciousness.Unconscious && args.NewState != WolfmedConsciousness.Unconscious)
            _limbChecks.Add(body);

        if (_mobState.IsDead(body) || TransitionLine(body, args) is not { } line)
            return;

        Tell(body, line, args.NewState == WolfmedConsciousness.Up ? PopupType.Medium : PopupType.MediumCaution);
    }

    /// <summary>The one line a transition earns, or null when only the blockers moved.</summary>
    private string? TransitionLine(Entity<WolfmedConsciousnessComponent> body, WolfmedConsciousnessChangedEvent args)
    {
        var proto = GetCausePrototype(args.NewCause, body.Comp.Heartless);
        var old = GetCausePrototype(args.OldCause, body.Comp.Heartless);
        var blocked = args.NewBlockers != WolfmedCauseFlags.None;
        string? line;

        if (args.OldCause == WolfmedCause.Arrest && args.NewCause != WolfmedCause.Arrest)
        {
            // The heart restarting: what is still wrong, and what helps. M4: a heartless body's circulation returns.
            var heartless = body.Comp.Heartless;
            line = args.NewState == WolfmedConsciousness.Up || proto == null
                ? Loc.GetString(heartless ? "wolfmed-condition-collapse-restart" : "wolfmed-condition-heart-restart")
                : Loc.GetString(heartless ? "wolfmed-condition-collapse-restart-still" : "wolfmed-condition-heart-restart-still",
                    ("cause", GetCauseName(args.NewCause, body.Comp.CauseSource)),
                    ("help", Help(body, proto, args.NewState, blocked) ?? string.Empty));
            return WithBlockers(body, line, args.NewState);
        }

        // M4 (plan §3.11): out of thermal shutdown. Still shut down for another reason says so, and nothing more.
        if (args.OldCause == WolfmedCause.CoreHeat && args.NewCause != WolfmedCause.CoreHeat)
        {
            line = args.NewState == WolfmedConsciousness.Unconscious
                ? Loc.GetString("wolfmed-condition-core-cooled-still",
                    ("cause", GetCauseName(args.NewCause, body.Comp.CauseSource)))
                : Loc.GetString("wolfmed-condition-core-cooled");
            return WithBlockers(body, line, args.NewState);
        }

        if (args.OldState == args.NewState && args.OldCause == args.NewCause)
            return null;

        switch (args.NewState)
        {
            case WolfmedConsciousness.Up:
                line = old?.Stand is { } stand && args.OldState == WolfmedConsciousness.Downed
                    ? Loc.GetString(stand)
                    : Loc.GetString(args.OldState == WolfmedConsciousness.Unconscious
                        ? "wolfmed-condition-wake-up"
                        : "wolfmed-condition-stand");
                return line;
            case WolfmedConsciousness.Downed:
                line = args.OldState == WolfmedConsciousness.Unconscious
                    ? proto?.Wake is { } wake ? Loc.GetString(wake) : Loc.GetString("wolfmed-condition-wake")
                    : proto?.Down is { } down ? Loc.GetString(down) : Loc.GetString("wolfmed-condition-down");
                return WithBlockers(body, line, args.NewState);
            default:
                line = proto?.Out is { } outLine ? Loc.GetString(outLine) : Loc.GetString("wolfmed-condition-out");
                if (proto != null && Help(body, proto, args.NewState, blocked) is { } help)
                    line = $"{line} {help}";

                return WithBlockers(body, line, args.NewState);
        }
    }

    private string WithBlockers(Entity<WolfmedConsciousnessComponent> body, string line, WolfmedConsciousness state)
    {
        return state != WolfmedConsciousness.Up && BlockerLine(body.Comp) is { } blockers ? $"{line} {blockers}" : line;
    }

    /// <summary>A popup over the body for its own player, and the same line in their chat to read back.</summary>
    public void Tell(Entity<WolfmedConsciousnessComponent> body, string line, PopupType type = PopupType.MediumCaution)
    {
        body.Comp.LastConditionLine = line;
        body.Comp.ConditionLineCount++;
        _popup.PopupEntity(line, body, body, type);

        if (!TryComp(body, out ActorComponent? actor))
            return;

        var wrapped = Loc.GetString("wolfmed-condition-chat", ("message", FormattedMessage.EscapeText(line)));
        _chat.ChatMessageToOne(ChatChannel.Notifications, line, wrapped, body, false, actor.PlayerSession.Channel);
    }

    private void OnAlertClicked(Entity<WolfmedConsciousnessComponent> body, ref WolfmedConditionAlertEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Tell(body, GetConditionText(body), PopupType.Medium);
    }

    private void OnAdrenaline(ref WolfmedAdrenalineEvent args)
    {
        if (!TryComp(args.Body, out WolfmedConsciousnessComponent? comp) || _mobState.IsDead(args.Body))
            return;

        Tell((args.Body, comp), Loc.GetString(args.Started ? "wolfmed-condition-adrenaline-start" : "wolfmed-condition-adrenaline-end"),
            PopupType.Medium);
    }

    /// <summary>
    /// Playtest 1: a painkiller the patient cannot see working feels like nothing. One line when a tier takes
    /// hold and one when it wears off; the stim's end is the crash's own line. Machines feel none of it.
    /// </summary>
    private void OnPainReliefTier(ref WolfmedPainReliefTierChangedEvent args)
    {
        if (!TryComp(args.Body, out WolfmedConsciousnessComponent? comp) || _mobState.IsDead(args.Body) ||
            _shutdown.IsMechanical(args.Body))
            return;

        string? key = null;
        if (args.New > args.Old)
            key = $"wolfmed-painkiller-takes-hold-{args.New.ToString().ToLowerInvariant()}";
        else if (args.Old != WolfmedPainReliefTier.Emergency)
            key = args.New == WolfmedPainReliefTier.None ? "wolfmed-painkiller-worn-off" : "wolfmed-painkiller-fading";

        if (key != null)
            Tell((args.Body, comp), Loc.GetString(key), PopupType.Medium);
    }
}
