using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    /// <summary>Keeps invitations discoverable and explains who can receive them.</summary>
    private void BuildInvitations(WFRoundSetupLobby lobby, WFRoundSetupState state)
    {
        var content = Column(6);
        content.AddChild(Heading("invite-players"));
        content.AddChild(Description("invite-help"));
        var empty = state.InviteTargets.Length == 0;
        var reason = empty ? "invite-none-help" :
            lobby.MemberCount >= WFRoundSetupPolicy.MaximumMembers ? "disabled-full" :
            lobby.Started && !lobby.AllowLateJoin ? "invite-reopen-help" : null;
        var target = new WFRoundSetupOptionButton
        {
            Name = "CrewInviteTarget", HorizontalExpand = true, Disabled = reason != null,
            ToolTip = Text(reason ?? "invite-select-help"),
        };
        if (empty)
        {
            target.AddItem(Text("invite-none"), 0);
            target.SelectId(0);
        }
        else
        {
            for (var i = 0; i < state.InviteTargets.Length; i++)
                target.AddItem(state.InviteTargets[i].Name, i);
            target.SelectId(Math.Max(0, Array.FindIndex(state.InviteTargets, player => player.UserId == _inviteTarget)));
            target.OnItemSelected += args =>
            {
                target.SelectId(args.Id);
                _inviteTarget = state.InviteTargets[args.Id].UserId;
            };
        }
        var invite = Button("send-invitation", () => SendTarget(WFRoundSetupAction.Invite,
            lobby.Id, state.InviteTargets[target.SelectedId].UserId), reason != null, reason);
        invite.Name = "CrewSendInvitation";
        invite.AddStyleClass("ButtonAccept");
        invite.ToolTip = Text(reason ?? "invite-send-help");
        content.AddChild(FlowRow(target, invite));
        content.AddChild(Description(reason ?? "invite-response-help"));
        _body.AddChild(Panel(content));
    }
}
