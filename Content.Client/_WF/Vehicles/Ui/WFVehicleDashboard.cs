using System.Numerics;
using System.Linq;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>
/// Two low beveled wings either side of the hand slots, lifted above them where the screen is too narrow, with the
/// equipment tray opening above the right wing.
/// </summary>
public sealed class WFVehicleDashboard : LayoutContainer
{
    public const float LeftWidth = 340;
    public const float RightWidth = 440;
    public const float BankHeight = 186;
    public const float TrayWidth = 480;
    public const float MaxTrayHeight = 300;

    /// <summary>Screen edge kept clear for the corner buttons and the body doll.</summary>
    private const float Corner = 104;
    private const float Gap = 8;

    // The inner side is bevelled, so content keeps further from it.
    private const float Outer = 16;
    private const float Inner = 34;
    private const float Vertical = 12;

    public BoxContainer LeftBank { get; } = WFInstrumentTheme.Column();
    public BoxContainer RightBank { get; } = WFInstrumentTheme.Column();
    public BoxContainer Details { get; } = WFInstrumentTheme.Column();
    public UIBox2 LeftBounds { get; private set; }
    public UIBox2 RightBounds { get; private set; }
    public UIBox2 TrayBounds { get; private set; }

    public WFVehicleDashboard()
    {
        Name = "WFVehicleHud";
        MouseFilter = MouseFilterMode.Ignore;
        AddChild(LeftBank);
        AddChild(RightBank);
        AddChild(Details);
    }

    /// <summary>Puts the wings beside the hand slots, or both above them when either side lacks room.</summary>
    public void Fit(Vector2 viewport, UIBox2 hotbar)
    {
        SetSize = viewport;
        var beside = hotbar.Left - Gap - LeftWidth >= Corner && viewport.X - hotbar.Right - Gap - RightWidth >= Corner;
        if (beside)
        {
            var top = viewport.Y - Gap - BankHeight;
            LeftBounds = UIBox2.FromDimensions(new Vector2(hotbar.Left - Gap - LeftWidth, top), new Vector2(LeftWidth, BankHeight));
            RightBounds = UIBox2.FromDimensions(new Vector2(hotbar.Right + Gap, top), new Vector2(RightWidth, BankHeight));
        }
        else
        {
            var top = MathF.Max(0, hotbar.Top - Gap - BankHeight);
            var left = Math.Clamp(hotbar.Center.X - (LeftWidth + RightWidth + Gap) / 2, Gap,
                MathF.Max(Gap, viewport.X - Gap - LeftWidth - RightWidth - Gap));
            LeftBounds = UIBox2.FromDimensions(new Vector2(left, top), new Vector2(LeftWidth, BankHeight));
            RightBounds = UIBox2.FromDimensions(new Vector2(LeftBounds.Right + Gap, top), new Vector2(RightWidth, BankHeight));
        }

        Place(LeftBank, LeftBounds, new Thickness(Outer, Vertical, Inner, Vertical));
        Place(RightBank, RightBounds, new Thickness(Inner, Vertical, Outer, Vertical));

        // Measured unbounded: inside the tray's current size a panel just opened would measure as the closed tray.
        var detailHeight = 0f;
        foreach (var child in Details.Children)
        {
            if (!child.Visible)
                continue;
            child.Measure(new Vector2(TrayWidth - 16, float.PositiveInfinity));
            detailHeight = MathF.Max(detailHeight, child.DesiredSize.Y);
        }
        detailHeight = MathF.Min(MaxTrayHeight, detailHeight + 16);
        var trayLeft = MathF.Max(Gap, MathF.Min(RightBounds.Left, viewport.X - Gap - TrayWidth));
        TrayBounds = UIBox2.FromDimensions(new Vector2(trayLeft, MathF.Max(0, RightBounds.Top - detailHeight - Gap)),
            new Vector2(TrayWidth, detailHeight));
        Place(Details, TrayBounds, new Thickness(8));
    }

    private static void Place(Control control, UIBox2 box, Thickness padding)
    {
        SetPosition(control, box.TopLeft + new Vector2(padding.Left, padding.Top));
        control.SetSize = Vector2.Max(Vector2.Zero, box.Size - new Vector2(padding.SumHorizontal, padding.SumVertical));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        DrawWing(handle, LeftBounds, false);
        DrawWing(handle, RightBounds, true);
        if (Details.Children.Any(child => child.Visible))
        {
            var box = new UIBox2(TrayBounds.TopLeft * UIScale, TrayBounds.BottomRight * UIScale);
            handle.DrawRect(box, WFInstrumentTheme.Ink.WithAlpha(0.86f));
            handle.DrawRect(box, WFInstrumentTheme.Accent.WithAlpha(0.5f), false);
        }
    }

    /// <summary>A wing with its inner corners cut back toward the hand slots.</summary>
    private void DrawWing(DrawingHandleScreen handle, UIBox2 bounds, bool mirror)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        var points = new[] { new Vector2(0, 20), new Vector2(20, 0), new Vector2(width - 46, 0),
            new Vector2(width, 40), new Vector2(width, height - 60), new Vector2(width - 26, height),
            new Vector2(18, height), new Vector2(0, height - 18) };
        for (var i = 0; i < points.Length; i++)
        {
            if (mirror)
                points[i].X = width - points[i].X;
            points[i] = (points[i] + bounds.TopLeft) * UIScale;
        }

        var triangles = new Vector2[(points.Length - 2) * 3];
        for (var i = 1; i < points.Length - 1; i++)
        {
            triangles[(i - 1) * 3] = points[0];
            triangles[(i - 1) * 3 + 1] = points[i];
            triangles[(i - 1) * 3 + 2] = points[i + 1];
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, triangles, WFInstrumentTheme.Ink.WithAlpha(0.6f));
        for (var i = 0; i < points.Length; i++)
            handle.DrawLine(points[i], points[(i + 1) % points.Length], WFInstrumentTheme.Accent.WithAlpha(0.5f));
        var start = (bounds.TopLeft + new Vector2(mirror ? 58 : 22, 6)) * UIScale;
        handle.DrawLine(start, start + new Vector2((width - 80) * UIScale, 0), WFInstrumentTheme.Skin.EdgeLight);
    }
}
