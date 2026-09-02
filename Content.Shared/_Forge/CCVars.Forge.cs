// hud.offer_mode_indicators_point_show -> Port From SS14 Corvax-Next

using Robust.Shared.Configuration;

namespace Content.Shared._Forge.CCVars;

/// <summary>
///     Corvax modules console variables
/// </summary>
[CVarDefs]
// ReSharper disable once InconsistentNaming
public sealed class ForgeCCVars
{
    //          Regions are listed in alphabetical order
    // but not "Other" - he is an a/outsider (blame for everything)
    //                 (/◕ヮ◕)/  [___]  \(◕ヮ◕\)

    #region Autokick
    /// <summary>
    ///     Minutes before kicking a latejoin player that has not clocked in.
    /// </summary>
    public static readonly CVarDef<float> AutoKickPendingClockInMinutes =
        CVarDef.Create("autokick.pending_clockin_minutes", 15f, CVar.SERVERONLY);

    /// <summary>
    ///     Minutes before kicking a guest without a job role for AFK.
    /// </summary>
    public static readonly CVarDef<float> AutoKickGuestAfkMinutes =
        CVarDef.Create("autokick.guest_afk_minutes", 25f, CVar.SERVERONLY);
    #endregion

    #region Barks
    /// <summary>
    /// Responsible for turning on and off the bark system.
    /// </summary>
    public static readonly CVarDef<bool> BarksEnabled =
        CVarDef.Create("voice.barks_enabled", true, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of Barks sound
    /// </summary>
    public static readonly CVarDef<float> BarksVolume =
        CVarDef.Create("voice.barks_volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
    #endregion

    #region Discord
    public static readonly CVarDef<string> DiscordApiUrl =
        CVarDef.Create("jerry.discord_api_url", "", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    public static readonly CVarDef<bool> DiscordAuthEnabled =
        CVarDef.Create("jerry.discord_auth_enabled", false, CVar.CONFIDENTIAL | CVar.SERVERONLY);

    public static readonly CVarDef<string> DiscordGuildID =
        CVarDef.Create("jerry.discord_guildId", "1222332535628103750", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    public static readonly CVarDef<string> ApiKey =
        CVarDef.Create("jerry.discord_apikey", "", CVar.CONFIDENTIAL | CVar.SERVERONLY);
    #endregion

    #region Mapping
    /// <summary>
    ///     Comma-separated mapping palette favorites in the form e:ProtoId,t:TileId,d:DecalId.
    /// </summary>
    public static readonly CVarDef<string> MappingPaletteFavorites =
        CVarDef.Create("forge.mapping.palette_favorites", "", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Comma-separated recently used mapping palette entries (same encoding as favorites).
    /// </summary>
    public static readonly CVarDef<string> MappingPaletteRecents =
        CVarDef.Create("forge.mapping.palette_recents", "", CVar.CLIENTONLY | CVar.ARCHIVE);
    #endregion

    #region Physic
    /// <summary>
    ///     Nudge entities that stay in hard contact with static geometry for a long time (anti-stuck).
    /// </summary>
    public static readonly CVarDef<bool> AutoUnstuckEnabled =
        CVarDef.Create("forge.physics.auto_unstuck", true, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds of continuous static hard contact before a nudge is applied.
    /// </summary>
    public static readonly CVarDef<float> AutoUnstuckAfterSeconds =
        CVarDef.Create("forge.physics.auto_unstuck_after", 10f, CVar.SERVERONLY);

    /// <summary>
    ///     World-space displacement applied when unsticking (meters).
    /// </summary>
    public static readonly CVarDef<float> AutoUnstuckNudge =
        CVarDef.Create("forge.physics.auto_unstuck_nudge", 2f, CVar.SERVERONLY);
    #endregion

    #region POI Capture
    /// <summary>
    ///     Duration of a POI capture operation, in minutes.
    /// </summary>
    public static readonly CVarDef<float> PoiCaptureDurationMinutes =
        CVarDef.Create("forge.poi_capture.duration_minutes", 10f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum time after a completed capture before a new capture may begin, in hours.
    /// </summary>
    public static readonly CVarDef<float> PoiCaptureRecaptureCooldownHours =
        CVarDef.Create("forge.poi_capture.recapture_cooldown_hours", 1f, CVar.SERVERONLY);

    /// <summary>
    ///     Default radius (in tiles) of the captured POI overlay shown on shuttle radars.
    /// </summary>
    /// <summary>
    ///     Capture zone radius in tiles (≈ meters on radar). Default 1024 matches the
    ///     outer rings on long-range helm radar so the owned POI area is visible.
    ///     Per-grid overrides (e.g. faction bases) may use up to 2048 via YAML.
    /// </summary>
    public static readonly CVarDef<float> PoiCaptureZoneRadiusTiles =
        CVarDef.Create("forge.poi_capture.zone_radius_tiles", 1024f, CVar.SERVERONLY);

    /// <summary>
    ///     Hard cap for POI capture zone radius in tiles (YAML roundstart + marker overrides).
    /// </summary>
    public static readonly CVarDef<float> PoiCaptureZoneRadiusMaxTiles =
        CVarDef.Create("forge.poi_capture.zone_radius_max_tiles", 2048f, CVar.SERVERONLY);

    /// <summary>
    ///     Default interval between POI treasury reward rolls, in minutes.
    ///     Per-treasury override via <c>rewardIntervalMinutes</c> in YAML.
    /// </summary>
    public static readonly CVarDef<float> PoiCaptureRewardIntervalMinutes =
        CVarDef.Create("forge.poi_capture.reward_interval_minutes", 10f, CVar.SERVERONLY);

    /// <summary>
    ///     Fraction of a sale price routed to the owning POI treasury (in addition to sector taxes).
    /// </summary>
    public static readonly CVarDef<float> PoiCaptureSalesTaxRate =
        CVarDef.Create("forge.poi_capture.sales_tax_rate", 0.1f, CVar.SERVERONLY);
    #endregion

    #region TTS
    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<bool> TTSEnabled =
        CVarDef.Create("tts.enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiUrl =
        CVarDef.Create("tts.api_url", "", CVar.SERVERONLY | CVar.CONFIDENTIAL | CVar.ARCHIVE);

    /// <summary>
    /// Auth token of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiToken =
        CVarDef.Create("tts.api_token", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Amount of seconds before timeout for API
    /// </summary>
    public static readonly CVarDef<int> TTSApiTimeout =
        CVarDef.Create("tts.api_timeout", 5, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of TTS sound
    /// </summary>
    public static readonly CVarDef<float> TTSVolume =
        CVarDef.Create("tts.volume", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of radio TTS sound
    /// </summary>
    public static readonly CVarDef<float> TTSRadioVolume =
        CVarDef.Create("tts.radio_volume", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether the client wants local TTS playback enabled.
    /// </summary>
    public static readonly CVarDef<bool> LocalTTSEnabled =
        CVarDef.Create("tts.local_enabled", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Whether the client wants local TTS playback on **Radio event** enabled.
    /// </summary>
    public static readonly CVarDef<bool> LocalRadioTTSEnabled =
        CVarDef.Create("tts.local_radio_enabled", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Count of in-memory cached tts voice lines.
    /// </summary>
    public static readonly CVarDef<int> TTSMaxCache =
        CVarDef.Create("tts.max_cache", 250, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Tts rate limit values are accounted in periods of this size (seconds).
    /// After the period has passed, the count resets.
    /// </summary>
    public static readonly CVarDef<float> TTSRateLimitPeriod =
        CVarDef.Create("tts.rate_limit_period", 2f, CVar.SERVERONLY);

    /// <summary>
    /// How many tts preview messages are allowed in a single rate limit period.
    /// </summary>
    public static readonly CVarDef<int> TTSRateLimitCount =
        CVarDef.Create("tts.rate_limit_count", 3, CVar.SERVERONLY);
    #endregion

    #region UI
    /// <summary>
    ///     Chat log font size in pixels. Independent from the global UI scale.
    /// </summary>
    public static readonly CVarDef<int> ChatFontSize =
        CVarDef.Create("forge.ui.chat_font_size", 12, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Examine tooltip font size in pixels. Independent from the global UI scale.
    /// </summary>
    public static readonly CVarDef<int> ExamineFontSize =
        CVarDef.Create("forge.ui.examine_font_size", 12, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Scale for HUD chrome: hotbar slots, hand slots, and action buttons.
    ///     1 is the default 64px slot size.
    /// </summary>
    public static readonly CVarDef<float> HudScale =
        CVarDef.Create("forge.ui.hud_scale", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Scale for bag/belt storage grid tiles. Independent from HUD scale.
    /// </summary>
    public static readonly CVarDef<float> StorageScale =
        CVarDef.Create("forge.ui.storage_scale", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
    #endregion

    #region Other
    /// <summary>
    ///     Controls if the connections queue is enabled
    ///     If enabled plyaers will be added to a queue instead of being kicked after SoftMaxPlayers is reached
    /// </summary>
    public static readonly CVarDef<bool> QueueEnabled =
        CVarDef.Create("queue.enabled", true, CVar.SERVERONLY);

    public static readonly CVarDef<float> StationAiSsdGracePeriod =
        CVarDef.Create("station_ai.ssd_grace_period", 300f, CVar.SERVERONLY);

    /// <summary>
    /// Client volume slider for boarding teleport countdown and arrival sounds.
    /// </summary>
    public static readonly CVarDef<float> BoardingTeleportVolume =
        CVarDef.Create("forge.boarding_teleport_volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Offer item.
    /// </summary>
    public static readonly CVarDef<bool> OfferModeIndicatorsPointShow =
        CVarDef.Create("hud.offer_mode_indicators_point_show", true, CVar.ARCHIVE | CVar.CLIENTONLY);
    #endregion

    #region Drone_Inner_Zone
    /// <summary>
    ///     Radius of the inner worldgen zone (meters from map origin) that procedural drones must flee.
    ///     Must stay in sync with MonoEmptyFallback distanceRange max in basic.yml (18000).
    /// </summary>
    public static readonly CVarDef<float> DroneInnerZoneRadius =
        CVarDef.Create("forge.drone.inner_zone_radius", 18000f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum seconds after flee starts before a procedural drone grid is deleted.
    /// </summary>
    public static readonly CVarDef<float> DroneInnerZoneFleeDeleteMin =
        CVarDef.Create("forge.drone.inner_zone_flee_delete_min", 10f, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum seconds after flee starts before a procedural drone grid is deleted.
    /// </summary>
    public static readonly CVarDef<float> DroneInnerZoneFleeDeleteMax =
        CVarDef.Create("forge.drone.inner_zone_flee_delete_max", 15f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between scans for procedural drones inside the inner worldgen zone.
    /// </summary>
    public static readonly CVarDef<float> DroneInnerZoneCheckInterval =
        CVarDef.Create("forge.drone.inner_zone_check_interval", 2f, CVar.SERVERONLY);
    #endregion
}
