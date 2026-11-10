using Robust.Shared.Utility;

namespace Content.Shared._WF.ExamineCard;

/// <summary>A chip pulled out of the examine text, with the line it replaced as its hover text.</summary>
public sealed class ExamineCardChip(string label, ExamineChipTone tone, string text)
{
    public readonly string Label = label;
    public readonly ExamineChipTone Tone = tone;
    public readonly string Text = text;
}

/// <summary>Splits examine markup into lines and folds the lines a pattern matches into chips.</summary>
public static class ExamineCardText
{
    /// <summary>Stands in for a Fluent variable the server can't know, such as the held items.</summary>
    public const char Wildcard = '\u0001';

    /// <summary>A line as plain text, trimmed and without the list dash some systems put in front.</summary>
    public static string Normalize(string text)
    {
        text = text.Trim();
        if (text.StartsWith("- "))
            text = text[2..].TrimStart();
        return text;
    }

    /// <summary>Whether a line reads as a pattern's fragments in order, with anything in the gaps between them.</summary>
    public static bool Matches(string line, IReadOnlyList<string> fragments)
    {
        if (fragments.Count == 1)
            return line == fragments[0];

        if (!line.StartsWith(fragments[0]) || !line.EndsWith(fragments[^1]))
            return false;

        var at = fragments[0].Length;
        var end = line.Length - fragments[^1].Length;
        if (end < at)
            return false;

        for (var i = 1; i < fragments.Count - 1; i++)
        {
            var found = line.IndexOf(fragments[i], at, end - at, StringComparison.Ordinal);
            if (found < 0)
                return false;
            at = found + fragments[i].Length;
        }

        return true;
    }

    /// <summary>
    /// Removes every line a pattern matches, adding a chip for each one that has a label. Tags on a removed line
    /// stay, since some carry data (such as a wound inspection) rather than formatting.
    /// </summary>
    public static FormattedMessage Fold(FormattedMessage message, IReadOnlyList<ExamineCardPattern> patterns,
        List<ExamineCardChip> chips)
    {
        var result = new FormattedMessage();
        var first = true;
        foreach (var line in SplitLines(message))
        {
            var text = Normalize(PlainText(line));
            ExamineCardPattern? match = null;
            if (text.Length > 0)
            {
                // The most specific pattern wins, so a loose one like "He is _ _." can't take a whole sentence.
                var best = -1;
                foreach (var pattern in patterns)
                {
                    var fixedLength = 0;
                    foreach (var fragment in pattern.Fragments)
                    {
                        fixedLength += fragment.Length;
                    }

                    if (fixedLength <= best || !Matches(text, pattern.Fragments))
                        continue;

                    match = pattern;
                    best = fixedLength;
                }
            }

            if (match != null)
            {
                if (match.Label is { } label && !chips.Exists(c => c.Label == label))
                    chips.Add(new ExamineCardChip(label, match.Tone, text));
            }

            var keepText = match == null;
            if (!keepText && !HasDataTag(line))
                continue;
            if (keepText && text.Length == 0 && !HasDataTag(line))
                continue;

            if (!first)
                result.PushNewline();
            first = false;

            foreach (var node in line)
            {
                if (node.Name == null)
                {
                    if (keepText)
                        result.AddText(node.Value.StringValue ?? string.Empty);
                    continue;
                }

                result.PushTag(node);
            }
        }

        return result;
    }

    /// <summary>Splits markup at its newlines, closing and reopening tags so each line stands alone.</summary>
    public static List<List<MarkupNode>> SplitLines(FormattedMessage message)
    {
        var lines = new List<List<MarkupNode>>();
        var open = new List<MarkupNode>();
        var line = new List<MarkupNode>();

        foreach (var node in message.Nodes)
        {
            if (node.Name != null)
            {
                line.Add(node);
                if (!node.Closing)
                {
                    open.Add(node);
                }
                else
                {
                    var index = open.FindLastIndex(n => n.Name == node.Name);
                    if (index >= 0)
                        open.RemoveAt(index);
                }
                continue;
            }

            var parts = (node.Value.StringValue ?? string.Empty).Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    for (var j = open.Count - 1; j >= 0; j--)
                    {
                        line.Add(new MarkupNode(open[j].Name, null, null, true));
                    }
                    lines.Add(line);
                    line = new List<MarkupNode>(open);
                }

                if (parts[i].Length > 0)
                    line.Add(new MarkupNode(parts[i]));
            }
        }

        lines.Add(line);
        return lines;
    }

    private static string PlainText(List<MarkupNode> line)
    {
        var text = string.Empty;
        foreach (var node in line)
        {
            if (node.Name == null)
                text = string.Concat(text, node.Value.StringValue);
        }
        return text;
    }

    /// <summary>Tags that carry data for a renderer, as opposed to plain text formatting.</summary>
    private static bool HasDataTag(List<MarkupNode> line)
    {
        foreach (var node in line)
        {
            if (node.Name is null or "color" or "font" or "bold" or "italic" or "bolditalic" or "head" or "bullet")
                continue;
            return true;
        }
        return false;
    }
}
