using System.Numerics;
using Content.Shared._WF.Warmth;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Warmth;

/// <summary>
/// The body temperature gauge: a bar from freezing to hot with a marker where the body sits, an arrow for which way it
/// is heading, and a line that says when a fire is warming it.
/// </summary>
public sealed class WFWarmthGauge : BoxContainer
{
    private static readonly (float At, Color Color)[] Ramp =
    {
        (0f, Color.FromHex("#5AA8FF")),
        (0.3f, Color.FromHex("#6FD6C8")),
        (0.5f, Color.FromHex("#8FD694")),
        (0.7f, Color.FromHex("#E6DC96")),
        (0.85f, Color.FromHex("#FFA040")),
        (1f, Color.FromHex("#FF5A5A")),
    };

    private readonly Bar _bar = new();
    private readonly Label _near;

    public int Gauge => _bar.Gauge;
    public WFWarmthTrend Trend => _bar.Trend;
    public bool NearVisible => _near.Visible;

    public WFWarmthGauge(Font font)
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 1;
        MouseFilter = MouseFilterMode.Ignore;

        _near = new Label
        {
            Text = Loc.GetString("wf-warmth-near"),
            FontOverride = font,
            FontColorOverride = Color.FromHex("#FFB060"),
            HorizontalAlignment = HAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
            Visible = false,
        };

        AddChild(_bar);
        AddChild(_near);
    }

    /// <summary>Sets the marker, the arrow and the near-warmth line.</summary>
    public void Set(int gauge, WFWarmthTrend trend, bool nearWarmth)
    {
        _near.Visible = nearWarmth;
        _bar.Set(gauge, trend);
    }

    private static Color RampColor(float at)
    {
        for (var i = 1; i < Ramp.Length; i++)
        {
            if (at > Ramp[i].At)
                continue;

            var (from, to) = (Ramp[i - 1], Ramp[i]);
            return Color.InterpolateBetween(from.Color, to.Color, (at - from.At) / (to.At - from.At));
        }

        return Ramp[^1].Color;
    }

    private sealed class Bar : Control
    {
        private const int Segments = 24;
        private const float BarHeight = 5f;
        private const float ArrowWidth = 12f;

        private readonly List<Vector2> _arrow = new(3);

        public int Gauge { get; private set; } = 50;
        public WFWarmthTrend Trend { get; private set; }

        public Bar()
        {
            MinSize = new Vector2(0f, 12f);
            HorizontalExpand = true;
            MouseFilter = MouseFilterMode.Ignore;
        }

        public void Set(int gauge, WFWarmthTrend trend)
        {
            Gauge = gauge;
            Trend = trend;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var scale = UIScale;
            var width = PixelSize.X;
            var height = PixelSize.Y;
            var barRight = width - ArrowWidth * scale;
            var top = (height - BarHeight * scale) * 0.5f;
            var bottom = top + BarHeight * scale;

            for (var i = 0; i < Segments; i++)
            {
                var color = RampColor((i + 0.5f) / Segments);
                handle.DrawRect(new UIBox2(barRight * i / Segments, top, barRight * (i + 1) / Segments, bottom), color);
            }

            var marker = barRight * Math.Clamp(Gauge, 0, 100) / 100f;
            handle.DrawRect(new UIBox2(marker - scale, top - 2f * scale, marker + scale, bottom + 2f * scale), Color.White);

            var centre = new Vector2(width - ArrowWidth * 0.5f * scale, height * 0.5f);
            var half = 3.5f * scale;
            if (Trend == WFWarmthTrend.Steady)
            {
                handle.DrawRect(new UIBox2(centre.X - half, centre.Y - 0.5f * scale, centre.X + half, centre.Y + 0.5f * scale),
                    Color.FromHex("#8A9C96"));
                return;
            }

            var warming = Trend == WFWarmthTrend.Warming;
            var tip = new Vector2(centre.X, centre.Y + (warming ? -half : half));
            var baseY = centre.Y + (warming ? half : -half);
            _arrow.Clear();
            _arrow.Add(tip);
            _arrow.Add(new Vector2(centre.X - half, baseY));
            _arrow.Add(new Vector2(centre.X + half, baseY));
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _arrow,
                warming ? Color.FromHex("#FFA040") : Color.FromHex("#7EC8FF"));
        }
    }
}
