using System.Numerics;

namespace Content.Client.UserInterface.Controls;

public partial class MapGridControl
{
    /// <summary>Fits the radar and its input transform to a resizable console instrument viewport.</summary>
    public bool WfFitInstrument { get; set; }

    /// <summary>Visible world extents at the current plotting scale, independent of sensor and IFF range.</summary>
    private Vector2 WfViewportHalfExtents => MinimapScale > 0f
        ? new Vector2(PixelWidth, PixelHeight) / (2f * MinimapScale)
        : Vector2.Zero;
}
