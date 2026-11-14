using System.Globalization;
using System.Text;

namespace Content.Server._WF.ShipPa;

/// <summary>Turns links and track titles into the short one-line labels the PA panel lists.</summary>
public static class ShipPaPlaylistText
{
    /// <summary>Longest label sent to clients.</summary>
    public const int MaxLabelLength = 48;

    /// <summary>A link without its scheme, login, port or fragment, cut to label length.</summary>
    public static string ShortenLink(string url)
    {
        var text = url;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            text = host + (uri.PathAndQuery == "/" ? string.Empty : uri.PathAndQuery);
        }

        return Label(text);
    }

    /// <summary>Text on one line without control or invisible characters, cut to label length.</summary>
    public static string Label(string text)
    {
        var builder = new StringBuilder();
        var gap = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                gap = builder.Length > 0;
                continue;
            }

            // Direction marks and zero-width characters could disguise a title.
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
                continue;

            if (gap)
            {
                builder.Append(' ');
                gap = false;
            }

            builder.Append(c);

            if (builder.Length > MaxLabelLength)
                break;
        }

        if (builder.Length <= MaxLabelLength)
            return builder.ToString();

        var keep = MaxLabelLength - 3;

        // Don't leave half a surrogate pair at the cut.
        if (char.IsHighSurrogate(builder[keep - 1]))
            keep--;

        return builder.ToString(0, keep) + "...";
    }
}
