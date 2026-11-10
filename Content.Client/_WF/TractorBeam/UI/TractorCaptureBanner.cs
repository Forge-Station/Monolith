using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.TractorBeam.UI;

/// <summary>
/// A capture warning shown in every shuttle-console mode, independent of the radar view. Other warnings about the
/// ship can add a row of their own, and show wherever this does.
/// </summary>
public sealed class TractorCaptureBanner : BoxContainer
{
    private readonly PanelContainer _capture;
    private readonly RichTextLabel _message;

    public TractorCaptureBanner()
    {
        Visible = false;
        HorizontalExpand = true;
        Orientation = LayoutOrientation.Vertical;
        AddChild(_capture = new PanelContainer
        {
            Visible = false,
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#40250d"),
                BorderColor = Color.FromHex("#d99028"),
                BorderThickness = new Thickness(1),
            },
        });
        _capture.AddChild(_message = new RichTextLabel { Margin = new Thickness(10, 7), HorizontalExpand = true });
    }

    /// <summary>Shows the banner while any of its rows has something to say.</summary>
    public void UpdateVisibility()
    {
        Visible = Children.Any(child => child.Visible);
    }

    public void SetSources(string[] sources)
    {
        _capture.Visible = sources.Length > 0;
        UpdateVisibility();
        // SetMessage(string) treats ship names as literal text, including brackets or markup.
        _message.SetMessage(sources.Length == 0 ? string.Empty : Loc.GetString("tractor-capture-pilot-warning",
            ("ships", string.Join(", ", sources))), Color.FromHex("#ffbf66"));
    }
}
