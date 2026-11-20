using System.Linq;
using System.Numerics;
using Content.Server.Speech;
using Content.Shared._WF.Strangers;
using Content.Shared.Cargo.Components;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Shared.Player;

namespace Content.Server._WF.ShipSquatters;

public sealed partial class WFShipSquatterSystem
{
    private void InitializeTalk()
    {
        SubscribeNetworkEvent<WFStrangerChooseEvent>(OnChoose);
        SubscribeLocalEvent<WFShipSquatterComponent, GetVerbsEvent<ActivationVerb>>(OnVerbs);
        SubscribeLocalEvent<WFShipSquatterComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFShipSquatterComponent, ListenEvent>(OnListen);
    }

    private void OnVerbs(Entity<WFShipSquatterComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_mobs.IsAlive(ent) || !HasComp<ActorComponent>(args.User)
            || ent.Comp.Mood == WFSquatterMood.Angry)
            return;

        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("wf-ship-squatter-verb-talk"),
            Act = () => StartTalk(ent, user),
        });
    }

    /// <summary>Anything handed to him that looks like money is turned down; he takes nothing from anyone.</summary>
    private void OnInteractUsing(Entity<WFShipSquatterComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<CashComponent>(args.Used)
            && !(TryComp(args.Used, out StackComponent? stack) && stack.StackTypeId == "Credit"))
            return;

        args.Handled = true;
        Say(ent, ent.Comp.GiftBarks);
    }

    /// <summary>Whoever talks near him gets told he is staying.</summary>
    private void OnListen(EntityUid uid, WFShipSquatterComponent comp, ListenEvent args)
    {
        var now = _timing.CurTime;
        if (args.Source == uid || comp.Mood == WFSquatterMood.Angry || now < comp.NextBark || !_mobs.IsAlive(uid)
            || !HasComp<ActorComponent>(args.Source))
            return;

        comp.NextBark = now + TimeSpan.FromSeconds(comp.BarkGap);
        Say(uid, comp.TalkedBarks);
    }

    /// <summary>Opens the conversation at its start.</summary>
    public void StartTalk(Entity<WFShipSquatterComponent> ent, EntityUid user)
    {
        if (!_prototypes.TryIndex(ent.Comp.Dialogue, out var dialogue) || !_mobs.IsAlive(ent)
            || ent.Comp.Mood == WFSquatterMood.Angry || ent.Comp.Talking is { } other && other != user)
            return;

        _steering.Unregister(ent);
        ent.Comp.WanderUntil = null;
        ent.Comp.Talking = user;
        GoTo(ent, user, dialogue, dialogue.Start);
    }

    private void OnChoose(WFStrangerChooseEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user || !TryGetEntity(ev.Stranger, out var uid)
            || !TryComp(uid, out WFShipSquatterComponent? comp))
            return;

        Choose((uid.Value, comp), user, ev.Option);
    }

    /// <summary>The player answers with an option of the current node.</summary>
    public void Choose(Entity<WFShipSquatterComponent> ent, EntityUid user, int index)
    {
        var comp = ent.Comp;
        if (comp.Talking != user || comp.Node is not { } nodeId || !_prototypes.TryIndex(comp.Dialogue, out var dialogue)
            || !dialogue.Nodes.TryGetValue(nodeId, out var node) || index < 0 || index >= node.Options.Count)
            return;

        var option = node.Options[index];
        _chat.TrySendInGameICMessage(user, Loc.GetString(option.Text), InGameICChatType.Speak, false);
        if (option.Next is { } next)
            GoTo(ent, user, dialogue, next);
        else
            EndTalk(ent);
    }

    /// <summary>Moves the talk to a node: says its line and shows the answers. He turns down everything, so nothing costs or changes hands.</summary>
    private void GoTo(Entity<WFShipSquatterComponent> ent, EntityUid user, WFStrangerDialoguePrototype dialogue, string nodeId)
    {
        if (!dialogue.Nodes.TryGetValue(nodeId, out var node))
        {
            Log.Error($"Squatter dialogue {dialogue.ID} has no node {nodeId}");
            EndTalk(ent);
            return;
        }

        var line = Loc.GetString(node.Line);
        _chat.TrySendInGameICMessage(ent, line, InGameICChatType.Speak, false);
        var ended = node.Options.Count == 0;
        ent.Comp.Node = ended ? null : nodeId;
        if (TryComp(user, out ActorComponent? actor))
        {
            RaiseNetworkEvent(new WFStrangerDialogueEvent
            {
                Stranger = GetNetEntity(ent),
                Name = Name(ent),
                Line = line,
                Options = node.Options.Select(option => Loc.GetString(option.Text)).ToList(),
                Ended = ended,
            }, actor.PlayerSession);
        }

        if (ended)
            EndTalk(ent, tell: false);
    }

    /// <summary>The talk is over: the window closes and he goes back to sitting.</summary>
    private void EndTalk(Entity<WFShipSquatterComponent> ent, bool tell = true)
    {
        var comp = ent.Comp;
        if (tell && comp.Talking is { } user && TryComp(user, out ActorComponent? actor))
            RaiseNetworkEvent(new WFStrangerDialogueEvent { Stranger = GetNetEntity(ent), Ended = true }, actor.PlayerSession);

        comp.Talking = null;
        comp.Node = null;
        comp.NextWander = _timing.CurTime + Seconds(comp.LingerMin, comp.LingerMax);
    }

    /// <summary>A conversation ends when the other walks off.</summary>
    private void TickTalk(Entity<WFShipSquatterComponent> ent)
    {
        if (ent.Comp.Talking is not { } user)
            return;

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(user);
        if (TerminatingOrDeleted(user) || there.MapId != here.MapId || Vector2.Distance(here.Position, there.Position) > TalkRange)
            EndTalk(ent);
    }
}
