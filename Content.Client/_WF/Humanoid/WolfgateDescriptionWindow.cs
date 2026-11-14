using System.Numerics;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Full-size editor for the character description, opened from the small box under the preview so long
/// text is workable without the creator's preview column having to grow.
/// </summary>
public sealed class WolfgateDescriptionWindow : DefaultWindow
{
    private readonly TextEdit _edit;
    private readonly Label _counter;
    private readonly Label _warning;

    /// <summary>Raised on every keystroke, so the small box and the profile stay in step while typing.</summary>
    public Action<string>? OnTextChanged;

    public WolfgateDescriptionWindow(string text, string placeholder)
    {
        Title = Loc.GetString("humanoid-profile-editor-flavortext-tab");
        MinSize = new Vector2(620, 460);

        _edit = new TextEdit
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Placeholder = new Rope.Leaf(placeholder),
            TextRope = new Rope.Leaf(text),
        };
        _edit.OnTextChanged += _ =>
        {
            OnTextChanged?.Invoke(Text);
            UpdateStatus();
        };

        _warning = new Label { StyleClasses = { "Danger" }, VAlign = Label.VAlignMode.Center, Visible = false };
        _counter = new Label { StyleClasses = { "CreatorFieldLabel" }, VAlign = Label.VAlignMode.Center };

        var status = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Children = { _warning, new Control { HorizontalExpand = true }, _counter },
        };

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8),
            Children = { _edit, status },
        });

        UpdateStatus();
    }

    /// <summary>Same live length and square bracket note as the small box under the preview.</summary>
    private void UpdateStatus()
    {
        var content = Text;
        var max = HumanoidCharacterProfile.MaxDescLength;
        _counter.Text = Loc.GetString("wf-creator-description-counter", ("count", content.Length), ("max", max));

        var tooLong = content.Length > max;
        if (tooLong)
            _counter.StyleClasses.Add("Danger");
        else
            _counter.StyleClasses.Remove("Danger");

        var brackets = content.Contains('[') || content.Contains(']');
        _warning.Visible = brackets || tooLong;
        if (!_warning.Visible)
            return;

        _warning.Text = Loc.GetString(tooLong ? "wf-creator-description-too-long" : "wf-creator-description-brackets");
        _warning.ToolTip = Loc.GetString(
            tooLong ? "wf-creator-description-too-long-tooltip" : "wf-creator-description-brackets-tooltip",
            ("max", max));
    }

    public string Text => Rope.Collapse(_edit.TextRope);
}
