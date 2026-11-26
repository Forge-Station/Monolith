using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Forge.Botany.UI;

/// <summary>
/// Dark terminal chip/action control. Default SS14 buttons go neon when modulated.
/// </summary>
public sealed class BotanyChipButton : ContainerButton
{
    private readonly PanelContainer _panel;
    private readonly Label _label;

    public BotanyChipButton(BotanyUiStyles styles, string text, bool selected = false)
    {
        ToggleMode = false;
        MinHeight = 24;
        Margin = new Thickness(0, 0, 6, 4);

        _panel = new PanelContainer { HorizontalExpand = true };
        _label = new Label
        {
            Text = text,
            FontOverride = styles.GetBoldFont(11),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(10, 4, 10, 4)
        };

        _panel.AddChild(_label);
        AddChild(_panel);
        ApplySelected(selected);
    }

    public void SetText(string text)
    {
        _label.Text = text;
    }

    public void ApplySelected(bool selected)
    {
        Pressed = selected;
        BotanyUiStyles.ApplyPanel(
            _panel,
            selected ? BotanyUiTheme.EntrySelectedBackground : BotanyUiTheme.EntryBackground,
            selected ? BotanyUiTheme.EntrySelectedBorder : BotanyUiTheme.EntryBorder,
            selected ? 2 : 1);
        _label.FontColorOverride = selected ? BotanyUiTheme.Heading : BotanyUiTheme.Text;
    }
}
