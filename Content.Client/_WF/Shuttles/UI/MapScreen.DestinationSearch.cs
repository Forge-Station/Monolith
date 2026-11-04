using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    /// <summary>Filters the destination register as the search text changes.</summary>
    private void WfInitDestinationSearch()
    {
        DestinationSearch.OnTextChanged += _ => WfFilterDestinations();
    }

    /// <summary>Keeps a long destination name inside the register and shows it whole on hover.</summary>
    private static void WfFitMapObject(Button button, string name)
    {
        button.ClipText = true;
        button.ToolTip = name;
    }

    /// <summary>
    /// Shows only the destinations whose name, or whose sector's name, contains the search text, and opens every
    /// sector with a match. An empty search shows everything and leaves the sectors as they were.
    /// </summary>
    private void WfFilterDestinations()
    {
        var search = DestinationSearch.Text.Trim();
        var searching = search.Length > 0;

        foreach (var child in HyperspaceDestinations.Children)
        {
            if (child is not Collapsible { Heading: CollapsibleHeading heading, Body: { } body } map)
                continue;

            // A clipped label wants no width, so a centred one would be laid out at none: stretch it, centre the text.
            heading.Label.ClipText = true;
            heading.Label.HorizontalAlignment = Control.HAlignment.Stretch;
            heading.Label.Align = Label.AlignMode.Center;
            heading.ToolTip = heading.Title;
            var sectorMatch = !searching || Matches(heading.Title, search);
            var anyVisible = false;

            foreach (var contents in body.Children)
            {
                foreach (var row in contents.Children)
                {
                    if (!_mapObjectControls.TryGetValue(row, out var name))
                        continue;

                    row.Visible = sectorMatch || Matches(name, search);
                    anyVisible |= row.Visible;
                }
            }

            map.Visible = !searching || sectorMatch || anyVisible;
            if (searching && anyVisible)
                map.BodyVisible = true;
        }
    }

    private static bool Matches(string? text, string search)
    {
        return text != null && text.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }
}
