using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Forge.Botany.UI;

public sealed class BotanyListButton : ContainerButton
{
    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly Label _subtitle;
    private bool _selected;

    public object? Key { get; set; }

    public BotanyListButton(BotanyUiStyles styles)
    {
        HorizontalExpand = true;
        Margin = new Thickness(0, 0, 0, 4);
        ToggleMode = false;

        _panel = new PanelContainer { HorizontalExpand = true };
        _title = new Label
        {
            ClipText = true,
            HorizontalExpand = true,
            FontOverride = styles.GetBoldFont(12),
            FontColorOverride = BotanyUiTheme.Heading
        };
        _subtitle = new Label
        {
            ClipText = true,
            HorizontalExpand = true,
            FontOverride = styles.GetRegularFont(11),
            FontColorOverride = BotanyUiTheme.SubText
        };

        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8, 6, 8, 6),
            HorizontalExpand = true
        };
        column.AddChild(_title);
        column.AddChild(_subtitle);
        _panel.AddChild(column);
        AddChild(_panel);

        ApplySelected(false);
    }

    public void SetContent(string title, string subtitle, Color? subtitleColor = null)
    {
        _title.Text = title;
        _subtitle.Text = subtitle;
        _subtitle.FontColorOverride = subtitleColor ?? BotanyUiTheme.SubText;
    }

    public void ApplySelected(bool selected)
    {
        _selected = selected;
        Pressed = selected;
        BotanyUiStyles.ApplyPanel(
            _panel,
            selected ? BotanyUiTheme.EntrySelectedBackground : BotanyUiTheme.EntryBackground,
            selected ? BotanyUiTheme.EntrySelectedBorder : BotanyUiTheme.EntryBorder,
            selected ? 2 : 1);
        _title.FontColorOverride = selected ? BotanyUiTheme.Ok : BotanyUiTheme.Heading;
    }
}
