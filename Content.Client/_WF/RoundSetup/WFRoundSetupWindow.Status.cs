using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    /// <summary>Picks the roster label for a member, using their condition once they have spawned.</summary>
    private static string MemberStatusKey(bool spawned, bool ready, WFCrewMemberStatus status) => !spawned
        ? ready ? "member-ready" : "member-not-ready"
        : status switch
        {
            WFCrewMemberStatus.Critical => "member-critical",
            WFCrewMemberStatus.Dead => "member-dead",
            WFCrewMemberStatus.Missing => "member-missing",
            _ => "member-alive",
        };

    private static string ShipConditionKey(WFCrewShipCondition condition) => condition switch
    {
        WFCrewShipCondition.Damaged => "ship-condition-damaged",
        WFCrewShipCondition.Wrecked => "ship-condition-wrecked",
        WFCrewShipCondition.Gone => "ship-condition-gone",
        _ => "ship-condition-intact",
    };

    private static Color ShipConditionColor(WFCrewShipCondition condition) => condition switch
    {
        WFCrewShipCondition.Damaged => Color.Gold,
        WFCrewShipCondition.Wrecked or WFCrewShipCondition.Gone => Color.OrangeRed,
        _ => Color.LightGreen,
    };

    private static bool NeedsJoinWarning(WFRoundSetupLobby lobby) =>
        WFCrewStatusPolicy.NeedsJoinWarning(lobby.Started, lobby.Roster.Select(mate => mate.Status).ToArray(), lobby.ShipCondition);

    /// <summary>Summarises a launched crew's living members and ship condition.</summary>
    private static Control CrewHealthSummary(WFRoundSetupLobby lobby)
    {
        var statuses = lobby.Roster.Select(mate => mate.Status).ToArray();
        var living = WFCrewStatusPolicy.Living(statuses);
        var deployed = WFCrewStatusPolicy.Deployed(statuses);
        var content = Column(2);
        var crew = new RichTextLabel { HorizontalExpand = true };
        crew.SetMessage(FormattedMessage.FromUnformatted(Text("crew-alive", ("living", living), ("deployed", deployed))),
            defaultColor: deployed > 0 && living == 0 ? Color.OrangeRed : Color.LightGreen);
        content.AddChild(crew);
        if (lobby.ShipCondition != WFCrewShipCondition.None)
        {
            var ship = new RichTextLabel { HorizontalExpand = true };
            ship.SetMessage(FormattedMessage.FromUnformatted(Text(ShipConditionKey(lobby.ShipCondition))),
                defaultColor: ShipConditionColor(lobby.ShipCondition));
            content.AddChild(ship);
        }
        return content;
    }

    /// <summary>Explains why joining a crew that has lost its people or ship needs a second press.</summary>
    private static Control JoinWarning()
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(Text("join-warning")), defaultColor: Color.OrangeRed);
        return label;
    }

    /// <summary>A join action that asks for confirmation when the crew is dead or its ship is lost.</summary>
    private static Button JoinButton(WFRoundSetupLobby lobby, string key, Action pressed, bool disabled, string? disabledReason)
    {
        if (disabled || !NeedsJoinWarning(lobby))
            return Button(key, pressed, disabled, disabledReason);
        var button = new ConfirmButton
        {
            Text = Text(key), ConfirmationText = Text("join-warning-confirm"), MinHeight = 40, HorizontalExpand = true,
            ToolTip = Text("join-warning"), ResetTime = TimeSpan.FromSeconds(5),
        };
        button.Label.ClipText = false;
        button.AddStyleClass("ButtonColorRed");
        button.OnPressed += _ => pressed();
        return button;
    }
}
