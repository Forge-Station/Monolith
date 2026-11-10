using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Content.Shared._WF.Headshot;

/// <summary>Headshot URL checks shared by the profile editor and the server.</summary>
public static class HeadshotRules
{
    public const int MaxUrlLength = 512;

    /// <summary>Headshots are scaled to fit a square of this many pixels, the size they zoom to on hover.</summary>
    public const int ImageSize = 256;

    /// <summary>Size of the square headshots are shown in until hovered.</summary>
    public const int ThumbnailSize = 128;

    /// <summary>Returns the trimmed URL, or an empty string when it is not a usable headshot URL.</summary>
    public static string Clean(string? url)
    {
        var trimmed = url?.Trim() ?? string.Empty;
        return IsValid(trimmed, out _) ? trimmed : string.Empty;
    }

    /// <summary>Checks a trimmed, non-empty URL. <paramref name="reason"/> is a loc id.</summary>
    public static bool IsValid(string url, [NotNullWhen(false)] out string? reason)
    {
        reason = null;
        if (url.Length > MaxUrlLength)
            reason = "wf-headshot-error-too-long";
        else if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            reason = "wf-headshot-error-https";
        else if (Parse(url) is not { } uri)
            reason = "wf-headshot-error-invalid";
        else if (uri.UserInfo.Length > 0 || IsIpLiteral(uri.Host))
            reason = "wf-headshot-error-host";

        return reason == null;
    }

    // Uri.TryCreate and Uri.HostNameType take enums the client sandbox doesn't allow.
    private static Uri? Parse(string url)
    {
        try
        {
            return new Uri(url);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Uri writes every IPv4 form as dotted decimal and IPv6 in brackets.</summary>
    private static bool IsIpLiteral(string host)
    {
        return host.StartsWith('[') || host.All(c => c is '.' or >= '0' and <= '9');
    }

    /// <summary>Whether <paramref name="host"/> matches the comma-separated allowlist. An empty list allows any.</summary>
    public static bool IsHostAllowed(string host, string allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(allowedHosts))
            return true;

        foreach (var entry in allowedHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (host.Equals(entry, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + entry, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
