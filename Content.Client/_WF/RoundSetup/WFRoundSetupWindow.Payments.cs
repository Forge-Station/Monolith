using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    /// <summary>Shows the agreed allocation and lets the leader assign each crew member's contribution.</summary>
    private void BuildContributions(WFRoundSetupState state, WFRoundSetupLobby lobby, bool host)
    {
        if (state.Payment is not { Price: > 0 } payment)
            return;
        var section = Column(6);
        section.AddChild(Heading("payment-title"));
        section.AddChild(Description("payment-help"));
        var allocated = PlainDescription(Text("payment-total", ("allocated", payment.Allocated), ("price", Price(payment.Price))));
        section.AddChild(allocated);
        foreach (var member in lobby.Members)
        {
            var identity = PlainDescription(member.Name);
            if (!host)
            {
                section.AddChild(Row(identity, PlainDescription(Price(member.Contribution))));
                continue;
            }
            var amount = new LineEdit { Text = member.Contribution.ToString(), HorizontalExpand = true,
                Name = "CrewContribution", ToolTip = Text("payment-amount-help", ("max", Price(payment.Price))) };
            var apply = Button("payment-apply", () =>
            {
                if (int.TryParse(amount.Text, out var value))
                    SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SetContribution)
                    {
                        LobbyId = lobby.Id, Target = member.UserId, Contribution = value, PaymentRevision = payment.Revision,
                    });
            });
            void Validate()
            {
                apply.Disabled = !int.TryParse(amount.Text, out var value) || value < 0 || value > payment.Price || value == member.Contribution;
                apply.ToolTip = apply.Disabled ? Text("payment-amount-help", ("max", Price(payment.Price))) : null;
            }
            amount.OnTextChanged += _ => Validate();
            Validate();
            section.AddChild(FlowRow(identity, amount, apply));
        }
        _body.AddChild(Panel(section));
    }
}
