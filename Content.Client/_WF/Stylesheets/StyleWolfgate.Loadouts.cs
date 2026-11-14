using Content.Client._WF.Loadouts;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WF.Stylesheets;

public sealed partial class StyleWolfgate
{
    /// <summary>Solid panel behind the loadout rail, the tile grid and the selected panel.</summary>
    public const string StyleClassLoadoutPanel = "LoadoutPanel";

    /// <summary>Solid sunken box of the loadout detail strip.</summary>
    public const string StyleClassLoadoutInset = "LoadoutInset";

    /// <summary>Card of one pick in the loadout window's selected panel.</summary>
    public const string StyleClassLoadoutPick = "LoadoutPick";

    /// <summary>Small label for a price or a status word on a loadout tile or rail row.</summary>
    public const string StyleClassLoadoutChip = "LoadoutChip";

    /// <summary>Category heading in the loadout rail.</summary>
    public const string StyleClassLoadoutRailHeading = "LoadoutRailHeading";

    /// <summary>Link button whose label reads as a warning until hovered.</summary>
    public const string StyleClassLoadoutAttention = "LoadoutAttention";

    /// <summary>
    /// Rules for the loadout window: panels, tiles, rail rows, chips and the warning links. Every surface is a
    /// solid colour, each a step lighter than the one it sits on, so nothing shows through.
    /// </summary>
    private StyleRule[] LoadoutRules()
    {
        var panel = SolidBox(GlassRaised, EdgeSoft, 10);
        var inset = SolidBox(Ink, EdgeSoft, 4);

        // Flat and white, so the state modulate alone decides how a row reads.
        var rowBox = new StyleBoxFlat { BackgroundColor = Color.White };
        rowBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 4);

        return new StyleRule[]
        {
            Element<PanelContainer>().Class(StyleClassLoadoutPanel)
                .Prop(PanelContainer.StylePropertyPanel, panel),
            Element<PanelContainer>().Class(StyleClassLoadoutInset)
                .Prop(PanelContainer.StylePropertyPanel, inset),
            Element<PanelContainer>().Class(StyleClassLoadoutPick)
                .Prop(PanelContainer.StylePropertyPanel, SolidBox(GlassLight, Edge, 4)),

            // Tiles: raised cards, lit when the loadout is selected, sunken when it is locked
            Element<ContainerButton>().Class(LoadoutTile.StyleClassTile).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(ContainerButton.StylePropertyStyleBox, SolidBox(GlassLight, Edge, 4)),
            Element<ContainerButton>().Class(LoadoutTile.StyleClassTile).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, SolidBox(Edge, EdgeLight, 4)),
            Element<ContainerButton>().Class(LoadoutTile.StyleClassTile).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(ContainerButton.StylePropertyStyleBox, SolidBox(AccentDim, Accent, 4)),
            Element<ContainerButton>().Class(LoadoutTile.StyleClassTile).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(ContainerButton.StylePropertyStyleBox, SolidBox(Glass, EdgeSoft, 4)),

            // Rail rows: bare on the panel until hovered, lit for the open group
            Element<ContainerButton>().Class(LoadoutRailRow.StyleClassRow)
                .Prop(ContainerButton.StylePropertyStyleBox, rowBox),
            Element<ContainerButton>().Class(LoadoutRailRow.StyleClassRow).Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(Control.StylePropertyModulateSelf, GlassRaised),
            Element<ContainerButton>().Class(LoadoutRailRow.StyleClassRow).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, EdgeSoft),
            Element<ContainerButton>().Class(LoadoutRailRow.StyleClassRow).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, AccentDim),
            Element<ContainerButton>().Class(LoadoutRailRow.StyleClassRow).Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(Control.StylePropertyModulateSelf, GlassRaised),

            Element<Label>().Class(StyleClassLoadoutRailHeading)
                .Prop(Label.StylePropertyFont, Display(12))
                .Prop(Label.StylePropertyFontColor, Accent),

            // Font only: a chip's colour comes from the label tone classes
            Element<Label>().Class(StyleClassLoadoutChip)
                .Prop(Label.StylePropertyFont, _resCache.NotoStack(size: 10)),

            Child().Parent(Element<Button>().Class(StyleClassLoadoutAttention)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, Danger),
            Child().Parent(Element<Button>().Class(StyleClassLoadoutAttention).Pseudo(ContainerButton.StylePseudoClassHover)).Child(Element<Label>())
                .Prop(Label.StylePropertyFontColor, Accent),
        };
    }

    /// <summary>Opaque box with a one pixel edge and the same content margin on every side.</summary>
    private static StyleBoxFlat SolidBox(Color fill, Color edge, float margin)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = fill,
            BorderColor = edge,
            BorderThickness = new Thickness(1),
        };
        box.SetContentMarginOverride(StyleBox.Margin.All, margin);
        return box;
    }
}
