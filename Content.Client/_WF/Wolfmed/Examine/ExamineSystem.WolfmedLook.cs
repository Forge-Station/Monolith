using System.Text;
using Content.Client._WF.Wolfmed.Examine;
using Content.Client._WF.Wolfmed.Medical;
using Content.Shared._WF.Wolfmed.Examine;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client.Examine;

/// <summary>Draws a visual inspection in the examine tooltip: a row of finding chips per damaged part.</summary>
// The server sends one WolfmedLookTag node per damaged part. Chips carry the analyzer's pictograms with the full
// sentence on hover; the whole-body lines stay as text, and each part's plain-text copy is skipped since the row
// already says it.
public sealed partial class ExamineSystem
{
    private const float LookMaxWidth = 520f;
    private const float LookListMaxHeight = 360f;
    private const float LookIconSize = 17f;

    [Dependency] private IResourceCache _lookResources = default!;
    [Dependency] private SpriteSystem _lookSprites = default!;

    /// <summary>Built on first use: the examine runs long before anyone looks at a wounded body.</summary>
    private WolfmedAnalyzerIcons? _lookIcons;

    /// <summary>
    /// Replaces the plain examine label with rows when the message carries a visual inspection. Public so the
    /// row layout can be built and measured in a test without driving the client's input.
    /// </summary>
    public bool TryAddWolfmedLookMessage(Control parent, FormattedMessage message)
    {
        var carries = false;
        foreach (var node in message.Nodes)
        {
            if (node.Name != WolfmedLookTag.Part)
                continue;

            carries = true;
            break;
        }

        if (!carries)
            return false;

        var segment = new StringBuilder();
        var text = new List<RichTextLabel>();
        var names = new List<WolfmedLookRow>();
        BoxContainer? rows = null;
        WolfmedLookRow? row = null;
        var skip = false;

        foreach (var node in message.Nodes)
        {
            if (node.Name == WolfmedLookTag.End)
            {
                skip = false;
                continue;
            }

            // The parser closes a self-closing tag with a second node of the same name and no attributes.
            if (node.Name == WolfmedLookTag.Part)
            {
                if (node.Closing)
                    continue;

                // Anything written before this part belongs above the list.
                AddLookText(parent, segment, text);
                rows ??= CreateLookList(parent);
                row = null;
                if (WolfmedLookTag.TryReadPart(node, out var name, out var accent))
                {
                    row = new WolfmedLookRow(name, LookColour(accent), LookMaxWidth, names.Count % 2 == 1);
                    names.Add(row);
                    rows.AddChild(row);
                }

                skip = true;
                continue;
            }

            if (node.Name == WolfmedLookTag.Finding)
            {
                if (!node.Closing && row != null && WolfmedLookTag.TryReadFinding(node, out var finding))
                {
                    var colour = LookColour(finding.Colour);
                    row.Chips.AddChild(
                        new WolfmedLookChip(LookIcons.Get(finding.Icon), colour, finding, LookIconSize));
                }

                continue;
            }

            if (skip)
                continue;

            segment.Append(node);
        }

        AddLookText(parent, segment, text);

        if (rows != null)
        {
            // One name column for every row, so the chips start at the same place down the list.
            var width = 0f;
            foreach (var entry in names)
            {
                entry.PartLabel.Measure(Vector2Helpers.Infinity);
                width = MathF.Max(width, entry.PartLabel.DesiredSize.X);
            }

            foreach (var entry in names)
                entry.SetNameWidth(width);

            rows.Measure(Vector2Helpers.Infinity);
            foreach (var label in text)
                label.MaxWidth = MathF.Max(rows.DesiredSize.X, 240f);
        }

        parent.AddChild(new Control { MinHeight = 6 });
        return true;
    }

    private static BoxContainer CreateLookList(Control parent)
    {
        var list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            MaxWidth = LookMaxWidth,
        };

        parent.AddChild(new ScrollContainer
        {
            MaxWidth = LookMaxWidth,
            MaxHeight = LookListMaxHeight,
            ReturnMeasure = true,
            HScrollEnabled = false,
            Children = { list },
        });

        return list;
    }

    /// <summary>
    /// Flushes whatever plain markup has piled up into a label. The newlines that separated it from the rows
    /// are dropped, or the list would sit under a blank line.
    /// </summary>
    private static void AddLookText(Control parent, StringBuilder segment, List<RichTextLabel> text)
    {
        var markup = segment.ToString().Trim('\n');
        segment.Clear();
        if (string.IsNullOrWhiteSpace(FormattedMessage.RemoveMarkupPermissive(markup)))
            return;

        var label = new RichTextLabel
        {
            Margin = new Thickness(4, 3, 0, 0),
            HorizontalExpand = false,
            HorizontalAlignment = Control.HAlignment.Left,
        };
        label.SetMessage(FormattedMessage.FromMarkupPermissive(markup),
        [
            typeof(BoldItalicTag),
            typeof(BoldTag),
            typeof(BulletTag),
            typeof(ColorTag),
            typeof(FontTag),
            typeof(HeadingTag),
            typeof(ItalicTag),
        ]);
        parent.AddChild(label);
        text.Add(label);
    }

    /// <summary>The analyzer's palette, so a cut is the same red on the examine as it is on the scanner.</summary>
    private static Color LookColour(string key) => WolfmedWoundStyle.Look(key);

    private WolfmedAnalyzerIcons LookIcons => _lookIcons ??= new WolfmedAnalyzerIcons(_lookResources, _lookSprites);
}
