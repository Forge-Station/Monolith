using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using System.Numerics;
using System.Linq;

namespace Content.Client._Forge.Crypto.UI;

public sealed class PriceHistoryGraphControl : Control
{
    private IReadOnlyList<double> _points = Array.Empty<double>();

    public void SetPoints(IReadOnlyList<double> points)
    {
        _points = points;
        InvalidateMeasure();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var background = Color.FromHex("#11151d");
        var grid = Color.FromHex("#2d3542");
        var line = Color.FromHex("#4fc3f7");
        var border = Color.FromHex("#6f7a8c");

        handle.DrawRect(PixelSizeBox, background);

        var w = PixelWidth;
        var h = PixelHeight;
        if (w <= 4 || h <= 4)
            return;

        var margin = 8f;
        var graphMin = new Vector2(margin, margin);
        var graphMax = new Vector2(w - margin, h - margin);

        handle.DrawLine(new Vector2(graphMin.X, graphMin.Y), new Vector2(graphMax.X, graphMin.Y), border);
        handle.DrawLine(new Vector2(graphMax.X, graphMin.Y), new Vector2(graphMax.X, graphMax.Y), border);
        handle.DrawLine(new Vector2(graphMax.X, graphMax.Y), new Vector2(graphMin.X, graphMax.Y), border);
        handle.DrawLine(new Vector2(graphMin.X, graphMax.Y), new Vector2(graphMin.X, graphMin.Y), border);

        for (var i = 1; i < 4; i++)
        {
            var t = i / 4f;
            var y = MathHelper.Lerp(graphMin.Y, graphMax.Y, t);
            handle.DrawLine(new Vector2(graphMin.X, y), new Vector2(graphMax.X, y), grid);
        }

        if (_points.Count < 2)
            return;

        var min = _points.Min();
        var max = _points.Max();
        var range = Math.Max(1.0, max - min);

        Vector2? previous = null;
        for (var i = 0; i < _points.Count; i++)
        {
            var xT = i / (float) (_points.Count - 1);
            var yT = (float) ((_points[i] - min) / range);
            var x = MathHelper.Lerp(graphMin.X, graphMax.X, xT);
            var y = MathHelper.Lerp(graphMax.Y, graphMin.Y, yT);
            var current = new Vector2(x, y);

            if (previous != null)
                handle.DrawLine(previous.Value, current, line);

            previous = current;
        }
    }
}
