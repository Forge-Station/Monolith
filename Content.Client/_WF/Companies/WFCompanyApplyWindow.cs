using System.Numerics;
using Content.Shared._WF.Companies;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Companies;

/// <summary>The application form: the rank asked for and why the character wants to join.</summary>
public sealed class WFCompanyApplyWindow : DefaultWindow
{
    public WFCompanyApplyWindow(WFCompanyListing company, Action<WFCompanyRequest> send)
    {
        Title = Loc.GetString("wf-company-apply-title", ("company", company.Name));
        MinSize = new Vector2(460, 360);
        var ranks = company.Ranks ?? Array.Empty<WFCompanyOpenRank>();
        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(10) };

        layout.AddChild(new Label { Text = Loc.GetString("wf-company-apply-rank") });
        var rank = new OptionButton { HorizontalExpand = true };
        rank.AddItem(Loc.GetString("wf-company-apply-any-rank"), 0);
        for (var i = 0; i < ranks.Length; i++)
            rank.AddItem(ranks[i].Title, i + 1);
        // Applicants usually start at the bottom, so the lowest rank is the default.
        rank.SelectId(ranks.Length);
        rank.OnItemSelected += args => rank.SelectId(args.Id);
        layout.AddChild(rank);

        layout.AddChild(new Label { Text = Loc.GetString("wf-company-apply-message") });
        var message = new TextEdit { MinHeight = 140, VerticalExpand = true, HorizontalExpand = true,
            Placeholder = new Rope.Leaf(Loc.GetString("wf-company-apply-placeholder")) };
        layout.AddChild(message);
        var count = new Label { FontColorOverride = Color.Gray };
        layout.AddChild(count);
        var submit = new Button { Text = Loc.GetString("wf-company-apply-send") };
        submit.AddStyleClass("ButtonAccept");
        layout.AddChild(submit);

        void Count()
        {
            var length = Rope.Collapse(message.TextRope).Trim().Length;
            count.Text = Loc.GetString("wf-company-apply-count", ("count", length), ("max", WFCompanyPolicy.MaximumApplicationLength));
            submit.Disabled = length > WFCompanyPolicy.MaximumApplicationLength;
        }
        message.OnTextChanged += _ => Count();
        Count();
        submit.OnPressed += _ =>
        {
            send(new WFCompanyRequest(WFCompanyAction.Apply)
            {
                CompanyId = company.Id,
                RankId = rank.SelectedId == 0 ? null : ranks[rank.SelectedId - 1].Id,
                Description = Rope.Collapse(message.TextRope),
            });
            Close();
        };
        Contents.AddChild(layout);
    }
}
