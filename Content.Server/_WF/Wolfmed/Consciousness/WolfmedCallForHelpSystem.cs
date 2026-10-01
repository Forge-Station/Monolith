using Content.Server.Administration;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Consciousness;

/// <summary>
/// Call for help (M1a, plan §5.3): a Downed body shouts a short line it can edit and is flagged on medical
/// HUDs for <c>wolfmed.call_for_help_seconds</c>, then waits <c>wolfmed.call_for_help_cooldown</c> before the
/// next call. Downed only: the action exists while Downed and nowhere else.
/// </summary>
public sealed class WolfmedCallForHelpSystem : EntitySystem
{
    public static readonly EntProtoId CallAction = "WFActionWolfmedCallForHelp";

    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedCallForHelpComponent, WolfmedCallForHelpActionEvent>(OnAction);
    }

    /// <summary>Grants the action on a Downed body and takes it away otherwise. Called on every state apply.</summary>
    public void Refresh(EntityUid body, bool downed)
    {
        if (TerminatingOrDeleted(body))
            return;

        if (!downed)
        {
            if (TryComp(body, out WolfmedCallForHelpComponent? held) && held.Action is { } action)
            {
                held.Action = null;
                Del(action);
            }

            return;
        }

        var comp = EnsureComp<WolfmedCallForHelpComponent>(body);
        if (comp.Action is { } existing && Exists(existing))
            return;

        comp.Action = null;
        _actions.AddAction(body, ref comp.Action, CallAction);
        if (comp.CooldownUntil > _timing.CurTime)
            _actions.SetCooldown(comp.Action, _timing.CurTime, comp.CooldownUntil);
    }

    /// <summary>True while medical HUDs show the body's call.</summary>
    public bool IsFlagged(EntityUid body) =>
        TryComp(body, out WolfmedCallForHelpComponent? comp) && comp.FlagUntil > _timing.CurTime;

    private bool IsDowned(EntityUid body) =>
        !_mobState.IsDead(body) && TryComp(body, out WolfmedConsciousnessComponent? consciousness) &&
        consciousness.State == WolfmedConsciousness.Downed;

    private void OnAction(Entity<WolfmedCallForHelpComponent> ent, ref WolfmedCallForHelpActionEvent args)
    {
        if (args.Handled || !IsDowned(ent))
            return;

        args.Handled = true;
        if (ent.Comp.CooldownUntil > _timing.CurTime)
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-call-for-help-cooldown"), ent, ent);
            return;
        }

        var maxLength = args.MaxLength;
        if (!TryComp(ent, out ActorComponent? actor))
        {
            CallForHelp(ent, null, maxLength);
            return;
        }

        var body = ent.Owner;
        var session = actor.PlayerSession;
        _quickDialog.OpenDialog(session, Loc.GetString("wolfmed-call-for-help-title"),
            Loc.GetString("wolfmed-call-for-help-prompt", ("default", Loc.GetString("wolfmed-call-for-help-default"))),
            (string line) =>
            {
                if (session.AttachedEntity == body)
                    CallForHelp(body, line, maxLength);
            });
    }

    /// <summary>
    /// The call itself: shouts the line (the default one when it is empty), flags the body and starts the
    /// cooldown. False when the body is not Downed or the cooldown is still running.
    /// </summary>
    public bool CallForHelp(EntityUid body, string? line, int maxLength)
    {
        if (!IsDowned(body))
            return false;

        var comp = EnsureComp<WolfmedCallForHelpComponent>(body);
        var now = _timing.CurTime;
        if (comp.CooldownUntil > now)
            return false;

        var text = string.IsNullOrWhiteSpace(line) ? Loc.GetString("wolfmed-call-for-help-default") : line.Trim();
        if (text.Length > maxLength)
            text = text[..Math.Max(0, maxLength)];

        _chat.TrySendInGameICMessage(body, text, InGameICChatType.Speak, ChatTransmitRange.Normal);

        comp.FlagUntil = now + TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.CallForHelpSeconds)));
        comp.CooldownUntil = now + TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.CallForHelpCooldown)));
        Dirty(body, comp);

        if (comp.Action is { } action)
            _actions.SetCooldown(action, now, comp.CooldownUntil);

        return true;
    }
}
