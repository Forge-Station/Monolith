using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Examine;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WF.Wolfmed.Consciousness;

/// <summary>
/// M2 (plan §5.3): the crawling stage's own actions beside Call for help. Check yourself (Up or Downed) lists your own
/// symptoms in words through the self path of the visual inspection. Play dead (Downed only, OD20) makes you read as
/// lifeless from a distance until you move, speak or act. Granted by a direct call from consciousness's Apply.
/// </summary>
public sealed class WolfmedCrawlActionsSystem : EntitySystem
{
    public static readonly EntProtoId PlayDeadAction = "WFActionWolfmedPlayDead";
    public static readonly EntProtoId CheckYourselfAction = "WFActionWolfmedCheckYourself";

    [Dependency] private IChatManager _chat = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private WolfmedConditionAlertSystem _conditionAlerts = default!;
    [Dependency] private WolfmedVisualInspectionSystem _inspection = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedCrawlActionsComponent, WolfmedPlayDeadActionEvent>(OnPlayDead);
        SubscribeLocalEvent<WolfmedCrawlActionsComponent, WolfmedCheckYourselfActionEvent>(OnCheckYourself);
        SubscribeLocalEvent<WolfmedPlayingDeadComponent, MoveInputEvent>(OnMoveInput);
        SubscribeLocalEvent<WolfmedPlayingDeadComponent, EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<WolfmedPlayingDeadComponent, InteractionAttemptEvent>(OnInteractionAttempt);
    }

    /// <summary>
    /// Grants and takes away the actions for the state the body is in: Check yourself while Up or Downed, Play dead
    /// while Downed. Leaving Downed ends playing dead. Called on every state apply, so it only acts on a change.
    /// </summary>
    public void Refresh(EntityUid body, WolfmedConsciousness state)
    {
        if (TerminatingOrDeleted(body))
            return;

        var dead = _mobState.IsDead(body);
        var downed = !dead && state == WolfmedConsciousness.Downed;
        var awake = !dead && state != WolfmedConsciousness.Unconscious;

        if (!downed && HasComp<WolfmedPlayingDeadComponent>(body))
            StopPlayingDead(body, false);

        if (!awake && !HasComp<WolfmedCrawlActionsComponent>(body))
            return;

        var comp = EnsureComp<WolfmedCrawlActionsComponent>(body);
        Set(body, ref comp.CheckYourself, CheckYourselfAction, awake);
        Set(body, ref comp.PlayDead, PlayDeadAction, downed);
    }

    private void Set(EntityUid body, ref EntityUid? action, EntProtoId prototype, bool wanted)
    {
        if (wanted)
        {
            if (action is { } existing && Exists(existing))
                return;

            action = null;
            _actions.AddAction(body, ref action, prototype);
            return;
        }

        if (action is not { } held)
            return;

        action = null;
        Del(held);
    }

    public bool IsPlayingDead(EntityUid body) => HasComp<WolfmedPlayingDeadComponent>(body);

    private void OnPlayDead(Entity<WolfmedCrawlActionsComponent> ent, ref WolfmedPlayDeadActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (IsPlayingDead(ent))
        {
            StopPlayingDead(ent, true);
            return;
        }

        StartPlayingDead(ent);
    }

    /// <summary>Lie still and look dead. Downed only.</summary>
    public bool StartPlayingDead(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness) ||
            consciousness.State != WolfmedConsciousness.Downed || _mobState.IsDead(body))
            return false;

        EnsureComp<WolfmedPlayingDeadComponent>(body);
        _popup.PopupEntity(Loc.GetString("wolfmed-play-dead-start"), body, body);
        return true;
    }

    /// <summary>Stop playing dead, and say so when the player chose to.</summary>
    public void StopPlayingDead(EntityUid body, bool tell)
    {
        if (!RemComp<WolfmedPlayingDeadComponent>(body) || !tell)
            return;

        _popup.PopupEntity(Loc.GetString("wolfmed-play-dead-stop"), body, body);
    }

    /// <summary>Any movement key, pressed or let go, is the player moving. Being dragged is not.</summary>
    private void OnMoveInput(Entity<WolfmedPlayingDeadComponent> ent, ref MoveInputEvent args)
    {
        StopPlayingDead(ent, true);
    }

    private void OnSpoke(EntityUid uid, WolfmedPlayingDeadComponent comp, EntitySpokeEvent args)
    {
        StopPlayingDead(uid, true);
    }

    /// <summary>Doing anything to anything, a pen on yourself included, gives the game away.</summary>
    private void OnInteractionAttempt(Entity<WolfmedPlayingDeadComponent> ent, ref InteractionAttemptEvent args)
    {
        if (args.Target != null)
            StopPlayingDead(ent, true);
    }

    private void OnCheckYourself(Entity<WolfmedCrawlActionsComponent> ent, ref WolfmedCheckYourselfActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        CheckYourself(ent);
    }

    /// <summary>
    /// Your condition, then what you would see examining yourself, one line each, to your own chat. Returns the text,
    /// which the tests read.
    /// </summary>
    public string CheckYourself(EntityUid body)
    {
        var lines = new List<string>();
        if (TryComp(body, out WolfmedConsciousnessComponent? _))
            lines.Add(_conditionAlerts.GetConditionText(body));

        if (_inspection.GetLook(body, body, true) is { } look)
        {
            lines.AddRange(look.Parts.Select(part => FormattedMessage.RemoveMarkupPermissive(part.Line)));
            lines.AddRange(look.Notes.Select(FormattedMessage.RemoveMarkupPermissive));
        }

        var text = string.Join("\n", lines.Where(line => line.Length > 0));
        _popup.PopupEntity(Loc.GetString("wolfmed-check-yourself-popup"), body, body);

        if (TryComp(body, out ActorComponent? actor))
        {
            var wrapped = Loc.GetString("wolfmed-condition-chat", ("message", FormattedMessage.EscapeText(text)));
            _chat.ChatMessageToOne(ChatChannel.Notifications, text, wrapped, body, false, actor.PlayerSession.Channel);
        }

        return text;
    }
}
