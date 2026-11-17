#nullable enable
using Content.Shared._Mono.CCVar;
using Content.Shared.CCVar;
using Robust.Shared;

namespace Content.IntegrationTests;

// Partial class containing test cvars
// This could probably be merged into the main file, but I'm keeping it separate to reduce
// conflicts for forks.
public static partial class PoolManager
{
    public static readonly (string cvar, string value)[] TestCvars =
    {
        // @formatter:off
        // ES START
        (CCVars.ExcitedGroups.Name,     "true"),
        // ES END
        (CCVars.DatabaseSynchronous.Name,     "true"),
        (CCVars.DatabaseSqliteDelay.Name,     "0"),
        (CCVars.HolidaysEnabled.Name,         "false"),
        (CCVars.GameMap.Name,                 TestMap),
        (CCVars.AdminLogsQueueSendDelay.Name, "0"),
        (CCVars.NPCMaxUpdates.Name,           "999999"),
        (CCVars.GameRoleTimers.Name,          "false"),
        (CCVars.GameRoleWhitelist.Name,       "false"),
        (CCVars.GridFill.Name,                "false"),
        (CCVars.PreloadGrids.Name,            "false"),
        (CCVars.ArrivalsShuttles.Name,        "false"),
        (CCVars.EmergencyShuttleEnabled.Name, "false"),
        (CCVars.ProcgenPreload.Name,          "false"),
        (CCVars.WorldgenEnabled.Name,         "false"),
        (CCVars.GatewayGeneratorEnabled.Name, "false"),
        (CCVars.GameDummyTicker.Name, "true"),
        (CCVars.GameLobbyEnabled.Name, "false"),
        (CCVars.ConfigPresetDevelopment.Name, "false"),
        (CCVars.AdminLogsEnabled.Name, "false"),
        (CCVars.AutosaveEnabled.Name, "false"),
        (CCVars.InteractionRateLimitCount.Name, "9999999"),
        (CCVars.InteractionRateLimitPeriod.Name, "0.1"),
        (CCVars.MovementMobPushing.Name, "false"),
        (CCVars.GameLobbyDefaultPreset.Name, "secret"), // Frontier: Adventure takes ages, default to secret
        (CCVars.StaticStorageUI.Name, "true"),// Frontier: causes storage test failures
        ("wf.encounters.simulate", "false"), // WOLFGATE(Encounters): tests spawn real ships unless they turn simulation on
        ("wf.encounters.vote", "false"), // WOLFGATE(Encounters): a round-restart vote throws on a dummy session's join
        ("wf.newbie.welcome", "false"), // WOLFGATE(Newbie): welcome popups would open over other tests' windows
        (CCVars.StorageLimit.Name, "1"),// Frontier: test failures with multiple storage
        (MonoCVars.AreaEchoEnabled.Name, "false"),// headless client has no EFX slot; echo asserts and fails preset tests
        // Headless client does not draw. This skips the loose-texture pass. RSI decode still runs
        // in the engine; SpriteDecodeCleanupSystem drops that pool afterwards.
        (CVars.ResTexturePreloadingEnabled.Name, "false")
    };
}
