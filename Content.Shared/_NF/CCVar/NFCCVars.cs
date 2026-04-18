using Robust.Shared.Configuration;

namespace Content.Shared._NF.CCVar;

[CVarDefs]
public sealed class NFCCVars
{
    /*
     *  Respawn
    */
    /// <summary>
    /// Whether or not respawning is enabled.
    /// </summary>
    public static readonly CVarDef<bool> RespawnEnabled =
        CVarDef.Create("nf14.respawn.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Respawn time, how long the player has to wait in seconds after going into cryosleep. Should be small, misclicks happen.
    /// </summary>
    public static readonly CVarDef<float> RespawnCryoFirstTime =
        CVarDef.Create("nf14.respawn.cryo_first_time", 20.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Respawn time, how long the player has to wait in seconds after death, or on subsequent cryo attempts.
    /// </summary>
    public static readonly CVarDef<float> RespawnTime =
        CVarDef.Create("nf14.respawn.time", 1200.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Whether or not returning from cryosleep is enabled.
    /// </summary>
    public static readonly CVarDef<bool> CryoReturnEnabled =
        CVarDef.Create("nf14.uncryo.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// The time in seconds after which a cryosleeping body is considered expired and can be deleted from the storage map.
    /// </summary>
    public static readonly CVarDef<float> CryoExpirationTime =
        CVarDef.Create("nf14.uncryo.maxtime", 180 * 60f, CVar.SERVER | CVar.REPLICATED);

    /*
     *  Public Transit
     */
    /// <summary>
    /// Whether public transit is enabled.
    /// </summary>
    public static readonly CVarDef<bool> PublicTransit =
        CVarDef.Create("nf14.publictransit.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// The map to use for the public bus.
	/// Mono: Changed to _Mono busdart.yml
    /// </summary>
    public static readonly CVarDef<string> PublicTransitBusMap =
        CVarDef.Create("nf14.publictransit.bus_map", "/Maps/_Mono/Shuttles/Bus/busdart.yml", CVar.SERVERONLY);

    /// <summary>
    /// The amount of time the bus waits at a station.
    /// </summary>
    public static readonly CVarDef<float> PublicTransitWaitTime =
        CVarDef.Create("nf14.publictransit.wait_time", 40f, CVar.SERVERONLY);

    /// <summary>
    /// The amount of time the bus flies through FTL space.
    /// This does nothing because the transit system is bugged in our favor (instant travel)
    /// </summary>
    public static readonly CVarDef<float> PublicTransitFlyTime =
        CVarDef.Create("nf14.publictransit.fly_time", 15f, CVar.SERVERONLY);

    /*
     *  World Gen
     */
    /// <summary>
    /// The number of Trade Stations to spawn in every round
    /// </summary>
    public static readonly CVarDef<int> MarketStations =
        CVarDef.Create("nf14.worldgen.market_stations", 1, CVar.SERVERONLY);

    /// <summary>
    /// The number of Cargo Depots to spawn in every round
    /// </summary>
    public static readonly CVarDef<int> CargoDepots =
        CVarDef.Create("nf14.worldgen.cargo_depots", 4, CVar.SERVERONLY);

    /// <summary>
    /// The number of Optional Points Of Interest to spawn in every round
    /// </summary>
    public static readonly CVarDef<int> OptionalStations =
        CVarDef.Create("nf14.worldgen.optional_stations", 6, CVar.SERVERONLY);

    /// <summary>
    /// The multiplier to add to distance spawning calculations for a smidge of server setting variance
    /// </summary>
    public static readonly CVarDef<float> POIDistanceModifier =
        CVarDef.Create("nf14.worldgen.distance_modifier", 1f, CVar.SERVERONLY);

    /// <summary>
    /// The rough minimum distance between POIs in meters.
    /// </summary>
    public static readonly CVarDef<float> MinPOIDistance =
        CVarDef.Create("nf14.worldgen.min_poi_distance", 400f, CVar.SERVERONLY);

    /// <summary>
    /// The maximum number of times to retry POI placement during world generation.
    /// </summary>
    public static readonly CVarDef<int> POIPlacementRetries =
        CVarDef.Create("nf14.worldgen.poi_placement_retries", 10, CVar.SERVERONLY);

    /*
    * Shipyard
    */
    /// <summary>
    /// Whether the Shipyard is enabled.
    /// </summary>
    public static readonly CVarDef<bool> Shipyard =
        CVarDef.Create("shuttle.shipyard", true, CVar.SERVERONLY);

    /// <summary>
    /// Base sell rate (multiplier: 0.85 = 85%)
    /// </summary>
    public static readonly CVarDef<float> ShipyardSellRate =
        CVarDef.Create("shuttle.shipyard_base_sell_rate", 0.85f, CVar.SERVERONLY);

    /// <summary>
    /// Enables automatic cleanup of inactive purchased shipyard shuttles.
    /// </summary>
    public static readonly CVarDef<bool> ShipyardAutoDeleteEnabled =
        CVarDef.Create("shuttle.shipyard_auto_delete_enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Owner must be offline for at least this many seconds before deletion can be armed.
    /// </summary>
    public static readonly CVarDef<float> ShipyardAutoDeleteOwnerOfflineSeconds =
        CVarDef.Create("shuttle.shipyard_auto_delete_owner_offline_seconds", 3600f, CVar.SERVERONLY);

    /// <summary>
    /// No players must be aboard for at least this many seconds before deletion can be armed.
    /// </summary>
    public static readonly CVarDef<float> ShipyardAutoDeleteInactiveSeconds =
        CVarDef.Create("shuttle.shipyard_auto_delete_inactive_seconds", 7200f, CVar.SERVERONLY);

    /// <summary>
    /// Safety grace period after arming deletion. Any player activity cancels it.
    /// </summary>
    public static readonly CVarDef<float> ShipyardAutoDeleteGraceSeconds =
        CVarDef.Create("shuttle.shipyard_auto_delete_grace_seconds", 900f, CVar.SERVERONLY);

    /// <summary>
    /// Interval in seconds between ownership cleanup checks.
    /// </summary>
    public static readonly CVarDef<float> ShipyardAutoDeleteCheckIntervalSeconds =
        CVarDef.Create("shuttle.shipyard_auto_delete_check_interval_seconds", 60f, CVar.SERVERONLY);

    // Forge-Change-start: crypto market tuning.
    public static readonly CVarDef<float> CryptoBasePrice =
        CVarDef.Create("forge.crypto.base_price", 10000f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoMinPriceMultiplier =
        CVarDef.Create("forge.crypto.min_price_multiplier", 0.2f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoMaxPriceMultiplier =
        CVarDef.Create("forge.crypto.max_price_multiplier", 2.5f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoAbsoluteMaxPrice =
        CVarDef.Create("forge.crypto.absolute_max_price", 50000f, CVar.SERVERONLY); // Forge-Change

    public static readonly CVarDef<float> CryptoPassiveGrowthRate =
        CVarDef.Create("forge.crypto.passive_growth_rate", 0.005f, CVar.SERVERONLY);

    /// <summary>
    /// Each market tick, probability (0–1) of an extra passive price dip (volatility / correction).
    /// </summary>
    public static readonly CVarDef<float> CryptoPassiveDropChance =
        CVarDef.Create("forge.crypto.passive_drop_chance", 0.18f, CVar.SERVERONLY);

    /// <summary>
    /// When a passive dip triggers, subtract BasePrice * this * GrowthMultiplier (same scale as passive growth).
    /// </summary>
    public static readonly CVarDef<float> CryptoPassiveDropStrength =
        CVarDef.Create("forge.crypto.passive_drop_strength", 0.006f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoBaseDrop =
        CVarDef.Create("forge.crypto.base_drop", 0.06f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoVolumeDropFactor =
        CVarDef.Create("forge.crypto.volume_drop_factor", 0.025f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoMomentumDropFactor =
        CVarDef.Create("forge.crypto.momentum_drop_factor", 0.015f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoMinDropMultiplier =
        CVarDef.Create("forge.crypto.min_drop_multiplier", 0.35f, CVar.SERVERONLY);

    /// <summary>
    /// Per-unit price pressure when buying coins (mirrors base_drop for sales).
    /// </summary>
    public static readonly CVarDef<float> CryptoBaseRise =
        CVarDef.Create("forge.crypto.base_rise", 0.055f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoVolumeRiseFactor =
        CVarDef.Create("forge.crypto.volume_rise_factor", 0.022f, CVar.SERVERONLY);

    public static readonly CVarDef<float> CryptoMomentumRiseFactor =
        CVarDef.Create("forge.crypto.momentum_rise_factor", 0.014f, CVar.SERVERONLY);

    /// <summary>
    /// Max multiplier applied to price per bought unit (cap on 1 + riseFraction).
    /// </summary>
    public static readonly CVarDef<float> CryptoMaxRiseMultiplier =
        CVarDef.Create("forge.crypto.max_rise_multiplier", 1.14f, CVar.SERVERONLY);
    public static readonly CVarDef<float> CryptoVolumeDecayPerSecond =
        CVarDef.Create("forge.crypto.volume_decay_per_second", 0.92f, CVar.SERVERONLY);

    public static readonly CVarDef<int> CryptoHistoryLength =
        CVarDef.Create("forge.crypto.history_length", 24, CVar.SERVERONLY);
    // Forge-Change-end: crypto market tuning.
    /*
     * Salvage
     */
    /// <summary>
    /// The maximum number of shuttles able to go on expedition at once.
    /// </summary>
    public static readonly CVarDef<int> SalvageExpeditionMaxActive =
        CVarDef.Create("nf14.salvage.expedition_max_active", 15, CVar.REPLICATED);

    /// <summary>
    /// Cooldown for failed missions.
    /// </summary>
    public static readonly CVarDef<float> SalvageExpeditionFailedCooldown =
        CVarDef.Create("salvage.expedition_failed_cooldown", 450f, CVar.REPLICATED); //Mono 1200->450

    /// <summary>
    /// Whether salvage expedition rewards is enabled.
    /// </summary>
    public static readonly CVarDef<bool> SalvageExpeditionRewardsEnabled =
        CVarDef.Create("nf14.salvage.expedition_rewards_enabled", false, CVar.REPLICATED);

    /*
     * Smuggling
     */
    /// <summary>
    /// The maximum number of smuggling drop pods to be out at once.
    /// Taking another dead drop note will cause the oldest one to be destroyed.
    /// </summary>
    public static readonly CVarDef<int> SmugglingMaxSimultaneousPods =
        CVarDef.Create("nf14.smuggling.max_simultaneous_pods", 5, CVar.REPLICATED);
    /// <summary>
    /// The maximum number of dead drops (places to get smuggling notes) to place at once.
    /// </summary>
    public static readonly CVarDef<int> SmugglingMaxDeadDrops =
        CVarDef.Create("nf14.smuggling.max_sector_dead_drops", 10, CVar.REPLICATED);
    /// <summary>
    /// The minimum number of FMCs to spawn for anti-smuggling work.
    /// </summary>
    public static readonly CVarDef<int> SmugglingMinFMCPayout =
        CVarDef.Create("nf14.smuggling.min_fmc_payout", 1, CVar.REPLICATED);
    /// <summary>
    /// The shortest time to wait before a dead drop spawns a new smuggling note.
    /// </summary>
    public static readonly CVarDef<int> DeadDropMinTimeout =
        CVarDef.Create("nf14.smuggling.min_timeout", 900, CVar.REPLICATED);
    /// <summary>
    /// The longest time to wait before a dead drop spawns a new smuggling note.
    /// </summary>
    public static readonly CVarDef<int> DeadDropMaxTimeout =
        CVarDef.Create("nf14.smuggling.max_timeout", 5400, CVar.REPLICATED);
    /// <summary>
    /// The shortest distance that a smuggling pod will spawn away from Colonial Outpost.
    /// </summary>
    public static readonly CVarDef<int> DeadDropMinDistance =
        CVarDef.Create("nf14.smuggling.min_distance", 6500, CVar.REPLICATED);
    /// <summary>
    /// The longest distance that a smuggling pod will spawn away from Colonial Outpost.
    /// </summary>
    public static readonly CVarDef<int> DeadDropMaxDistance =
        CVarDef.Create("nf14.smuggling.max_distance", 8000, CVar.REPLICATED);
    /// <summary>
    /// The smallest number of dead drop hints (paper clues to dead drop locations) at round start.
    /// </summary>
    public static readonly CVarDef<int> DeadDropMinHints =
        CVarDef.Create("nf14.smuggling.min_hints", 0, CVar.REPLICATED); // Used with BasicDeadDropHintVariationPass
    /// <summary>
    /// The largest number of dead drop hints (paper clues to dead drop locations) at round start.
    /// </summary>
    public static readonly CVarDef<int> DeadDropMaxHints =
        CVarDef.Create("nf14.smuggling.max_hints", 0, CVar.REPLICATED); // Used with BasicDeadDropHintVariationPass

    /*
    * Discord
    */
    /// <summary>
    ///     URL of the Discord webhook which will send round status notifications.
    /// </summary>
    public static readonly CVarDef<string> DiscordRoundWebhook =
        CVarDef.Create("discord.round_webhook", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Discord ID of role which will be pinged on new round start message.
    /// </summary>
    public static readonly CVarDef<string> DiscordRoundRoleId =
        CVarDef.Create("discord.round_roleid", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Send notifications only about a new round begins.
    /// </summary>
    public static readonly CVarDef<bool> DiscordRoundStartOnly =
        CVarDef.Create("discord.round_start_only", false, CVar.SERVERONLY);

    /// <summary>
    /// URL of the Discord webhook which will relay all round end messages.
    /// </summary>
    public static readonly CVarDef<string> DiscordLeaderboardWebhook =
        CVarDef.Create("discord.leaderboard_webhook", string.Empty, CVar.SERVERONLY);

    /*
    * Auth
    */
    public static readonly CVarDef<string> ServerAuthList =
        CVarDef.Create("frontier.auth_servers", "", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    public static readonly CVarDef<bool> AllowMultiConnect =
        CVarDef.Create("frontier.allow_multi_connect", true, CVar.CONFIDENTIAL | CVar.SERVERONLY);

    /*
     * Events
     */
    /// <summary>
    ///     A scale factor applied to a grid's bounds when trying to find a spot to randomly generate a crate for bluespace events.
    /// </summary>
    public static readonly CVarDef<float> CrateGenerationGridBoundsScale =
        CVarDef.Create("nf14.events.crate_generation_grid_bounds_scale", 0.6f, CVar.SERVERONLY);
}
