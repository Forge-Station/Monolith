using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Forge.Botany.UI;

public sealed class BotanyUiStyles
{
    private readonly IResourceCache _resCache;

    public BotanyUiStyles(IResourceCache resCache)
    {
        _resCache = resCache;
    }

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

    public static StyleBoxFlat Panel(Color background, Color border, int thickness = 2)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(thickness)
        };
    }

    public static void ApplyPanel(PanelContainer panel, Color background, Color border, int thickness = 2)
    {
        panel.PanelOverride = Panel(background, border, thickness);
        panel.ModulateSelfOverride = Color.White;
    }

    public Label Heading(string text, int size = 16)
    {
        return new Label
        {
            Text = text,
            FontOverride = GetBoldFont(size),
            FontColorOverride = BotanyUiTheme.Heading
        };
    }

    public Label Body(string text, int size = 12, Color? color = null)
    {
        return new Label
        {
            Text = text,
            FontOverride = GetRegularFont(size),
            FontColorOverride = color ?? BotanyUiTheme.Text
        };
    }

    public Label Sub(string text, int size = 11)
    {
        return Body(text, size, BotanyUiTheme.SubText);
    }

    public Control KeyValue(string key, string value, Color? valueColor = null)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 1, 0, 1)
        };

        row.AddChild(new Label
        {
            Text = key,
            MinWidth = 180,
            FontOverride = GetRegularFont(12),
            FontColorOverride = BotanyUiTheme.SubText
        });

        row.AddChild(new Label
        {
            Text = value,
            HorizontalExpand = true,
            FontOverride = GetBoldFont(12),
            FontColorOverride = valueColor ?? BotanyUiTheme.Text
        });

        return row;
    }

    public Label Section(string text)
    {
        return new Label
        {
            Text = text,
            FontOverride = GetBoldFont(13),
            FontColorOverride = BotanyUiTheme.Heading,
            Margin = new Thickness(0, 10, 0, 4)
        };
    }

    public BotanyChipButton Chip(string text, bool selected)
    {
        var button = new BotanyChipButton(this, text, selected)
        {
            ToggleMode = true,
            Margin = new Thickness(0, 0, 6, 0)
        };
        return button;
    }

    public BotanyChipButton Action(string text)
    {
        return new BotanyChipButton(this, text);
    }
}
