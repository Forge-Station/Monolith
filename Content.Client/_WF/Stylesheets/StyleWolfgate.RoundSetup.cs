using Content.Client._WF.RoundSetup;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WF.Stylesheets;

public sealed partial class StyleWolfgate
{
    /// <summary>Gives crew preparation translucent surfaces while keeping text and controls fully readable.</summary>
    private StyleRule[] RoundSetupRules()
    {
        var window = Box("window_panel.png", 9);
        window.Modulate = Color.White.WithAlpha(0.75f);
        var surface = new StyleBoxFlat { BackgroundColor = Ink.WithAlpha(0.7f) };
        var card = new StyleBoxFlat
        {
            BackgroundColor = GlassRaised.WithAlpha(0.62f),
            BorderColor = EdgeLight,
            BorderThickness = new Thickness(1),
        };
        var inset = new StyleBoxFlat
        {
            BackgroundColor = Ink.WithAlpha(0.35f),
            BorderColor = EdgeSoft,
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        return new StyleRule[]
        {
            Child().Parent(Element<WFRoundSetupWindow>()).Child(Element<PanelContainer>().Class(DefaultWindow.StyleClassWindowPanel))
                .Prop(PanelContainer.StylePropertyPanel, window),
            Element<PanelContainer>().Class("WFRoundSetupSurface")
                .Prop(PanelContainer.StylePropertyPanel, surface),
            Element<PanelContainer>().Class("WFRoundSetupCard")
                .Prop(PanelContainer.StylePropertyPanel, card),
            Element<PanelContainer>().Class("WFRoundSetupInset")
                .Prop(PanelContainer.StylePropertyPanel, inset),
            Element<RichTextLabel>().Class("WFRoundSetupTitle")
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(variation: "Bold", size: 26))
                .Prop(Label.StylePropertyFontColor, Text),
            Element<Label>().Class("WFRoundSetupEyebrow")
                .Prop(Label.StylePropertyFont, Mono(11))
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class("WFRoundSetupHeading")
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(variation: "Bold", size: 17))
                .Prop(Label.StylePropertyFontColor, Text),
            Element<RichTextLabel>().Class("WFRoundSetupHeading")
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(variation: "Bold", size: 17))
                .Prop(Label.StylePropertyFontColor, Text),
            Element<Label>().Class("WFRoundSetupMuted")
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<RichTextLabel>().Class("WFRoundSetupMuted")
                .Prop(Label.StylePropertyFontColor, TextMuted),
        };
    }
}
