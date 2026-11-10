using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Wolfgate settings.
/// </summary>
[CVarDefs]
public sealed class WolfgateCVars
{
    /// <summary>
    /// Which Wolfgate UI skin the client draws. Matches a <c>WolfgateSkin</c> id: "WolfgateRetro", the default, is the
    /// Aphelion cassette-futurism look and "Wolfgate" the hyper-futurist one. Changing it restyles the client
    /// immediately. Keep the default in step with <c>WolfgateSkins.Default</c>.
    /// </summary>
    public static readonly CVarDef<string> UiStyle =
        CVarDef.Create("wf.ui_style", "WolfgateRetro", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Enters the cockpit by itself when the shuttle console is opened while buckled into a seat.
    /// </summary>
    public static readonly CVarDef<bool> CockpitAutoEnter =
        CVarDef.Create("wf.cockpit_auto_enter", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>The MFD page the cockpit last showed, as its page key. Empty or unknown means the NAV page.</summary>
    public static readonly CVarDef<string> CockpitMfdPage =
        CVarDef.Create("wf.cockpit_mfd_page", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether the cockpit was left with the MFD and the world view swapped.</summary>
    public static readonly CVarDef<bool> CockpitMfdSwapped =
        CVarDef.Create("wf.cockpit_mfd_swapped", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether the cockpit was left with the MFD expanded to an even split.</summary>
    public static readonly CVarDef<bool> CockpitMfdExpanded =
        CVarDef.Create("wf.cockpit_mfd_expanded", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether the cockpit was left with the MFD maximised over the world view.</summary>
    public static readonly CVarDef<bool> CockpitMfdMaximised =
        CVarDef.Create("wf.cockpit_mfd_maximised", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether radar screens label their contacts.</summary>
    public static readonly CVarDef<bool> RadarShowIff =
        CVarDef.Create("wf.radar_show_iff", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether radar labels carry their detail lines.</summary>
    public static readonly CVarDef<bool> RadarShowIffDetailed =
        CVarDef.Create("wf.radar_show_iff_detailed", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether radar screens with a docks toggle mark docking ports.</summary>
    public static readonly CVarDef<bool> RadarShowDocks =
        CVarDef.Create("wf.radar_show_docks", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Radar heading offset, in degrees, so a tilted console still reads north at the top.</summary>
    public static readonly CVarDef<int> RadarAzimuth =
        CVarDef.Create("wf.radar_azimuth", 0, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Kill switch for the anatomy system. When false every anatomy gate fails and every anatomy UI is hidden.
    /// </summary>
    public static readonly CVarDef<bool> AnatomyEnabled =
        CVarDef.Create("wf.anatomy_enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Version of the adult content notice this client has acknowledged; 0 means never.
    /// </summary>
    public static readonly CVarDef<int> AnatomyNoticeSeen =
        CVarDef.Create("wf.anatomy_notice_seen", 0, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Shows the title bar button that pops in-game windows out into their own OS window.
    /// </summary>
    public static readonly CVarDef<bool> WindowPopout =
        CVarDef.Create("wf.ui_window_popout", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Comma separated entity prototype ids starred in the spawn menu's Favourites folder.
    /// </summary>
    public static readonly CVarDef<string> SpawnFavourites =
        CVarDef.Create("wf.spawn_favourites", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// The player's chat tabs: <c>name=channel,channel</c> entries separated by <c>|</c>, with <c>*</c> for every
    /// channel. Empty means the default tabs.
    /// </summary>
    public static readonly CVarDef<string> ChatTabs =
        CVarDef.Create("wf.chat_tabs", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether saved chat tabs have had the Subtle channel added to an old default Local tab, done once.</summary>
    public static readonly CVarDef<bool> ChatTabsSubtleAdded =
        CVarDef.Create("wf.chat_tabs_subtle_added", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether saved chat tabs that show LOOC have had the SOOC channel added, done once.</summary>
    public static readonly CVarDef<bool> ChatTabsSubtleOocAdded =
        CVarDef.Create("wf.chat_tabs_subtle_ooc_added", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
