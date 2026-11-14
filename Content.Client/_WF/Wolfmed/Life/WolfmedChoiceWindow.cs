using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._WF.Wolfmed.Life;

/// <summary>A yes/no window with the exact text the server sent: the Succumb and "left alive" dialogs.</summary>
/// <remarks>
/// Sized to its text (playtest 1): the body wraps at <see cref="TextWidth"/> and nothing expands, so the window
/// is as tall as the words and the buttons and no taller. Open it after <see cref="SetChoice"/>, or it centres
/// on an empty body and grows off the bottom of the screen.
/// </remarks>
public sealed class WolfmedChoiceWindow : DefaultWindow
{
    /// <summary>Where the body text wraps, in UI units.</summary>
    public const float TextWidth = 420f;

    public readonly Button AcceptChoice;
    public readonly Button DenyChoice;
    public readonly BoxContainer ButtonRow;
    public readonly RichTextLabel Text;

    public WolfmedChoiceWindow()
    {
        MinSize = new Vector2(300, 0);
        Resizable = false;

        Contents.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 10,
            Margin = new Thickness(6, 4, 6, 6),
            Children =
            {
                (Text = new RichTextLabel { MaxWidth = TextWidth }),
                (ButtonRow = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    HorizontalAlignment = HAlignment.Center,
                    SeparationOverride = 20,
                    Children =
                    {
                        (AcceptChoice = new Button()),
                        (DenyChoice = new Button()),
                    },
                }),
            },
        });
    }

    public void SetChoice(string title, string text, string accept, string deny)
    {
        Title = title;
        Text.SetMessage(FormattedMessage.FromUnformatted(text));
        AcceptChoice.Text = accept;
        DenyChoice.Text = deny;
    }
}
