using System.Linq;
using System.Numerics;
using Content.Client._WF.ChatTabs;
using Content.Client._WF.UserInterface.WindowPopout;
using Content.Client.ContextMenu.UI;
using Content.Client.Examine;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Controls.FancyTree;
using Content.Client.Verbs.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WF.Stylesheets;

public sealed partial class StyleWolfgate
{
    /// <summary>
    /// Override rules. Selectors mirror the upstream ones so the later Wolfgate rule wins the specificity tie.
    /// </summary>
    private StyleRule[] BuildRules()
    {
        var display14 = Display(14);
        var display16 = Display(16);
        var display18 = Display(18);
        var display20 = Display(20);
        var mono11 = Mono(11);

        // Window chrome
        var windowPanel = Box("window_panel.png", 9);
        var borderedPanel = Box("panel_bordered.png", 3);
        var transparentPanel = Box("panel_bordered.png", 3);
        transparentPanel.Modulate = Color.White.WithAlpha(0.75f);
        var hotbarPanel = Box("panel_bordered.png", 3);
        hotbarPanel.SetExpandMargin(StyleBox.Margin.All, 4);
        var header = HeaderBox("window_header.png");
        var headerAlert = HeaderBox("window_header_alert.png");
        var menuPanel = Box("menu_panel.png", 3);
        var tooltip = Box("tooltip.png", 3);
        tooltip.SetContentMarginOverride(StyleBox.Margin.Horizontal, 7);
        var tabPanel = Box("tab_panel.png", 3);
        var lineEdit = Box("lineedit.png", 4);
        lineEdit.SetContentMarginOverride(StyleBox.Margin.Horizontal, 5);
        var searchBox = Box("search_box.png", 3);
        searchBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 5);
        var invSlot = Box("inv_slot_background.png", 2);
        invSlot.SetContentMarginOverride(StyleBox.Margin.All, 0);
        var handHighlight = Box("hand_slot_highlight.png", 2);
        var stripe = new StyleBoxTexture { Texture = Tex("stripeback.png"), Mode = StyleBoxTexture.StretchMode.Tile };
        var heading = new StyleBoxTexture
        {
            Texture = Tex("heading.png"),
            PatchMarginRight = 10,
            PatchMarginTop = 10,
            ContentMarginTopOverride = 2,
            ContentMarginLeftOverride = 10,
            PaddingTop = 4,
        };
        heading.SetPatchMargin(StyleBox.Margin.Left | StyleBox.Margin.Bottom, 2);

        // Outlined panels: white texture, the modulate colour becomes the outline and the fill is 45% of it.
        var panelTex = Tex("panel.png");
        var panel = new StyleBoxTexture { Texture = panelTex };
        panel.SetPatchMargin(StyleBox.Margin.All, 9);
        var panelOpenRight = new StyleBoxTexture(panel)
        {
            Texture = new AtlasTexture(panelTex, UIBox2.FromDimensions(new Vector2(0, 0), new Vector2(16, 32))),
        };
        panelOpenRight.SetPatchMargin(StyleBox.Margin.Right, 0);
        var panelOpenLeft = new StyleBoxTexture(panel)
        {
            Texture = new AtlasTexture(panelTex, UIBox2.FromDimensions(new Vector2(16, 0), new Vector2(16, 32))),
        };
        panelOpenLeft.SetPatchMargin(StyleBox.Margin.Left, 0);

