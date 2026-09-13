using Content.Client._WF.Humanoid;
using Content.Client._WF.UserInterface.Controls;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WF.Stylesheets;

public sealed partial class StyleWolfgate
{
    public const string StyleClassCreatorCard = "CreatorCard";
    public const string StyleClassCreatorPreview = "CreatorPreview";
    public const string StyleClassCreatorHeading = "CreatorHeading";
    public const string StyleClassCreatorFieldLabel = "CreatorFieldLabel";
    public const string StyleClassCreatorWarning = "CreatorWarning";
    public const string StyleClassCreatorTabs = "CreatorTabs";
    public const string StyleClassCreatorPrimary = "CreatorPrimary";
    public const string StyleClassCreatorToggle = "CreatorToggle";
    public const string StyleClassCreatorSlot = "CreatorSlot";
    public const string StyleClassLinkButton = "LinkButton";
    public const string StyleClassCreatorBackdrop = "CreatorBackdrop";

    /// <summary>Bordered inset for a text field, so an editor reads as a box rather than loose text.</summary>
    public const string StyleClassCreatorInset = "CreatorInset";

    /// <summary>Translucent panel grouping a set of species cards.</summary>
    public const string StyleClassCreatorGroup = "CreatorGroup";

    /// <summary>Card title: the heading font at card scale, keeping the accent colour.</summary>
    public const string StyleClassCreatorCardTitle = "CreatorCardTitle";

    /// <summary>Sex selector glyphs. The texture comes from the skin, the tint from the on/off class.</summary>
    public const string StyleClassSexIconMale = "SexIconMale";
    public const string StyleClassSexIconFemale = "SexIconFemale";
    public const string StyleClassSexIconNone = "SexIconNone";
    public const string StyleClassSexIconOn = "SexIconOn";
    public const string StyleClassSexIconOff = "SexIconOff";

