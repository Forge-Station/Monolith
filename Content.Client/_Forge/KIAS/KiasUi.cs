using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Forge.KIAS;

public static class KiasUi
{
    public static PanelContainer Panel(Control content) => new()
    {
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#181c22"),
            BorderColor = Color.FromHex("#39414a"),
            BorderThickness = new Thickness(1)
        },
        Children = { content }
    };

    public static BoxContainer Section(BoxContainer parent, string key)
    {
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6), SeparationOverride = 4 };
        content.AddChild(new Label { Text = Loc.GetString(key), Modulate = Color.FromHex("#bacbd8"), ClipText = true });
        parent.AddChild(Panel(content));
        return content;
    }

    public static Control Field(Control editor, string key)
    {
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        box.AddChild(new Label { Text = Loc.GetString(key), ClipText = true });
        box.AddChild(editor);
        return box;
    }

    public static Control Help(string title, string description)
    {
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(4), SeparationOverride = 3 };
        box.AddChild(new Label { Text = title, ClipText = true, ToolTip = title });
        var text = new RichTextLabel(); text.SetMessage(FormattedMessage.FromUnformatted(description));
        box.AddChild(text);
        return Panel(box);
    }
}
