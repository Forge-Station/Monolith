// Forge-Change
using Content.Client.Resources;
using Content.Shared.SecApartment;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Wega.Stylesheets;

/// <summary>
/// Строит stylesheet-правила из <see cref="SecApUiThemePrototype"/>. Цвета не дублируются — только YAML.
/// </summary>
public sealed class SecApartmentStyles
{
    private readonly IResourceCache _resCache;
    private SecApUiThemePrototype _theme = new();

    public SecApUiThemePrototype Theme => _theme;

    public const string StyleClassButtonRed = "ButtonRed";
    public const string StyleClassConsoleLineEdit = "ConsoleLineEdit";
    public const string StyleClassConsoleHeading = "ConsoleHeading";
    public const string StyleClassOptionButton = "SecApartmentOptionButton";

    public SecApartmentStyles(IResourceCache resCache)
    {
        _resCache = resCache;
    }

    // Forge-Change-start
    public void SetTheme(SecApUiThemePrototype theme)
    {
        _theme = theme;
    }
    // Forge-Change-end

    private StyleBoxFlat CreateStyleBox(Color backgroundColor, Color borderColor,
        Thickness borderThickness, Thickness? contentMargin = null)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = backgroundColor,
            BorderColor = borderColor,
            BorderThickness = borderThickness
        };

        if (contentMargin.HasValue)
        {
            style.ContentMarginLeftOverride = contentMargin.Value.Left;
            style.ContentMarginRightOverride = contentMargin.Value.Right;
            style.ContentMarginTopOverride = contentMargin.Value.Top;
            style.ContentMarginBottomOverride = contentMargin.Value.Bottom;
        }

        return style;
    }

    public StyleBox GetTabActiveStyle() => CreateStyleBox(
        _theme.TabActiveBackground,
        _theme.TabActive,
        new Thickness(2, 2, 2, 0),
        new Thickness(10, 5, 10, 5)
    );

    public StyleBox GetTabInactiveStyle() => CreateStyleBox(
        _theme.TabInactiveBackground,
        _theme.TabInactive,
        new Thickness(2, 2, 2, 0),
        new Thickness(10, 5, 10, 5)
    );

    public StyleBox GetPanelStyle() => CreateStyleBox(
        _theme.PanelBackground,
        _theme.TabActive,
        new Thickness(2),
        new Thickness(5, 5, 5, 5)
    );

    public StyleBox GetButtonRedStyle() => CreateStyleBox(
        _theme.ButtonBackground,
        _theme.ButtonBorder,
        new Thickness(1),
        new Thickness(8, 4, 8, 4)
    );

    public StyleBox GetLineEditStyle() => CreateStyleBox(
        _theme.LineEditBackground,
        _theme.TabActive,
        new Thickness(1),
        new Thickness(4, 2, 4, 2)
    );

    public Font GetBoldFont(int size = 12) => _resCache.GetFont(new[]
    {
        "/Fonts/NotoSans/NotoSans-Bold.ttf",
        "/Fonts/NotoSans/NotoSansSymbols-Regular.ttf",
        "/Fonts/NotoSans/NotoSansSymbols2-Regular.ttf"
    }, size);

    public Font GetRegularFont(int size = 12) => _resCache.GetFont(new[]
    {
        "/Fonts/NotoSans/NotoSans-Regular.ttf",
        "/Fonts/NotoSans/NotoSansSymbols-Regular.ttf",
        "/Fonts/NotoSans/NotoSansSymbols2-Regular.ttf"
    }, size);

    public StyleRule CreateButtonRedRule(StyleBox buttonRedStyle, Font font, Color fontColor, Color disabledColor)
    {
        return new StyleRule(
            new SelectorElement(typeof(Button), new[] { StyleClassButtonRed }, null, null),
            new[]
            {
                new StyleProperty("stylebox", buttonRedStyle),
                new StyleProperty("font-color", fontColor),
                new StyleProperty("font", font),
                new StyleProperty("font-color-disabled", disabledColor)
            }
        );
    }

    public StyleRule CreateLineEditRule(StyleBox lineEditStyle, Font font, Color textColor, Color placeholderColor)
    {
        return new StyleRule(
            new SelectorElement(typeof(LineEdit), new[] { StyleClassConsoleLineEdit }, null, null),
            new[]
            {
                new StyleProperty("stylebox", lineEditStyle),
                new StyleProperty("font-color", textColor),
                new StyleProperty("font", font),
                new StyleProperty("placeholder-color", placeholderColor),
                new StyleProperty("cursor-color", _theme.TabActive),
                new StyleProperty("selection-color", _theme.TabActive.WithAlpha(0.3f))
            }
        );
    }

    public StyleBox GetOptionButtonStyle() => CreateStyleBox(
        _theme.OptionBackground,
        _theme.TabActive,
        new Thickness(1),
        new Thickness(6, 3, 6, 3)
    );

    public StyleRule CreateOptionButtonRule(StyleBox optionStyle, Font font, Color fontColor, Color disabledColor)
    {
        return new StyleRule(
            new SelectorElement(typeof(OptionButton), new[] { StyleClassOptionButton }, null, null),
            new[]
            {
                new StyleProperty(ContainerButton.StylePropertyStyleBox, optionStyle),
                new StyleProperty("font", font),
                new StyleProperty("font-color", fontColor),
                new StyleProperty("font-color-disabled", disabledColor)
            }
        );
    }

    public StyleRule CreateTabContainerRule(StyleBox tabActiveStyle, StyleBox tabInactiveStyle,
        StyleBox panelStyle, Font font, Color activeColor, Color inactiveColor)
    {
        return new StyleRule(
            new SelectorElement(typeof(TabContainer), null, null, null),
            new[]
            {
                new StyleProperty("tab-stylebox", tabActiveStyle),
                new StyleProperty("tab-stylebox-inactive", tabInactiveStyle),
                new StyleProperty("panel-stylebox", panelStyle),
                new StyleProperty("tab-font-color", activeColor),
                new StyleProperty("tab-font-color-inactive", inactiveColor),
                new StyleProperty("font", font)
            }
        );
    }
}
