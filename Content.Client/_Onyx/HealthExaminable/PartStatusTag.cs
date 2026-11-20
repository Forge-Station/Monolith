// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Robust.Shared.Utility;

namespace Content.Client._Onyx.HealthExaminable;

public static class PartStatusTag
{
    public static bool TryRead(MarkupNode node, out string summary, out string severity, out string details)
    {
        summary = string.Empty;
        severity = string.Empty;
        details = string.Empty;
        return TryGetAttribute(node, "summary", out summary) &&
               TryGetAttribute(node, "severity", out severity) &&
               TryGetAttribute(node, "details", out details);
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
}
