using Robust.Shared.Utility;

namespace Content.Shared._WF.Wolfmed.Examine;

/// <summary>The markup tags that carry a visual inspection inside an examine message.</summary>
// Per part: a wolfmedlook node, a wolfmedlookfinding node per finding, the part's plain-text line, then
// wolfmedlookend. The client builds rows from the nodes and skips the plain line; anything else that renders the
// message (the chat copy, a log) keeps the plain line and drops the nodes.
/// <remarks>
/// Written in the same shape as Onyx's <c>partstatus</c>, which is the control this replaces. Attribute
/// values are plain text: the label and the tooltip have their markup removed before they are escaped, so
/// nothing in a locale string can break the tag, and the colour comes from the palette instead.
/// </remarks>
public static class WolfmedLookTag
{
    public const string Part = "wolfmedlook";
    public const string Finding = "wolfmedlookfinding";
    public const string End = "wolfmedlookend";

    /// <summary>Writes a part's node and its findings' nodes. The caller adds the plain line and the end.</summary>
    public static void WritePart(FormattedMessage message, WolfmedLookPart part)
    {
        message.AddMarkupOrThrow(
            $"[{Part} name=\"{Escape(part.Name)}\" accent=\"{Escape(part.Accent)}\" /]");

        foreach (var finding in part.Findings)
        {
            message.AddMarkupOrThrow(
                $"[{Finding} icon=\"{Escape(finding.Icon)}\" colour=\"{Escape(finding.Colour)}\"" +
                $" label=\"{Escape(finding.Label)}\" tip=\"{Escape(finding.Text)}\" /]");
        }
    }

    /// <summary>Closes the part, ending the stretch of markup the row renderer skips.</summary>
    public static void WriteEnd(FormattedMessage message) => message.AddMarkupOrThrow($"[{End} /]");

    public static bool TryReadPart(MarkupNode node, out string name, out string accent)
    {
        name = string.Empty;
        accent = WolfmedLookPalette.Neutral;
        return node.Name == Part &&
               TryGetAttribute(node, "name", out name) &&
               TryGetAttribute(node, "accent", out accent);
    }

    public static bool TryReadFinding(MarkupNode node, out WolfmedLookObservation finding)
    {
        finding = default!;
        if (node.Name != Finding ||
            !TryGetAttribute(node, "icon", out var icon) ||
            !TryGetAttribute(node, "colour", out var colour) ||
            !TryGetAttribute(node, "label", out var label) ||
            !TryGetAttribute(node, "tip", out var tip))
            return false;

        finding = new WolfmedLookObservation(icon, colour, label, tip);
        return true;
    }

    private static bool TryGetAttribute(MarkupNode node, string name, out string value)
    {
        value = string.Empty;
        if (!node.Attributes.TryGetValue(name, out var parameter) ||
            !parameter.TryGetString(out var found))
            return false;

        value = found;
        return true;
    }

    private static string Escape(string value) => FormattedMessage.EscapeStringParameter(value);
}
