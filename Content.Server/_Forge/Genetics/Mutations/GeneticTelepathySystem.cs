using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Prayer;
using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Actions;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._Forge.Genetics.Mutations;

public sealed class GeneticTelepathySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;
    [Dependency] private readonly PrayerSystem _prayer = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticTelepathyComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticTelepathyComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GeneticTelepathyComponent, GeneticTelepathyActionEvent>(OnAction);
    }

    private void OnStartup(Entity<GeneticTelepathyComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<GeneticTelepathyComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnAction(Entity<GeneticTelepathyComponent> ent, ref GeneticTelepathyActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(ent.Owner, out ActorComponent? actor))
            return;

        var target = args.Target;
        if (!HasComp<MobStateComponent>(target) || !TryComp(target, out ActorComponent? targetActor))
        {
            _popup.PopupEntity(Loc.GetString("gene-telepathy-no-mind"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        if (!_interaction.InRangeUnobstructed(ent.Owner, target, range: ent.Comp.Range))
        {
            _popup.PopupEntity(Loc.GetString("gene-telepathy-blocked"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;
        var session = actor.PlayerSession;
        var targetSession = targetActor.PlayerSession;
        var user = ent.Owner;

        _quickDialog.OpenDialog(session,
            Loc.GetString("gene-telepathy-ui-title"),
            Loc.GetString("gene-telepathy-ui-message"),
            (string message) =>
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                if (!Exists(user) || !Exists(target) || !HasComp<GeneticTelepathyComponent>(user))
                    return;

                if (!_interaction.InRangeUnobstructed(user, target, range: ent.Comp.Range))
                {
                    _popup.PopupEntity(Loc.GetString("gene-telepathy-blocked"), user, user, PopupType.SmallCaution);
                    return;
                }

                var popup = Loc.GetString("gene-telepathy-heard");
                _prayer.SendSubtleMessage(targetSession, session, message, popup);
                _popup.PopupEntity(Loc.GetString("gene-telepathy-sent"), user, user);
                _adminLogger.Add(LogType.Action, LogImpact.Low,
                    $"{ToPrettyString(user):player} telepathically messaged {ToPrettyString(target):target}: {message}");
                _chat.SendAdminAnnouncement(Loc.GetString("gene-telepathy-admin",
                    ("user", ToPrettyString(user)),
                    ("target", ToPrettyString(target)),
                    ("message", message)));
            });
    }
}
