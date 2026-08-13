// hud.offer_mode_indicators_point_show -> Port From SS14 Corvax-Next

using Robust.Shared.Configuration;

namespace Content.Shared._Forge;

/// <summary>
///     Corvax modules console variables
/// </summary>
[CVarDefs]
// ReSharper disable once InconsistentNaming
public sealed class ForgeVars
{
    /// <summary>
    /// Offer item.
    /// </summary>
    public static readonly CVarDef<bool> OfferModeIndicatorsPointShow =
        CVarDef.Create("hud.offer_mode_indicators_point_show", true, CVar.ARCHIVE | CVar.CLIENTONLY);

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

    /// <summary>
    /// Client volume slider for boarding teleport countdown and arrival sounds.
    /// </summary>
    public static readonly CVarDef<float> BoardingTeleportVolume =
        CVarDef.Create("forge.boarding_teleport_volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Selected company PDA UI palette prototype id. Colored palettes require sponsor.
    /// </summary>
    public static readonly CVarDef<string> CompanyUiPalette =
        CVarDef.Create("forge.company_ui_palette", "CompanyUiGrey", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> DiscordApiUrl =
        CVarDef.Create("jerry.discord_api_url", "", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    public static readonly CVarDef<bool> DiscordAuthEnabled =
        CVarDef.Create("jerry.discord_auth_enabled", false, CVar.CONFIDENTIAL | CVar.SERVERONLY);

    public static readonly CVarDef<string> DiscordGuildID =
        CVarDef.Create("jerry.discord_guildId", "1222332535628103750", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    public static readonly CVarDef<string> ApiKey =
        CVarDef.Create("jerry.discord_apikey", "", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    /**
     * TTS (Text-To-Speech)
     */

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

    /// <summary>
    ///     Controls if the connections queue is enabled
    ///     If enabled plyaers will be added to a queue instead of being kicked after SoftMaxPlayers is reached
    /// </summary>
    public static readonly CVarDef<bool> QueueEnabled =
        CVarDef.Create("queue.enabled", true, CVar.SERVERONLY); // Forge-Change

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

    /// <summary>
    /// Enables Forge world and shuttle persistence.
    /// </summary>
    public static readonly CVarDef<bool> PersistenceEnabled =
        CVarDef.Create("forge.persistence.enabled", true, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Days a persisted world (POIs, outposts, and their contents) lasts before a full wipe.
    /// Server restarts during this window keep the same world.
    /// </summary>
    public static readonly CVarDef<int> PersistenceCycleDays =
        CVarDef.Create("forge.persistence.cycle_days", 7, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// BSS network used to create extra sector maps and linear warp gates.
    /// </summary>
    public static readonly CVarDef<string> BssNetwork =
        CVarDef.Create("forge.bss.network", "ForgeBssNetwork", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Minimum world-space distance from the map origin at which BSS gates are placed.
    /// </summary>
    public static readonly CVarDef<float> BssGateDistance =
        CVarDef.Create("forge.bss.gate_distance", 5000f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Maximum world-space distance from the map origin at which BSS gates are placed.
    /// Each gate is spawned at a random distance between min and max.
    /// </summary>
    public static readonly CVarDef<float> BssGateDistanceMax =
        CVarDef.Create("forge.bss.gate_distance_max", 10000f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Root directory in server user data used by Forge persistence.
    /// </summary>
    public static readonly CVarDef<string> PersistenceRoot =
        CVarDef.Create("forge.persistence.root", "/Persisted", CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> HangarEnabled =
        CVarDef.Create("forge.hangar.enabled", true, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> HangarMaxSlots =
        CVarDef.Create("forge.hangar.max_slots", 2, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> HangarRoundEndWarning =
        CVarDef.Create("forge.hangar.round_end_warning", true, CVar.SERVERONLY | CVar.ARCHIVE);
}
