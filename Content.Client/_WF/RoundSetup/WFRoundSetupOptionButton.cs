using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

/// <summary>Wraps both the selected value and popup options within the available width.</summary>
public sealed class WFRoundSetupOptionButton : OptionButton
{
    private readonly Label _source;
    private readonly RichTextLabel _caption;
    private string? _shown;
    private readonly List<Button> _choices = new();

    /// <summary>Keeps the dropdown indicator separate from the wrapping selection caption.</summary>
    public WFRoundSetupOptionButton()
    {
        HorizontalExpand = true;
        MinHeight = 40;
        var row = Children.OfType<BoxContainer>().Single();
        _source = row.Children.OfType<Label>().Single();
        _source.Visible = false;
        var triangle = row.Children.OfType<TextureRect>().Single();
        triangle.Orphan();
        triangle.HorizontalAlignment = HAlignment.Right;
        _caption = new RichTextLabel
        {
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
            Margin = new Thickness(0, 0, 24, 0),
        };
        row.AddChild(_caption);
        AddChild(triangle);
        OptionsScroll.HScrollEnabled = false;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        UpdateCaption();
        OptionsScroll.MaxWidth = Math.Max(160, availableSize.X);
        return base.MeasureOverride(availableSize);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        // The hidden source label does not invalidate its parent's layout when selection changes.
        UpdateCaption();
    }

    private void UpdateCaption()
    {
        if (_shown == _source.Text)
            return;
        _shown = _source.Text;
        _caption.SetMessage(FormattedMessage.FromUnformatted((_shown ?? string.Empty).Trim()));
    }

    /// <summary>Removes every choice so the list can be rebuilt.</summary>
    public void ClearChoices()
    {
        Clear();
        _choices.Clear();
    }

    /// <summary>Explains why a popup choice cannot currently be selected.</summary>
    public void DisableItem(int index, string reason)
    {
        SetItemDisabled(index, true);
        _choices[index].ToolTip = reason;
    }

    /// <summary>Uses wrapping text for each searchable popup choice.</summary>
    public override void ButtonOverride(Button button)
    {
        _choices.Add(button);
        button.Label.Visible = false;
        var caption = new RichTextLabel { HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        caption.SetMessage(FormattedMessage.FromUnformatted(button.Text ?? string.Empty));
        button.AddChild(caption);
        button.MinHeight = 36;
    }
}
