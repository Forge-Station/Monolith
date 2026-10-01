// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using System.Text;
using Content.Client._Onyx.HealthExaminable;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client.Examine;

public sealed partial class ExamineSystem
{
    private const float PartStatusMaxWidth = 520f;
    private const float PartStatusMarkerWidth = 12f;
    private const float PartStatusListMaxHeight = 360f;

    private bool TryAddPartStatusMessage(Control parent, FormattedMessage message)
    {
        var hasPartStatus = false;
        foreach (var node in message.Nodes)
        {
            if (node.Name != "partstatus")
                continue;

            hasPartStatus = true;
            break;
        }

        if (!hasPartStatus)
            return false;

        var segment = new StringBuilder();
        var skipChatCopy = false;
        BoxContainer? statusList = null;
        var textSegments = new List<RichTextLabel>();
        var statusIndex = 0;
        foreach (var node in message.Nodes)
        {
            if (node.Name == "partstatusend")
            {
                skipChatCopy = false;
                continue;
            }

            if (skipChatCopy)
                continue;

            if (node.Name != "partstatus")
            {
                segment.Append(node);
                continue;
            }

            AddTextSegment(parent, segment, textSegments);
            if (PartStatusTag.TryRead(node, out var summary, out var severity, out var details))
            {
                if (statusList == null)
                {
                    statusList = new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Vertical,
                        SeparationOverride = 2,
                        MaxWidth = PartStatusMaxWidth,
                    };
                    parent.AddChild(new ScrollContainer
                    {
                        MaxWidth = PartStatusMaxWidth,
                        MaxHeight = PartStatusListMaxHeight,
                        ReturnMeasure = true,
                        HScrollEnabled = false,
                        Children = { statusList },
                    });
                }

                statusList.AddChild(CreatePartStatus(summary, severity, details, statusIndex++));
            }
            skipChatCopy = true;
        }

        AddTextSegment(parent, segment, textSegments);
        if (statusList != null)
        {
            statusList.Measure(Vector2Helpers.Infinity);
            foreach (var label in textSegments)
                label.MaxWidth = statusList.DesiredSize.X;
        }
        parent.AddChild(new Control { MinHeight = 8 });
        return true;
    }

    private static void AddTextSegment(Control parent, StringBuilder segment, List<RichTextLabel> textSegments)
    {
        var markup = segment.ToString();
        segment.Clear();
        if (string.IsNullOrWhiteSpace(FormattedMessage.RemoveMarkupPermissive(markup)))
            return;

        var label = new RichTextLabel
        {
            Margin = new Thickness(4, 4, 0, 0),
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
        textSegments.Add(label);
    }

    private static Control CreatePartStatus(string summary, string severity, string details, int index)
    {
        var hasDetails = !string.IsNullOrWhiteSpace(details);
        var arrow = new Label
        {
            Text = hasDetails ? "▸" : "•",
            SetWidth = PartStatusMarkerWidth,
            FontColorOverride = SeverityColor(severity),
            VerticalAlignment = Control.VAlignment.Top,
        };
        var summaryLabel = new RichTextLabel
        {
            MaxWidth = PartStatusMaxWidth - PartStatusMarkerWidth - 10f,
        };
        summaryLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(summary));

        var heading = new ContainerButton
        {
            MouseFilter = hasDetails ? Control.MouseFilterMode.Stop : Control.MouseFilterMode.Ignore,
            MaxWidth = PartStatusMaxWidth,
            StyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.Transparent,
                ContentMarginLeftOverride = 4,
                ContentMarginTopOverride = 2,
                ContentMarginRightOverride = 4,
                ContentMarginBottomOverride = 2,
            },
        };
        var headingRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 1,
            MaxWidth = PartStatusMaxWidth,
        };
        headingRow.AddChild(arrow);
        headingRow.AddChild(summaryLabel);
        heading.AddChild(headingRow);

        var detailLabel = new RichTextLabel
        {
            Margin = new Thickness(4, 0, 4, 4),
            MaxWidth = PartStatusMaxWidth - 22f,
        };
        detailLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(details));

        var detailPanel = new PanelContainer
        {
            Visible = false,
            MaxWidth = PartStatusMaxWidth - PartStatusMarkerWidth,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#121820C0"),
                ContentMarginLeftOverride = 14,
                ContentMarginTopOverride = 4,
                ContentMarginRightOverride = 4,
                ContentMarginBottomOverride = 1,
            },
            Children = { detailLabel },
        };

        var container = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MaxWidth = PartStatusMaxWidth,
        };
        container.AddChild(heading);
        container.AddChild(detailPanel);

        var entry = new PanelContainer
        {
            MaxWidth = PartStatusMaxWidth,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = index % 2 == 0
                    ? Color.FromHex("#1B222B90")
                    : Color.FromHex("#252E3A90"),
            },
            Children = { container },
        };

        if (hasDetails)
        {
            heading.OnPressed += _ =>
            {
                detailPanel.Visible = !detailPanel.Visible;
                arrow.Text = detailPanel.Visible ? "▾" : "▸";
                InvalidateParents(entry);
            };
        }

        return entry;
    }

    private static Color SeverityColor(string severity) => severity switch
    {
        "minor" => Color.Yellow,
        "moderate" => Color.Orange,
        "severe" => Color.Red,
        "critical" => Color.Crimson,
        _ => Color.Green,
    };

    private static void InvalidateParents(Control control)
    {
        Control? current = control;
        while (current != null)
        {
            current.InvalidateMeasure();
            current.InvalidateArrange();
            current = current.Parent;
        }
    }
}
