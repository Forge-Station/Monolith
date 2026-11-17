using Content.Client._Wega.Stylesheets;
using Content.Shared._Forge.Company;
using Content.Shared.SecApartment;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Company;

/// <summary>
/// Company PDA cartridge chrome — SecApartment-style panels driven by a selectable palette.
/// </summary>
public sealed class CompanyManagementStyles
{
    public const string DefaultPaletteId = "CompanyUiGrey";

    private readonly SecApartmentStyles _inner;
    private readonly IUserInterfaceManager _ui;
    private CompanyUiPalettePrototype _palette;

    public CompanyUiPalettePrototype Theme => _palette;
    public SecApartmentStyles Inner => _inner;

    public CompanyManagementStyles(
        IResourceCache resCache,
        IUserInterfaceManager ui,
        CompanyUiPalettePrototype palette)
    {
        _ui = ui;
        _inner = new SecApartmentStyles(resCache);
        _palette = palette;
        ApplyInnerTheme();
    }

    public void SetPalette(CompanyUiPalettePrototype palette)
    {
        _palette = palette;
        ApplyInnerTheme();
    }

    private void ApplyInnerTheme()
    {
        // Reuse SecApartment stylesheet helpers with a mirrored theme object.
        _inner.SetTheme(ToSecTheme(_palette));
    }

    private static SecApUiThemePrototype ToSecTheme(CompanyUiPalettePrototype p)
    {
        return new SecApUiThemePrototype
        {
            ContentBackground = p.ContentBackground,
            ContentBorder = p.ContentBorder,
            Divider = p.Divider,
            ListPanelBackground = p.ListPanelBackground,
            ListPanelBorder = p.ListPanelBorder,
            CreateSquadPanelBackground = p.CreateSquadPanelBackground,
            CreateSquadPanelBorder = p.CreateSquadPanelBorder,
            CardBackground = p.CardBackground,
            CardBorder = p.CardBorder,
            EntryBackground = p.EntryBackground,
            EntryBorder = p.EntryBorder,
            MemberAliveBackground = p.MemberAliveBackground,
            MemberAliveBorder = p.MemberAliveBorder,
            TabActive = p.TabActive,
            TabInactive = p.TabInactive,
            Heading = p.Heading,
            Text = p.Text,
            SubText = p.SubText,
            Placeholder = p.Placeholder,
            ButtonBackground = p.ButtonBackground,
            ButtonBorder = p.ButtonBorder,
            LineEditBackground = p.LineEditBackground,
            OptionBackground = p.OptionBackground,
            TabActiveBackground = p.TabActiveBackground,
            TabInactiveBackground = p.TabInactiveBackground,
            PanelBackground = p.PanelBackground,
        };
    }

    public Stylesheet CreateFragmentStylesheet()
    {
        var theme = _inner.Theme;
        var rules = new List<StyleRule>();
        if (_ui.Stylesheet?.Rules != null)
            rules.AddRange(_ui.Stylesheet.Rules);

        rules.Add(_inner.CreateButtonRedRule(
            _inner.GetButtonRedStyle(),
            _inner.GetBoldFont(11),
            theme.TabActive,
            theme.TabInactive));

        rules.Add(_inner.CreateLineEditRule(
            _inner.GetLineEditStyle(),
            _inner.GetRegularFont(11),
            theme.Text,
            theme.Placeholder));

        rules.Add(_inner.CreateOptionButtonRule(
            _inner.GetOptionButtonStyle(),
            _inner.GetRegularFont(11),
            theme.Text,
            theme.TabInactive));

        rules.Add(_inner.CreateTabContainerRule(
            _inner.GetTabActiveStyle(),
            _inner.GetTabInactiveStyle(),
            _inner.GetPanelStyle(),
            _inner.GetBoldFont(11),
            theme.TabActive,
            theme.TabInactive));

        return new Stylesheet(rules);
    }

    public StyleBoxFlat MakePanelBox(Color background, Color border, int borderThickness = 2, Thickness? margin = null)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(borderThickness),
        };

        var m = margin ?? new Thickness(6);
        box.ContentMarginLeftOverride = m.Left;
        box.ContentMarginRightOverride = m.Right;
        box.ContentMarginTopOverride = m.Top;
        box.ContentMarginBottomOverride = m.Bottom;
        return box;
    }

    public PanelContainer MakeListPanel(Control content, bool verticalExpand = true)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = verticalExpand,
            PanelOverride = MakePanelBox(Theme.ListPanelBackground, Theme.ListPanelBorder),
        };
        panel.AddChild(content);
        return panel;
    }

    public PanelContainer MakeActionPanel(Control content)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 8),
            PanelOverride = MakePanelBox(Theme.CreateSquadPanelBackground, Theme.CreateSquadPanelBorder),
        };
        panel.AddChild(content);
        return panel;
    }

    public PanelContainer MakeCard(Control content)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 6),
            PanelOverride = MakePanelBox(Theme.CardBackground, Theme.CardBorder, 2, new Thickness(8, 6, 8, 6)),
        };
        panel.AddChild(content);
        return panel;
    }

    public PanelContainer MakeEntry(Control content)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 4),
            PanelOverride = MakePanelBox(Theme.EntryBackground, Theme.EntryBorder, 1, new Thickness(6, 4, 6, 4)),
        };
        panel.AddChild(content);
        return panel;
    }

    public PanelContainer MakeDivider()
    {
        return new PanelContainer
        {
            HorizontalExpand = true,
            SetHeight = 2,
            Margin = new Thickness(0, 4, 0, 4),
            PanelOverride = MakePanelBox(Theme.Divider, Theme.Divider, 0, new Thickness(0)),
        };
    }

    public Label MakeHeading(string text)
    {
        return new Label
        {
            Text = text,
            FontColorOverride = Theme.Heading,
            StyleClasses = { "LabelHeading" },
        };
    }

    public Label MakeSubHeading(string text)
    {
        return new Label
        {
            Text = text,
            FontColorOverride = Theme.SubText,
        };
    }

    public Label MakeText(string text, bool expand = false)
    {
        return new Label
        {
            Text = text,
            FontColorOverride = Theme.Text,
            HorizontalExpand = expand,
            ClipText = true,
        };
    }

    public Button MakeButton(string text)
    {
        var btn = new Button { Text = text };
        btn.AddStyleClass(SecApartmentStyles.StyleClassButtonRed);
        return btn;
    }

    public LineEdit MakeLineEdit(string? placeholder = null, string? text = null)
    {
        var edit = new LineEdit
        {
            PlaceHolder = placeholder ?? string.Empty,
            Text = text ?? string.Empty,
            HorizontalExpand = true,
        };
        edit.AddStyleClass(SecApartmentStyles.StyleClassConsoleLineEdit);
        return edit;
    }

    public void StyleOption(OptionButton option)
    {
        option.AddStyleClass(SecApartmentStyles.StyleClassOptionButton);
    }

    public void StyleCheckBox(CheckBox box)
    {
        box.ModulateSelfOverride = Theme.Text;
    }

    public void TintLabel(Label label, bool heading = false)
    {
        label.FontColorOverride = heading ? Theme.Heading : Theme.Text;
    }
}
