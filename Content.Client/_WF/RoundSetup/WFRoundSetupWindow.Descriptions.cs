using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private readonly List<Action> _descriptionChecks = new();

    /// <summary>Preserves multiline drafts and explains validation without clipping or discarding text.</summary>
    private BoxContainer DescriptionEditor(string initial, Action<string> changed)
    {
        var content = Column(4);
        content.AddChild(Heading("crew-description"));
        var editor = new TextEdit
        {
            Name = "RoundSetupDescription", TextRope = new Rope.Leaf(initial), SetHeight = 84,
            HorizontalExpand = true, Placeholder = new Rope.Leaf(Text("description-placeholder")),
        };
        var counter = PlainDescription(string.Empty);
        void UpdateCounter(string value)
        {
            var valid = WFRoundSetupPolicy.TryNormalizeDescription(value, out _);
            counter.SetMessage(FormattedMessage.FromUnformatted(Text(valid ? "description-count" : "error-description",
                ("count", value.Length), ("max", WFRoundSetupPolicy.MaximumDescriptionLength))),
                valid ? Color.LightGray : Color.OrangeRed);
        }
        editor.OnTextChanged += args =>
        {
            var value = Rope.Collapse(args.TextRope);
            changed(value);
            UpdateCounter(value);
            foreach (var check in _descriptionChecks)
                check();
        };
        UpdateCounter(initial);
        content.AddChild(editor);
        content.AddChild(counter);
        return content;
    }

    private void CheckDescription(Button button, Func<string> description)
    {
        var disabled = button.Disabled;
        var tooltip = button.ToolTip;
        void Update()
        {
            var valid = WFRoundSetupPolicy.TryNormalizeDescription(description(), out _);
            button.Disabled = disabled || !valid;
            button.ToolTip = valid ? tooltip : Text("error-description", ("max", WFRoundSetupPolicy.MaximumDescriptionLength));
        }
        _descriptionChecks.Add(Update);
        Update();
    }
}
