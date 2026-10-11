using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Cockpit;

/// <summary>Temporarily borrows live controls and restores their exact parents and layout.</summary>
public sealed class WFCockpitLease
{
    private readonly List<Action> _restore = new();

    /// <summary>Enumerates controls before their temporary layout is rebuilt.</summary>
    public static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }

    /// <summary>Records additional presentation state that must be restored on exit.</summary>
    public void Remember(Action restore) => _restore.Add(restore);

    /// <summary>Retains a container's original children while a cockpit layout uses it.</summary>
    public void Clear(Control root)
    {
        var children = root.Children.ToArray();
        foreach (var child in children)
            root.RemoveChild(child);
        Remember(() =>
        {
            root.DisposeAllChildren();
            foreach (var child in children)
                root.AddChild(child);
        });
    }

    /// <summary>Moves a control without replacing any of its live bindings.</summary>
    public T Take<T>(T control, bool restoreVisibility = true) where T : Control
    {
        var parent = control.Parent;
        var index = parent == null ? 0 : control.GetPositionInParent();
        var minimum = control.MinSize;
        var maximum = control.MaxSize;
        var size = control.SetSize;
        var margin = control.Margin;
        var horizontal = control.HorizontalExpand;
        var vertical = control.VerticalExpand;
        var hAlign = control.HorizontalAlignment;
        var vAlign = control.VerticalAlignment;
        var visible = control.Visible;
        var orientation = (control as BoxContainer)?.Orientation;
        parent?.RemoveChild(control);
        Remember(() =>
        {
            control.Parent?.RemoveChild(control);
            control.MinSize = minimum;
            control.MaxSize = maximum;
            control.SetSize = size;
            control.Margin = margin;
            control.HorizontalExpand = horizontal;
            control.VerticalExpand = vertical;
            control.HorizontalAlignment = hAlign;
            control.VerticalAlignment = vAlign;
            if (restoreVisibility)
                control.Visible = visible;
            if (control is BoxContainer box && orientation is { } direction)
                box.Orientation = direction;
            if (parent == null)
                return;
            parent.AddChild(control);
            control.SetPositionInParent(Math.Min(index, parent.ChildCount - 1));
        });
        control.MinSize = Vector2.Zero;
        control.MaxSize = new Vector2(float.PositiveInfinity);
        control.SetSize = new Vector2(float.NaN);
        control.Margin = new Thickness(0);
        control.HorizontalExpand = true;
        control.VerticalExpand = false;
        control.HorizontalAlignment = Control.HAlignment.Stretch;
        control.VerticalAlignment = Control.VAlignment.Stretch;
        return control;
    }

    /// <summary>Restores in reverse order so nested controls return before their containers; a failing step is logged and skipped.</summary>
    public void Restore()
    {
        var restore = _restore.ToArray();
        _restore.Clear();
        for (var i = restore.Length - 1; i >= 0; i--)
        {
            try
            {
                restore[i]();
            }
            catch (Exception e)
            {
                Logger.GetSawmill("wf.cockpit").Error($"Cockpit restore step failed: {e}");
            }
        }
    }
}
