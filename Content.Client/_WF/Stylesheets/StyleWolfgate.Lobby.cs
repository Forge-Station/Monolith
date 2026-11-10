using System.Collections.Generic;
using System.Linq;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WF.Stylesheets;

public sealed partial class StyleWolfgate
{
    public const string StyleClassLobbyShade = "LobbyShade";
    public const string StyleClassLobbyTitle = "LobbyTitle";
    public const string StyleClassLobbyStatus = "LobbyStatus";
    public const string StyleClassLobbyInfo = "LobbyInfo";
    public const string StyleClassLobbyNav = "LobbyNav";
    public const string StyleClassLobbyNavPrimary = "LobbyNavPrimary";
    public const string StyleClassLobbyLinks = "LobbyLinks";
    public const string StyleClassLobbyChatPanel = "LobbyChatPanel";
    public const string StyleClassLobbyHero = "LobbyHero";
    public const string StyleClassLobbyHeroName = "LobbyHeroName";
    public const string StyleClassLobbyHeroText = "LobbyHeroText";
    public const string StyleClassLobbyBar = "LobbyBar";
    public const string StyleClassLobbyBarText = "LobbyBarText";
    public const string StyleClassLobbyScanlines = "LobbyScanlines";
    public const string StyleClassLobbyTitleRule = "LobbyTitleRule";

    /// <summary>Rules for the game-menu lobby: title block, menu entries, link row, hero frame, chat dock and info strip.</summary>
    private StyleRule[] LobbyRules()
    {
        var shade = new StyleBoxTexture { Texture = Tex("lobby_shade.png") };

        // Full-window scanlines and the title rule: drawn by the retro skin, clear textures in the futurist one
        var scanlines = new StyleBoxTexture { Texture = Tex("scanlines.png"), Mode = StyleBoxTexture.StretchMode.Tile };
        var titleRule = new StyleBoxTexture { Texture = Tex("title_rule.png") };

        var navBox = new StyleBoxTexture { Texture = Tex("nav_button.png") };
        navBox.SetPatchMargin(StyleBox.Margin.All, 2);
        navBox.SetPatchMargin(StyleBox.Margin.Left, 4);
        navBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 9);
        navBox.SetContentMarginOverride(StyleBox.Margin.Right, 12);
        navBox.SetContentMarginOverride(StyleBox.Margin.Left, 20);

