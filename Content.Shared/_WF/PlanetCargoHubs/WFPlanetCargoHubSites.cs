namespace Content.Shared._WF.PlanetCargoHubs;

/// <summary>Names a planetside depot's hub site by its letter, and tells a hub's site from any other by that name.</summary>
public static class WFPlanetCargoHubSites
{
    /// <summary>The Fluent id of a hub site's name, before its letter.</summary>
    public const string SiteNamePrefix = "wf-planet-cargo-hub-";

    /// <summary>The colour a hub is marked in on radars, maps and scans: the space depots' IFF green.</summary>
    public static readonly Color Colour = Color.FromHex("#37C837");

    /// <summary>The site name of depot <paramref name="index"/>'s hub: 0 for A, 1 for B.</summary>
    public static string SiteName(int index) => SiteNamePrefix + "abcdefghijklmnopqrstuvwxyz".Substring(index, 1);

    /// <summary>The depot index a site name belongs to, or false when it isn't a hub's.</summary>
    public static bool TryIndex(string name, out int index)
    {
        index = -1;
        if (!name.StartsWith(SiteNamePrefix, StringComparison.Ordinal) || name.Length != SiteNamePrefix.Length + 1)
            return false;

        index = name[name.Length - 1] - 'a';
        return index is >= 0 and < 26;
    }

    /// <summary>Whether a site name is a planetside depot's hub.</summary>
    public static bool IsHub(string name) => TryIndex(name, out _);
}
