using System.Globalization;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.SectorControl;

/// <summary>The sector map over a bar with "Centre on me", "Whole sector" and the scale, as the sector map window shows it.</summary>
public sealed class WFSectorMapPanel : BoxContainer
{
    private static readonly Color ScaleColor = Color.FromHex("#8a96a3");

    public readonly WFSectorMapControl MapControl;
    public readonly Button CentreButton;
    public readonly Button WholeSectorButton;
    public readonly Label ScaleLabel;

    private float _shownRange = -1f;

    public WFSectorMapPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        MapControl = new WFSectorMapControl { HorizontalExpand = true, VerticalExpand = true };
        CentreButton = new Button { Text = Loc.GetString("wf-sector-map-centre-on-me") };
        CentreButton.OnPressed += _ => MapControl.CentreOnPlayer();
        WholeSectorButton = new Button { Text = Loc.GetString("wf-sector-map-whole-sector") };
        WholeSectorButton.OnPressed += _ => MapControl.ShowWholeSector();
        ScaleLabel = new Label
        {
            HorizontalExpand = true,
            Align = Label.AlignMode.Right,
            FontColorOverride = ScaleColor,
        };

        var bar = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Margin = new Thickness(0, 4, 0, 0),
        };
        bar.AddChild(CentreButton);
        bar.AddChild(WholeSectorButton);
        bar.AddChild(ScaleLabel);

        AddChild(MapControl);
        AddChild(bar);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (MathF.Abs(_shownRange - MapControl.Range) < 0.5f)
            return;

        _shownRange = MapControl.Range;
        var width = _shownRange * 2f / 1000f;
        var text = width < 10f
            ? width.ToString("0.0", CultureInfo.InvariantCulture)
            : width.ToString("0", CultureInfo.InvariantCulture);
        ScaleLabel.Text = Loc.GetString("wf-sector-map-scale", ("width", text));
    }
}