        // Buttons (atlas slices match StyleBase so the open-sided variants keep joining seamlessly)
        var buttonTex = Tex("button.png");
        var button = new StyleBoxTexture { Texture = buttonTex };
        button.SetPatchMargin(StyleBox.Margin.All, 10);
        button.SetPadding(StyleBox.Margin.All, 1);
        button.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        button.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);

        var buttonOpenRight = new StyleBoxTexture(button)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(0, 0), new Vector2(14, 24))),
        };
        buttonOpenRight.SetPatchMargin(StyleBox.Margin.Right, 0);
        buttonOpenRight.SetContentMarginOverride(StyleBox.Margin.Right, 8);
        buttonOpenRight.SetPadding(StyleBox.Margin.Right, 2);

        var buttonOpenLeft = new StyleBoxTexture(button)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(10, 0), new Vector2(14, 24))),
        };
        buttonOpenLeft.SetPatchMargin(StyleBox.Margin.Left, 0);
        buttonOpenLeft.SetContentMarginOverride(StyleBox.Margin.Left, 8);
        buttonOpenLeft.SetPadding(StyleBox.Margin.Left, 1);

        var buttonOpenBoth = new StyleBoxTexture(button)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(10, 0), new Vector2(3, 24))),
        };
        buttonOpenBoth.SetPatchMargin(StyleBox.Margin.Horizontal, 0);
        buttonOpenBoth.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        buttonOpenBoth.SetPadding(StyleBox.Margin.Right, 2);
        buttonOpenBoth.SetPadding(StyleBox.Margin.Left, 1);

        var buttonSmall = Box("button_small.png", 6);

        var buttonStorage = new StyleBoxTexture(button);
        buttonStorage.SetPadding(StyleBox.Margin.All, 0);
        buttonStorage.SetContentMarginOverride(StyleBox.Margin.Vertical, 0);
        buttonStorage.SetContentMarginOverride(StyleBox.Margin.Horizontal, 4);

        var selectedOpenRight = new StyleBoxTexture(buttonOpenRight) { Modulate = ButtonGoodDefault };
        var selectedOpenBoth = new StyleBoxTexture(buttonOpenBoth) { Modulate = ButtonGoodDefault };

        // Top bar buttons: no padding so the icons stay centred.
        var topButton = new StyleBoxTexture { Texture = buttonTex };
        topButton.SetPatchMargin(StyleBox.Margin.All, 10);
        topButton.SetPadding(StyleBox.Margin.All, 0);
        topButton.SetContentMarginOverride(StyleBox.Margin.All, 0);
        var topOpenRight = new StyleBoxTexture(topButton)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(0, 0), new Vector2(14, 24))),
        };
        topOpenRight.SetPatchMargin(StyleBox.Margin.Right, 0);
        var topOpenLeft = new StyleBoxTexture(topButton)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(10, 0), new Vector2(14, 24))),
        };
        topOpenLeft.SetPatchMargin(StyleBox.Margin.Left, 0);
        var topSquare = new StyleBoxTexture(topButton)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(10, 0), new Vector2(3, 24))),
        };
        topSquare.SetPatchMargin(StyleBox.Margin.Horizontal, 0);

        var pill = Box("pill.png", 8);
        pill.SetPadding(StyleBox.Margin.All, 2);
        var pillBordered = Box("pill_bordered.png", 8);
        pillBordered.SetPadding(StyleBox.Margin.All, 2);
        var chatTab = Box("pill.png", 8);
        chatTab.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        chatTab.SetContentMarginOverride(StyleBox.Margin.Horizontal, 9);

        // Sliders
        var sliderFillTex = Tex("slider_fill.png");
        var sliderFill = new StyleBoxTexture { Texture = sliderFillTex, Modulate = Accent };
        var sliderBack = new StyleBoxTexture { Texture = sliderFillTex, Modulate = Ink };
        var sliderFore = new StyleBoxTexture { Texture = Tex("slider_outline.png"), Modulate = Edge };
        var sliderGrab = new StyleBoxTexture { Texture = Tex("slider_grabber.png") };
        foreach (var box in new[] { sliderFill, sliderBack, sliderFore, sliderGrab })
            box.SetPatchMargin(StyleBox.Margin.All, 12);
        var sliderFillWhite = new StyleBoxTexture(sliderFill) { Modulate = Color.White };
        var sliderFillRed = new StyleBoxTexture(sliderFill) { Modulate = Danger };
        var sliderFillGreen = new StyleBoxTexture(sliderFill) { Modulate = Good };
        var sliderFillBlue = new StyleBoxTexture(sliderFill) { Modulate = Color.FromHex("#4C8DFF") };

        // Flat boxes
        var lowDivider = new StyleBoxFlat { BackgroundColor = EdgeSoft, ContentMarginLeftOverride = 2, ContentMarginBottomOverride = 2 };
        var highDivider = new StyleBoxFlat { BackgroundColor = Accent, ContentMarginLeftOverride = 2, ContentMarginBottomOverride = 2 };
        var tabActive = new StyleBoxFlat { BackgroundColor = GlassLight, BorderColor = Accent, BorderThickness = new Thickness(0, 0, 0, 2) };
        tabActive.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        var tabInactive = new StyleBoxFlat { BackgroundColor = Glass, BorderColor = EdgeSoft, BorderThickness = new Thickness(0, 0, 0, 1) };
        tabInactive.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        var progressBack = Flat(GlassRaised, vertical: 14.5f);
        var progressFore = Flat(AccentDim, vertical: 14.5f);
        var listItem = Flat(GlassRaised, 2, 4);
        var listItemSelected = Flat(Selection, 2, 4);
        var listItemDisabled = Flat(Ink, 2, 4);
        var chatPanel = new StyleBoxFlat { BackgroundColor = ChatBackground };
        var vGrabber = new StyleBoxFlat { BackgroundColor = EdgeLight.WithAlpha(0.6f), ContentMarginLeftOverride = StyleBase.DefaultGrabberSize, ContentMarginTopOverride = StyleBase.DefaultGrabberSize };
        var vGrabberHover = new StyleBoxFlat { BackgroundColor = AccentDim.WithAlpha(0.8f), ContentMarginLeftOverride = StyleBase.DefaultGrabberSize, ContentMarginTopOverride = StyleBase.DefaultGrabberSize };
        var vGrabberGrabbed = new StyleBoxFlat { BackgroundColor = Accent.WithAlpha(0.9f), ContentMarginLeftOverride = StyleBase.DefaultGrabberSize, ContentMarginTopOverride = StyleBase.DefaultGrabberSize };
        var hGrabber = new StyleBoxFlat { BackgroundColor = EdgeLight.WithAlpha(0.6f), ContentMarginTopOverride = StyleBase.DefaultGrabberSize };
        var hGrabberHover = new StyleBoxFlat { BackgroundColor = AccentDim.WithAlpha(0.8f), ContentMarginTopOverride = StyleBase.DefaultGrabberSize };
        var hGrabberGrabbed = new StyleBoxFlat { BackgroundColor = Accent.WithAlpha(0.9f), ContentMarginTopOverride = StyleBase.DefaultGrabberSize };

        var checkBoxChecked = Tex("checkbox_checked.png");
        var checkBoxUnchecked = Tex("checkbox_unchecked.png");

        var rules = new StyleRule[]
        {
            // Text
            Element<Label>()
                .Prop(Label.StylePropertyFontColor, Text),
            Element<Label>().Class(DefaultWindow.StyleClassWindowTitle)
                .Prop(Label.StylePropertyFont, display14)
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class("windowTitleAlert")
                .Prop(Label.StylePropertyFont, display14)
                .Prop(Label.StylePropertyFontColor, Text),
            Element<Label>().Class("FancyWindowTitle")
                .Prop(Label.StylePropertyFont, display14)
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class(StyleBase.StyleClassLabelHeading)
                .Prop(Label.StylePropertyFont, display16)
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<RichTextLabel>().Class(StyleBase.StyleClassLabelHeading)
                .Prop(Label.StylePropertyFont, display16)
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class(StyleNano.StyleClassLabelHeadingBigger)
                .Prop(Label.StylePropertyFont, display20)
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class(StyleBase.StyleClassLabelSubText)
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<RichTextLabel>().Class(StyleBase.StyleClassLabelSubText)
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<Label>().Class(StyleNano.StyleClassLabelKeyText)
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<RichTextLabel>().Class(StyleNano.StyleClassLabelKeyText)
                .Prop(Control.StylePropertyModulateSelf, Accent),
            Element<Label>().Class(StyleNano.StyleClassLabelSecondaryColor)
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<Label>().Class("StatusFieldTitle")
                .Prop(Label.StylePropertyFontColor, Accent),
            Element<Label>().Class("Good")
                .Prop(Label.StylePropertyFontColor, Good),
            Element<Label>().Class("Caution")
                .Prop(Label.StylePropertyFontColor, Caution),
            Element<Label>().Class("Danger")
                .Prop(Label.StylePropertyFontColor, Danger),
            Element<Label>().Class("Disabled")
                .Prop(Label.StylePropertyFontColor, TextDisabled),
            Element<Label>().Class(Placeholder.StyleClassPlaceholderText)
                .Prop(Label.StylePropertyFontColor, TextMuted.WithAlpha(0.5f)),
            Element<Label>().Class("WindowFooterText")
                .Prop(Label.StylePropertyFontColor, TextMuted.WithAlpha(0.6f)),
            Element<Label>().Class("PdaContentFooterText")
                .Prop(Label.StylePropertyFontColor, TextMuted.WithAlpha(0.6f)),
            Element<TextureRect>().Class("NTLogoDark")
                .Prop(Control.StylePropertyModulateSelf, EdgeLight),
            Element<Label>().Class(MenuButton.StyleClassLabelTopButton)
                .Prop(Label.StylePropertyFont, mono11),
            new StyleRule(new SelectorChild(
                    new SelectorElement(typeof(Button), null, "mainMenu", null),
                    new SelectorElement(typeof(Label), null, null, null)),
                new[] { new StyleProperty(Label.StylePropertyFont, display18) }),
            new StyleRule(new SelectorElement(typeof(BoxContainer), null, "mainMenuVBox", null),
                new[] { new StyleProperty(BoxContainer.StylePropertySeparation, 4) }),
            new StyleRule(new SelectorChild(
                    new SelectorElement(typeof(Button), null, null, new[] { ContainerButton.StylePseudoClassDisabled }),
                    new SelectorElement(typeof(Label), null, null, null)),
                new[] { new StyleProperty(Label.StylePropertyFontColor, TextDisabled) }),
            Element<LineEdit>().Class(LineEdit.StyleClassLineEditNotEditable)
                .Prop(Label.StylePropertyFontColor, TextMuted),
            Element<LineEdit>().Pseudo(LineEdit.StylePseudoClassPlaceholder)
                .Prop(Label.StylePropertyFontColor, TextMuted.WithAlpha(0.7f)),
            Element<TextEdit>().Pseudo(TextEdit.StylePseudoClassPlaceholder)
                .Prop(Label.StylePropertyFontColor, TextMuted.WithAlpha(0.7f)),

            // Windows
            Element().Class(DefaultWindow.StyleClassWindowPanel)
                .Prop(PanelContainer.StylePropertyPanel, windowPanel),
            Element().Class(StyleNano.StyleClassBorderedWindowPanel)
                .Prop(PanelContainer.StylePropertyPanel, borderedPanel),
            Element().Class(StyleNano.StyleClassTransparentBorderedWindowPanel)
                .Prop(PanelContainer.StylePropertyPanel, transparentPanel),
            Element<PanelContainer>().Class(StyleNano.StyleClassHotbarPanel)
                .Prop(PanelContainer.StylePropertyPanel, hotbarPanel),
            Element<PanelContainer>().Class(DefaultWindow.StyleClassWindowHeader)
                .Prop(PanelContainer.StylePropertyPanel, header),
            Element<PanelContainer>().Class("windowHeaderAlert")
                .Prop(PanelContainer.StylePropertyPanel, headerAlert),
            Element<PanelContainer>().Class("WindowHeadingBackground")
                .Prop(PanelContainer.StylePropertyPanel, header)
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            Element<PanelContainer>().Class("WindowHeadingBackgroundLight")
                .Prop(PanelContainer.StylePropertyPanel, header),
            Element<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton)
                .Prop(Control.StylePropertyModulateSelf, EdgeLight),
            Element<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).Pseudo(TextureButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, Danger),
            Element<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).Pseudo(TextureButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ButtonDangerDefault),
            Element<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton)
                .Prop(Control.StylePropertyModulateSelf, EdgeLight),
            Element<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, Accent),
            Element<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, AccentDim),
            Element<TextureButton>().Class(WolfgatePopoutButton.StyleClassWindowPopoutButton)
                .Prop(Control.StylePropertyModulateSelf, EdgeLight),
            Element<TextureButton>().Class(WolfgatePopoutButton.StyleClassWindowPopoutButton).Pseudo(TextureButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, Accent),
            Element<TextureButton>().Class(WolfgatePopoutButton.StyleClassWindowPopoutButton).Pseudo(TextureButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, AccentDim),
            Element<PanelContainer>().Class(StyleBase.ClassLowDivider)
                .Prop(PanelContainer.StylePropertyPanel, lowDivider),
            Element<PanelContainer>().Class(StyleBase.ClassHighDivider)
                .Prop(PanelContainer.StylePropertyPanel, highDivider),

            // Panels
            Element<PanelContainer>().Class(StyleBase.ClassAngleRect)
                .Prop(PanelContainer.StylePropertyPanel, windowPanel)
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            Element<PanelContainer>().Class("BackgroundOpenRight")
                .Prop(PanelContainer.StylePropertyPanel, panelOpenRight)
                .Prop(Control.StylePropertyModulateSelf, Edge),
            Element<PanelContainer>().Class("BackgroundOpenLeft")
                .Prop(PanelContainer.StylePropertyPanel, panelOpenLeft)
                .Prop(Control.StylePropertyModulateSelf, Edge),
            Element<PanelContainer>().Class(StyleNano.StyleClassBackgroundBaseDark)
                .Prop(PanelContainer.StylePropertyPanel, panel)
                .Prop(Control.StylePropertyModulateSelf, Edge),
            Element<PanelContainer>().Class(StyleNano.StyleClassBackgroundBaseLight)
                .Prop(PanelContainer.StylePropertyPanel, panel)
                .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#34485E")),
            Element<PanelContainer>().Class("PanelBackgroundLight")
                .Prop(PanelContainer.StylePropertyPanel, panel)
                .Prop(Control.StylePropertyModulateSelf, EdgeLight),
            Element<PanelContainer>().Class("BackgroundDark")
                .Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat(Glass)),
            Element<PanelContainer>().Class("PdaContentBackground")
                .Prop(PanelContainer.StylePropertyPanel, panel)
                .Prop(Control.StylePropertyModulateSelf, Edge),
            Element<PanelContainer>().Class("PdaBackground")
                .Prop(PanelContainer.StylePropertyPanel, panel)
                .Prop(Control.StylePropertyModulateSelf, Ink),
            Element<PanelContainer>().Class("PdaBackgroundRect")
                .Prop(PanelContainer.StylePropertyPanel, panel)
                .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#6E8CAA")),
            Element().Class(StyleNano.StyleClassInventorySlotBackground)
                .Prop(PanelContainer.StylePropertyPanel, invSlot),
            Element().Class(StyleNano.StyleClassHandSlotHighlight)
                .Prop(PanelContainer.StylePropertyPanel, handHighlight),
            new StyleRule(new SelectorChild(
                    SelectorElement.Type(typeof(NanoHeading)),
                    SelectorElement.Type(typeof(PanelContainer))),
                new[] { new StyleProperty(PanelContainer.StylePropertyPanel, heading) }),
            new StyleRule(SelectorElement.Type(typeof(StripeBack)),
                new[] { new StyleProperty(StripeBack.StylePropertyBackground, stripe) }),

            // Buttons
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton)
                .Prop(ContainerButton.StylePropertyStyleBox, button),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonOpenRight)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonOpenRight),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonOpenRightSelected)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonOpenRight)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodDefault),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonOpenLeft)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonOpenLeft),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonOpenBoth)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonOpenBoth),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonSquare)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonOpenBoth),
            Element<Button>().Class(StyleBase.ButtonOpenRightSelected)
                .Prop(ContainerButton.StylePropertyStyleBox, selectedOpenRight),
            Element<Button>().Class(StyleBase.ButtonOpenBothSelected)
                .Prop(ContainerButton.StylePropertyStyleBox, selectedOpenBoth),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonDefault),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonHovered),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ButtonPressed),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, ButtonDisabled),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonCaution).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionDefault),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonCaution).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionHovered),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonCaution).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionPressed),
            Element<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleBase.ButtonCaution).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionDisabled),
            Element<ConfirmButton>().Pseudo(ConfirmButton.ConfirmPrefix + ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionDefault),
            Element<ConfirmButton>().Pseudo(ConfirmButton.ConfirmPrefix + ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionHovered),
            Element<ConfirmButton>().Pseudo(ConfirmButton.ConfirmPrefix + ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionPressed),
            Element<ConfirmButton>().Pseudo(ConfirmButton.ConfirmPrefix + ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionDisabled),
            Element<Button>().Class(StyleNano.StyleClassButtonColorRed)
                .Prop(Control.StylePropertyModulateSelf, ButtonDangerDefault),
            Element<Button>().Class(StyleNano.StyleClassButtonColorRed).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonDangerDefault),
            Element<Button>().Class(StyleNano.StyleClassButtonColorRed).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonDangerHovered),
            Element<Button>().Class(StyleNano.StyleClassButtonColorGreen)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodDefault),
            Element<Button>().Class(StyleNano.StyleClassButtonColorGreen).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodDefault),
            Element<Button>().Class(StyleNano.StyleClassButtonColorGreen).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodHovered),
            Element<Button>().Class("ButtonAccept")
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodDefault),
            Element<Button>().Class("ButtonAccept").Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodDefault),
            Element<Button>().Class("ButtonAccept").Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodHovered),
            Element<Button>().Class("ButtonAccept").Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, ButtonGoodDisabled),
            Element<Button>().Class("ButtonSmall")
                .Prop(ContainerButton.StylePropertyStyleBox, buttonSmall),
            Element<ContainerButton>().Class(StyleNano.StyleClassStorageButton)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonStorage),
            Element<ContainerButton>().Class(StyleNano.StyleClassStorageButton).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonDefault),
            Element<ContainerButton>().Class(StyleNano.StyleClassStorageButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonHovered),
            Element<ContainerButton>().Class(StyleNano.StyleClassStorageButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ButtonPressed),
            Element<ContainerButton>().Class(StyleNano.StyleClassStorageButton).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, ButtonDisabled),
            Element<ContainerButton>().Class(ListContainer.StyleClassListContainerButton).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, GlassRaised),
            Element<ContainerButton>().Class(ListContainer.StyleClassListContainerButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, Selection),
            Element<ContainerButton>().Class(ListContainer.StyleClassListContainerButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, Selection),
            Element<ContainerButton>().Class(ListContainer.StyleClassListContainerButton).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, Ink),

            // Option buttons
            Element<OptionButton>()
                .Prop(ContainerButton.StylePropertyStyleBox, button),
            Element<OptionButton>().Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonDefault),
            Element<OptionButton>().Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonHovered),
            Element<OptionButton>().Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ButtonPressed),
            Element<OptionButton>().Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, ButtonDisabled),
            Element<TextureRect>().Class(OptionButton.StyleClassOptionTriangle)
                .Prop(Control.StylePropertyModulateSelf, Accent),
            Element<PanelContainer>().Class(OptionButton.StyleClassOptionsBackground)
                .Prop(PanelContainer.StylePropertyPanel, menuPanel),

            // Top bar
            new StyleRule(new SelectorElement(typeof(MenuButton), new[] { StyleBase.ButtonSquare }, null, null),
                new[] { new StyleProperty(Button.StylePropertyStyleBox, topSquare) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), new[] { StyleBase.ButtonOpenLeft }, null, null),
                new[] { new StyleProperty(Button.StylePropertyStyleBox, topOpenLeft) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), new[] { StyleBase.ButtonOpenRight }, null, null),
                new[] { new StyleProperty(Button.StylePropertyStyleBox, topOpenRight) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), null, null, new[] { Button.StylePseudoClassNormal }),
                new[] { new StyleProperty(Button.StylePropertyModulateSelf, TopButtonDefault) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), null, null, new[] { Button.StylePseudoClassPressed }),
                new[] { new StyleProperty(Button.StylePropertyModulateSelf, ButtonPressed) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), null, null, new[] { Button.StylePseudoClassHover }),
                new[] { new StyleProperty(Button.StylePropertyModulateSelf, Accent) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), new[] { MenuButton.StyleClassRedTopButton }, null, new[] { Button.StylePseudoClassNormal }),
                new[] { new StyleProperty(Button.StylePropertyModulateSelf, ButtonDangerDefault) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), new[] { MenuButton.StyleClassRedTopButton }, null, new[] { Button.StylePseudoClassHover }),
                new[] { new StyleProperty(Button.StylePropertyModulateSelf, ButtonDangerHovered) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), new[] { StyleNano.StyleClassButtonColorGreen }, null, new[] { Button.StylePseudoClassNormal }),
                new[] { new StyleProperty(Button.StylePropertyModulateSelf, ButtonGoodDefault) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), new[] { StyleNano.StyleClassButtonColorGreen }, null, new[] { Button.StylePseudoClassHover }),
                new[] { new StyleProperty(Button.StylePropertyModulateSelf, ButtonGoodHovered) }),
            new StyleRule(new SelectorElement(typeof(MenuButton), new[] { StyleNano.StyleClassButtonColorGreen }, null, new[] { Button.StylePseudoClassPressed }),
                new[] { new StyleProperty(Button.StylePropertyModulateSelf, ButtonGoodHovered) }),

            // Chat
            Element<PanelContainer>().Class(StyleNano.StyleClassChatPanel)
                .Prop(PanelContainer.StylePropertyPanel, chatPanel),
            Element<Button>().Class(StyleNano.StyleClassChatChannelSelectorButton)
                .Prop(Button.StylePropertyStyleBox, pill),
            Element<Button>().Class(ChatTabBar.StyleClassChatTab)
                .Prop(Button.StylePropertyStyleBox, chatTab),
            Element<ContainerButton>().Class(StyleNano.StyleClassChatFilterOptionButton)
                .Prop(ContainerButton.StylePropertyStyleBox, pillBordered),
            Element<ContainerButton>().Class(StyleNano.StyleClassChatFilterOptionButton).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonDefault),
            Element<ContainerButton>().Class(StyleNano.StyleClassChatFilterOptionButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonHovered),
            Element<ContainerButton>().Class(StyleNano.StyleClassChatFilterOptionButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ButtonPressed),
            Element<ContainerButton>().Class(StyleNano.StyleClassChatFilterOptionButton).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, ButtonDisabled),

            // Inputs and containers
            Element<LineEdit>()
                .Prop(LineEdit.StylePropertyStyleBox, lineEdit),
            Element<LineEdit>().Class(StyleNano.StyleClassActionSearchBox)
                .Prop(LineEdit.StylePropertyStyleBox, searchBox),
            Element<TabContainer>()
                .Prop(TabContainer.StylePropertyPanelStyleBox, tabPanel)
                .Prop(TabContainer.StylePropertyTabStyleBox, tabActive)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabInactive),
            Element<ProgressBar>()
                .Prop(ProgressBar.StylePropertyBackground, progressBack)
                .Prop(ProgressBar.StylePropertyForeground, progressFore),
            Element<TextureRect>().Class(CheckBox.StyleClassCheckBox)
                .Prop(TextureRect.StylePropertyTexture, checkBoxUnchecked),
            Element<TextureRect>().Class(CheckBox.StyleClassCheckBox, CheckBox.StyleClassCheckBoxChecked)
                .Prop(TextureRect.StylePropertyTexture, checkBoxChecked),
            Element<ItemList>()
                .Prop(ItemList.StylePropertyBackground, new StyleBoxFlat(Ink))
                .Prop(ItemList.StylePropertyItemBackground, listItem)
                .Prop(ItemList.StylePropertyDisabledItemBackground, listItemDisabled)
                .Prop(ItemList.StylePropertySelectedItemBackground, listItemSelected),
            Element<ItemList>().Class("transparentItemList")
                .Prop(ItemList.StylePropertyDisabledItemBackground, listItemDisabled)
                .Prop(ItemList.StylePropertySelectedItemBackground, listItemSelected),
            Element<ItemList>().Class("transparentBackgroundItemList")
                .Prop(ItemList.StylePropertyItemBackground, listItem)
                .Prop(ItemList.StylePropertyDisabledItemBackground, listItemDisabled)
                .Prop(ItemList.StylePropertySelectedItemBackground, listItemSelected),
            Element<Tree>()
                .Prop(Tree.StylePropertyBackground, new StyleBoxFlat(Ink))
                .Prop(Tree.StylePropertyItemBoxSelected, new StyleBoxFlat { BackgroundColor = Selection, ContentMarginLeftOverride = 4 }),
            Element<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton).Class(TreeItem.StyleClassEvenRow)
                .Prop(ContainerButton.StylePropertyStyleBox, new StyleBoxFlat(GlassRaised)),
            Element<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton).Class(TreeItem.StyleClassOddRow)
                .Prop(ContainerButton.StylePropertyStyleBox, new StyleBoxFlat(Glass)),
            Element<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton).Class(TreeItem.StyleClassSelected)
                .Prop(ContainerButton.StylePropertyStyleBox, new StyleBoxFlat(Selection)),
            Element<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, new StyleBoxFlat(Selection)),
            Element<VScrollBar>()
                .Prop(ScrollBar.StylePropertyGrabber, vGrabber),
            Element<VScrollBar>().Pseudo(ScrollBar.StylePseudoClassHover)
                .Prop(ScrollBar.StylePropertyGrabber, vGrabberHover),
            Element<VScrollBar>().Pseudo(ScrollBar.StylePseudoClassGrabbed)
                .Prop(ScrollBar.StylePropertyGrabber, vGrabberGrabbed),
            Element<HScrollBar>()
                .Prop(ScrollBar.StylePropertyGrabber, hGrabber),
            Element<HScrollBar>().Pseudo(ScrollBar.StylePseudoClassHover)
                .Prop(ScrollBar.StylePropertyGrabber, hGrabberHover),
            Element<HScrollBar>().Pseudo(ScrollBar.StylePseudoClassGrabbed)
                .Prop(ScrollBar.StylePropertyGrabber, hGrabberGrabbed),

            // Sliders
            Element<Slider>()
                .Prop(Slider.StylePropertyBackground, sliderBack)
                .Prop(Slider.StylePropertyForeground, sliderFore)
                .Prop(Slider.StylePropertyGrabber, sliderGrab)
                .Prop(Slider.StylePropertyFill, sliderFill),
            Element<ColorableSlider>()
                .Prop(ColorableSlider.StylePropertyFillWhite, sliderFillWhite)
                .Prop(ColorableSlider.StylePropertyBackgroundWhite, sliderFillWhite),
            Element<Slider>().Class(StyleNano.StyleClassSliderRed)
                .Prop(Slider.StylePropertyFill, sliderFillRed),
            Element<Slider>().Class(StyleNano.StyleClassSliderGreen)
                .Prop(Slider.StylePropertyFill, sliderFillGreen),
            Element<Slider>().Class(StyleNano.StyleClassSliderBlue)
                .Prop(Slider.StylePropertyFill, sliderFillBlue),
            Element<Slider>().Class(StyleNano.StyleClassSliderWhite)
                .Prop(Slider.StylePropertyFill, sliderFillWhite),

            // Tooltips and context menus (speech bubbles keep the stock look)
            Element<Tooltip>()
                .Prop(PanelContainer.StylePropertyPanel, tooltip),
            Element<PanelContainer>().Class(StyleNano.StyleClassTooltipPanel)
                .Prop(PanelContainer.StylePropertyPanel, tooltip),
            Element<PanelContainer>().Class(ExamineSystem.StyleClassEntityTooltip)
                .Prop(PanelContainer.StylePropertyPanel, tooltip),
            Element<PanelContainer>().Class(ContextMenuPopup.StyleClassContextMenuPopup)
                .Prop(PanelContainer.StylePropertyPanel, menuPanel),
            Element<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ContextDefault),
            Element<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ContextHovered),
            Element<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ContextPressed),
            Element<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, Ink),
            Element<ContextMenuElement>().Class(ConfirmationMenuElement.StyleClassConfirmationContextMenuButton).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionDefault),
            Element<ContextMenuElement>().Class(ConfirmationMenuElement.StyleClassConfirmationContextMenuButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionHovered),
            Element<ContextMenuElement>().Class(ConfirmationMenuElement.StyleClassConfirmationContextMenuButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ButtonCautionPressed),
            Element<ExamineButton>().Class(ExamineButton.StyleClassExamineButton).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, ContextHovered),
            Element<ExamineButton>().Class(ExamineButton.StyleClassExamineButton).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, ContextPressed),
        };

        return rules.Concat(RoundSetupRules()).Concat(LobbyRules()).Concat(CreatorRules()).Concat(LoadoutRules()).ToArray();
    }
}
