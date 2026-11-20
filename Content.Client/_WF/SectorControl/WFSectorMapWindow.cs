using System.Numerics;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.SectorControl;

/// <summary>The sector map in a window of its own, opened from the game menu or with wf_sectormap.</summary>
public sealed class WFSectorMapWindow : DefaultWindow
{
    /// <summary>Gap kept between the window and each edge of the viewport when it has to shrink.</summary>
    private const float ViewportMargin = 20f;

    public readonly WFSectorMapPanel Panel;

    public WFSectorMapWindow()
    {
        Title = Loc.GetString("wf-sector-map-window-title");
        Resizable = true;
        SetSize = new Vector2(900, 700);
        MinSize = new Vector2(400, 300);
        Panel = new WFSectorMapPanel();
        Contents.AddChild(Panel);
    }

    /// <summary>Shrinks the window to fit a viewport, keeping its design size when there is room.</summary>
    public void FitTo(Vector2 viewport)
    {
        var fitted = Vector2.Min(SetSize, viewport - new Vector2(ViewportMargin * 2));
        SetSize = Vector2.Max(fitted, MinSize);
    }
}
