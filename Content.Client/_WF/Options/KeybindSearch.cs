using Content.Client.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Options;

/// <summary>Filters the Controls tab's keybind list by action name or bound key.</summary>
public static class KeybindSearch
{
    /// <summary>
    /// Shows the rows of <paramref name="container"/> whose name contains <paramref name="query"/>, or whose bound key
    /// equals it, and the headers of sections that still have a row. An empty query shows everything.
    /// Returns whether any row is shown.
    /// </summary>
    public static bool Filter(Control container, string query)
    {
        query = query.Trim();
        var all = query.Length == 0;

        Control? header = null;
        Control? spacer = null;
        var sectionShown = false;
        var anyShown = false;

        foreach (var child in container.Children)
        {
            switch (child)
            {
                // Headers are bare labels; spacers are empty controls placed before each header after the first.
                case Label:
                    CloseSection(header, spacer, sectionShown, all);
                    header = child;
                    sectionShown = false;
                    break;
                case { ChildCount: 0 } when child.GetType() == typeof(Control):
                    spacer = child;
                    break;
                default:
                    child.Visible = all || Matches(child, query);
                    sectionShown |= child.Visible;
                    anyShown |= child.Visible;
                    break;
            }
        }

        CloseSection(header, spacer, sectionShown, all);
        return anyShown;
    }

    private static void CloseSection(Control? header, Control? spacer, bool shown, bool all)
    {
        if (header != null)
            header.Visible = all || shown;
        if (spacer != null)
            spacer.Visible = all;
    }

    /// <summary>Checkbox and action names match by substring; bind buttons match their whole key text.</summary>
    private static bool Matches(Control control, string query)
    {
        switch (control)
        {
            case CheckBox box:
                return box.Text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
            case Button button:
                return !button.HasStyleClass(StyleBase.ButtonCaution)
                       && string.Equals(button.Text, query, StringComparison.OrdinalIgnoreCase);
            case Label label:
                return label.Text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
        }

        foreach (var child in control.Children)
        {
            if (Matches(child, query))
                return true;
        }

        return false;
    }
}