    /// <summary>Rules for the character creator: cards, headings, tabs, primary/toggle/link buttons, slot cards and swatches.</summary>
    private StyleRule[] CreatorRules()
    {
        var card = new StyleBoxFlat
        {
            BackgroundColor = GlassRaised.WithAlpha(0.85f),
            BorderColor = EdgeSoft,
            BorderThickness = new Thickness(1),
        };
        card.SetContentMarginOverride(StyleBox.Margin.All, 12);

        var preview = Box("window_panel.png", 9);

        var tabActive = new StyleBoxFlat { BackgroundColor = GlassLight, BorderColor = Accent, BorderThickness = new Thickness(0, 0, 0, 2) };
        tabActive.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        tabActive.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);
        var tabInactive = new StyleBoxFlat { BackgroundColor = Glass, BorderColor = EdgeSoft, BorderThickness = new Thickness(0, 0, 0, 1) };
        tabInactive.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        tabInactive.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);

        var linkBox = new StyleBoxFlat { BackgroundColor = Color.White.WithAlpha(0.16f) };
        linkBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        linkBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);

        var slotBox = Box("panel.png", 9);
        slotBox.SetContentMarginOverride(StyleBox.Margin.All, 4);

        var swatchBox = new StyleBoxFlat { BackgroundColor = Color.White };

        // Inset field: sunken and outlined, so a text editor has an edge.
        var insetBox = new StyleBoxFlat
        {
            BackgroundColor = Ink.WithAlpha(0.55f),
            BorderColor = EdgeSoft,
            BorderThickness = new Thickness(1),
        };
        insetBox.SetContentMarginOverride(StyleBox.Margin.All, 4);

        // Group backpane: a tint rather than a solid card, so the tiles inside still read as the surface.
        var groupBox = new StyleBoxFlat
        {
            BackgroundColor = GlassLight.WithAlpha(0.35f),
            BorderColor = EdgeSoft,
            BorderThickness = new Thickness(1),
        };
        groupBox.SetContentMarginOverride(StyleBox.Margin.All, 10);

        return new StyleRule[]
        {
            Element<PanelContainer>().Class(StyleClassCreatorBackdrop)
                .Prop(PanelContainer.StylePropertyPanel, Flat(Ink.WithAlpha(0.94f))),
            Element<PanelContainer>().Class(StyleClassCreatorCard)
                .Prop(PanelContainer.StylePropertyPanel, card),
            Element<PanelContainer>().Class(StyleClassCreatorPreview)
                .Prop(PanelContainer.StylePropertyPanel, preview),
            Element<Label>().Class(StyleClassCreatorHeading)
                .Prop(Label.StylePropertyFont, Display(16))
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class(StyleClassCreatorFieldLabel)
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<RichTextLabel>().Class(StyleClassCreatorWarning)
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(size: 10)),
            Element<TabContainer>().Class(StyleClassCreatorTabs)
                .Prop("font", Display(15))
                .Prop("tab-font-color", Text)
                .Prop(TabContainer.StylePropertyTabFontColorInactive, TextMuted)
                .Prop(TabContainer.StylePropertyTabStyleBox, tabActive)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabInactive),

            // Primary action (save): good-green with a display label
            Element<Button>().Class(StyleClassCreatorPrimary).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodDefault),
            Element<Button>().Class(StyleClassCreatorPrimary).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodHovered),
            Element<Button>().Class(StyleClassCreatorPrimary).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodDisabled),
            Child().Parent(Element<Button>().Class(StyleClassCreatorPrimary)).Child(Element<Label>())
                .Prop(Label.StylePropertyFont, Display(16)),

            // Toggle: lit accent when on
            Element<Button>().Class(StyleClassCreatorToggle).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, AccentDim),
            Child().Parent(Element<Button>().Class(StyleClassCreatorToggle).Pseudo(ContainerButton.StylePseudoClassPressed)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, Text),

            // Link-style buttons
            Element<Button>().Class(StyleClassLinkButton)
                .Prop(ContainerButton.StylePropertyStyleBox, linkBox),
            Element<Button>().Class(StyleClassLinkButton).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, Color.Transparent),
            Element<Button>().Class(StyleClassLinkButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, Accent),
            Element<Button>().Class(StyleClassLinkButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, AccentDim),
            Element<Button>().Class(StyleClassLinkButton).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, Color.Transparent),
            Child().Parent(Element<Button>().Class(StyleClassLinkButton)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Child().Parent(Element<Button>().Class(StyleClassLinkButton).Pseudo(ContainerButton.StylePseudoClassHover)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, Accent),

            // Character slot cards: the selected slot (pressed) is tinted with the selection colour
            Element<ContainerButton>().Class(StyleClassCreatorSlot)
                .Prop(ContainerButton.StylePropertyStyleBox, slotBox),
            Element<ContainerButton>().Class(StyleClassCreatorSlot).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, EdgeSoft),
            Element<ContainerButton>().Class(StyleClassCreatorSlot).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, EdgeLight),
            Element<ContainerButton>().Class(StyleClassCreatorSlot).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, AccentDim),
            Element<ContainerButton>().Class(StyleClassCreatorSlot).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, Ink),

            // Marking tiles: dark cards, lit when the marking is chosen
            Element<ContainerButton>().Class(WolfgateMarkingTile.StyleClassTile)
                .Prop(ContainerButton.StylePropertyStyleBox, slotBox),
            Element<ContainerButton>().Class(WolfgateMarkingTile.StyleClassTile).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, EdgeSoft),
            Element<ContainerButton>().Class(WolfgateMarkingTile.StyleClassTile).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, EdgeLight),
            Element<ContainerButton>().Class(WolfgateMarkingTile.StyleClassTile).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, AccentDim),
            Element<ContainerButton>().Class(WolfgateMarkingTile.StyleClassTile).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, Ink),
            new StyleRule(new SelectorChild(
                    new SelectorChild(
                        new SelectorElement(typeof(ContainerButton), new[] { WolfgateMarkingTile.StyleClassTile }, null, new[] { ContainerButton.StylePseudoClassPressed }),
                        new SelectorElement(typeof(BoxContainer), null, null, null)),
                    new SelectorElement(typeof(Label), null, null, null)),
                new[] { new StyleProperty(Label.StylePropertyFontColor, Text) }),

            Element<PanelContainer>().Class(StyleClassCreatorInset)
                .Prop(PanelContainer.StylePropertyPanel, insetBox),
            Element<PanelContainer>().Class(StyleClassCreatorGroup)
                .Prop(PanelContainer.StylePropertyPanel, groupBox),
            Element<Label>().Class(StyleClassCreatorCardTitle)
                .Prop(Label.StylePropertyFont, Display(13))
                .Prop(Label.StylePropertyFontColor, Accent),

            // Sex selector glyphs: texture per sex, tint per selection state. A child TextureRect does not
            // inherit the button's pressed modulate, so the selector swaps the on/off class in code.
            Element<TextureRect>().Class(StyleClassSexIconMale)
                .Prop(TextureRect.StylePropertyTexture, Tex("sex_male.png")),
            Element<TextureRect>().Class(StyleClassSexIconFemale)
                .Prop(TextureRect.StylePropertyTexture, Tex("sex_female.png")),
            Element<TextureRect>().Class(StyleClassSexIconNone)
                .Prop(TextureRect.StylePropertyTexture, Tex("sex_none.png")),
            Element<TextureRect>().Class(StyleClassSexIconOn)
                .Prop(Control.StylePropertyModulateSelf, Text),
            Element<TextureRect>().Class(StyleClassSexIconOff)
                .Prop(Control.StylePropertyModulateSelf, TextMuted),

            // Colour swatches: a ring appears around the swatch on hover
            Element<ContainerButton>().Class(WolfgateColorPicker.StyleClassSwatch)
                .Prop(ContainerButton.StylePropertyStyleBox, swatchBox),
            Element<ContainerButton>().Class(WolfgateColorPicker.StyleClassSwatch).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, Color.Transparent),
            Element<ContainerButton>().Class(WolfgateColorPicker.StyleClassSwatch).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, Accent),
            Element<ContainerButton>().Class(WolfgateColorPicker.StyleClassSwatch).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, Text),
        };
    }
}