        var linkBox = new StyleBoxFlat { BackgroundColor = Color.White.WithAlpha(0.16f) };
        linkBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        linkBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);

        var chatPanel = Box("window_panel.png", 9);
        chatPanel.Modulate = Color.White.WithAlpha(0.72f);
        var heroPanel = Box("window_panel.png", 9);
        heroPanel.Modulate = Color.White.WithAlpha(0.65f);
        var bar = new StyleBoxFlat
        {
            BackgroundColor = Ink.WithAlpha(0.75f),
            BorderColor = EdgeSoft,
            BorderThickness = new Thickness(0, 1, 0, 0),
        };

        // The banners nest their buttons one box deep: banner > box > button > label
        var links = new SelectorElement(typeof(BoxContainer), new[] { StyleClassLobbyLinks }, null, null);
        var linkRow = new SelectorChild(links, new SelectorElement(typeof(BoxContainer), null, null, null));
        var linkButton = new SelectorChild(linkRow, new SelectorElement(typeof(Button), null, null, null));
        var linkButtonHover = new SelectorChild(linkRow,
            new SelectorElement(typeof(Button), null, null, new[] { ContainerButton.StylePseudoClassHover }));
        var linkLabel = new SelectorElement(typeof(Label), null, null, null);

        var rules = new List<StyleRule>
        {
            Element<PanelContainer>().Class(StyleClassLobbyShade)
                .Prop(PanelContainer.StylePropertyPanel, shade),
            Element<PanelContainer>().Class(StyleClassLobbyScanlines)
                .Prop(PanelContainer.StylePropertyPanel, scanlines),
            Element<PanelContainer>().Class(StyleClassLobbyTitleRule)
                .Prop(PanelContainer.StylePropertyPanel, titleRule),
            Element<Label>().Class(StyleClassLobbyTitle)
                .Prop(Label.StylePropertyFont, Display(32))
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<RichTextLabel>().Class(StyleClassLobbyTitle)
                .Prop(Label.StylePropertyFont, Display(32))
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class(StyleClassLobbyStatus)
                .Prop(Label.StylePropertyFont, Display(16))
                .Prop(Label.StylePropertyFontColor, TextMuted),
            // Round info: ServerInfo builds its label in code, so the rule reaches it through the box
            Child().Parent(Element<BoxContainer>().Class(StyleClassLobbyInfo)).Child(Element<RichTextLabel>())
                .Prop(Label.StylePropertyFontColor, TextMuted),

            // Link row: the banner controls build plain buttons in code, so style them by position
            new StyleRule(linkButton, new[]
            {
                new StyleProperty(ContainerButton.StylePropertyStyleBox, linkBox),
                new StyleProperty(Control.StylePropertyModulateSelf, Color.Transparent),
            }),
            new StyleRule(linkButtonHover, new[]
            {
                new StyleProperty(Control.StylePropertyModulateSelf, Accent),
            }),
            new StyleRule(new SelectorChild(linkButton, linkLabel), new[]
            {
                new StyleProperty(Label.StylePropertyFont, _resCache.NotoStack(size: 12)),
                new StyleProperty(Label.StylePropertyFontColor, TextMuted),
            }),
            new StyleRule(new SelectorChild(linkButtonHover, linkLabel), new[]
            {
                new StyleProperty(Label.StylePropertyFontColor, Accent),
            }),

            Element<PanelContainer>().Class(StyleClassLobbyChatPanel)
                .Prop(PanelContainer.StylePropertyPanel, chatPanel),
            Element<PanelContainer>().Class(StyleClassLobbyHero)
                .Prop(PanelContainer.StylePropertyPanel, heroPanel),
            Element<Label>().Class(StyleClassLobbyHeroName)
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(variation: "Bold", size: 14))
                .Prop(Label.StylePropertyFontColor, Text),
            Element<RichTextLabel>().Class(StyleClassLobbyHeroName)
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(variation: "Bold", size: 14))
                .Prop(Label.StylePropertyFontColor, Text),
            Element<Label>().Class(StyleClassLobbyHeroText)
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<RichTextLabel>().Class(StyleClassLobbyHeroText)
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<PanelContainer>().Class(StyleClassLobbyBar)
                .Prop(PanelContainer.StylePropertyPanel, bar),
            Element<Label>().Class(StyleClassLobbyBarText)
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<RichTextLabel>().Class(StyleClassLobbyBarText)
                .Prop(Label.StylePropertyFontColor, TextMuted),
        };

        // Secondary entries are text-only until hover; joining actions keep their state colours.
        // Each entry carries a single class because the XAML loader does not split space-separated StyleClasses.
        var secondaryNavBox = new StyleBoxTexture(navBox);
        secondaryNavBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 3);
        rules.AddRange(NavRules(StyleClassLobbyNav, secondaryNavBox, Color.Transparent));
        rules.AddRange(NavRules(StyleClassLobbyNavPrimary, navBox, EdgeLight));
        rules.AddRange(NavRules("WFLobbyCrewAction", navBox, AccentDim));
        foreach (var (state, color) in new[] { (StyleNano.StyleClassButtonColorRed, Danger), (StyleNano.StyleClassButtonColorGreen, Good) })
        {
            rules.AddRange(new StyleRule[]
            {
                Element<Button>().Class(StyleClassLobbyNavPrimary, state)
                    .Prop(Control.StylePropertyModulateSelf, color.WithAlpha(0.8f)),
                Element<Button>().Class(StyleClassLobbyNavPrimary, state).Pseudo(ContainerButton.StylePseudoClassHover)
                    .Prop(Control.StylePropertyModulateSelf, color),
                Element<Button>().Class(StyleClassLobbyNavPrimary, state).Pseudo(ContainerButton.StylePseudoClassDisabled)
                    .Prop(Control.StylePropertyModulateSelf, color.WithAlpha(0.25f)),
                Child().Parent(Element<Button>().Class(StyleClassLobbyNavPrimary, state)).Child(Element<Label>())
                    .Prop(Label.StylePropertyFontColor, Text),
            });
        }
        return rules.ToArray();
    }

    private IEnumerable<StyleRule> NavRules(string styleClass, StyleBox box, Color normal)
    {
        var button = Element<Button>().Class(styleClass);
        var label = Element<Label>();
        return new StyleRule[]
        {
            Element<Button>().Class(styleClass)
                .Prop(ContainerButton.StylePropertyStyleBox, box),
            Element<Button>().Class(styleClass).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, normal),
            Element<Button>().Class(styleClass).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, Accent),
            Element<Button>().Class(styleClass).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, Accent),
            Element<Button>().Class(styleClass).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, Color.Transparent),
            Child().Parent(button).Child(label)
                .Prop(Label.StylePropertyFont, Menu(20))
                .Prop(Label.StylePropertyFontColor, Text)
                .Prop(Label.StylePropertyAlignMode, Label.AlignMode.Left),
            Child().Parent(Element<Button>().Class(styleClass).Pseudo(ContainerButton.StylePseudoClassHover)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, Accent),
            Child().Parent(Element<Button>().Class(styleClass).Pseudo(ContainerButton.StylePseudoClassPressed)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, Accent),
            Child().Parent(Element<Button>().Class(styleClass).Pseudo(ContainerButton.StylePseudoClassDisabled)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, TextDisabled),
        };
    }
}
