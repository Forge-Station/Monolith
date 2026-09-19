using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Shuttles.UI;

/// <summary>A capture warning shown in every shuttle-console mode, independent of the radar view.</summary>
public sealed class TractorCaptureBanner : PanelContainer
{
    private readonly RichTextLabel _message;

    public TractorCaptureBanner()
    {
        Visible = false;
        HorizontalExpand = true;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#40250d"),
            BorderColor = Color.FromHex("#d99028"),
            BorderThickness = new Thickness(1),
        };
        AddChild(_message = new RichTextLabel { Margin = new Thickness(10, 7), HorizontalExpand = true });
    }

    public void SetSources(string[] sources)
    {
        Visible = sources.Length > 0;
        // SetMessage(string) treats ship names as literal text, including brackets or markup.
        _message.SetMessage(sources.Length == 0 ? string.Empty : Loc.GetString("tractor-capture-pilot-warning",
            ("ships", string.Join(", ", sources))), Color.FromHex("#ffbf66"));
    }
}
