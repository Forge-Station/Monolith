using System.Numerics;
using Content.Client._Shitmed.UserInterface.Systems.Targeting.Widgets;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._WF.Planets;

/// <summary>Where the climate panels sit: above the targeting doll at the screen's right, clear of the alert icons.</summary>
public static class WFHudPlacement
{
    private const float Gap = 8f;

    /// <summary>The top left of a panel of this size in the popup layer.</summary>
    public static Vector2 Place(IUserInterfaceManager ui, Vector2 size)
    {
        var root = ui.PopupRoot;
        var right = root.Width - 12;
        var bottom = root.Height - 150;
        if (ui.GetActiveUIWidgetOrNull<TargetingControl>() is { Visible: true, Width: > 0 } target)
        {
            var position = target.GlobalPosition - root.GlobalPosition;
            right = position.X + target.Width;
            bottom = position.Y - Gap;
        }

        var left = MathF.Max(0, right - size.X);
        var top = MathF.Max(0, bottom - size.Y);

        // The alert and body part icons stack down the same edge and draw over a panel they reach: sit beside them.
        if (ui.GetActiveUIWidgetOrNull<AlertsUI>() is { Visible: true } alerts)
        {
            foreach (var child in alerts.Children)
            {
                if (!child.Visible)
                    continue;

                var box = UIBox2.FromDimensions(child.GlobalPosition - root.GlobalPosition, child.Size);
                var panel = UIBox2.FromDimensions(left, top, size.X, size.Y);
                if (box.Intersects(panel))
                    left = MathF.Max(0, box.Left - Gap - size.X);
            }
        }

        return new Vector2(left, top);
    }
}
