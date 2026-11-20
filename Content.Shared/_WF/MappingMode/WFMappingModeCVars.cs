using Robust.Shared.Configuration;

namespace Content.Shared._WF.MappingMode;

/// <summary>Settings for mapping mode, the server-wide shared mapping workshop.</summary>
[CVarDefs]
public sealed class WFMappingModeCVars
{
    /// <summary>Turns the server into a mapping workshop: no rounds, a mapping lobby, per-player sessions.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.mapping_mode.enabled", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Client: downloads go through the save dialog, one file at a time, instead of into the data folder. The sandbox
    /// only lets the client write outside its data folder through that dialog.
    /// </summary>
    public static readonly CVarDef<bool> DownloadSaveTo =
        CVarDef.Create("wf.mapping_mode.download_save_to", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Side of the square, in tiles and centred on the origin, that every level's tiles must lie in.</summary>
    public static readonly CVarDef<int> MapSize =
        CVarDef.Create("wf.mapping_mode.map_size", 256, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Default seconds between autosaves of a dirty session.</summary>
    public static readonly CVarDef<int> AutosaveInterval =
        CVarDef.Create("wf.mapping_mode.autosave_interval", 120, CVar.SERVERONLY);

    /// <summary>Shortest autosave interval a session owner can pick, in seconds.</summary>
    public static readonly CVarDef<int> AutosaveMinInterval =
        CVarDef.Create("wf.mapping_mode.autosave_min_interval", 60, CVar.SERVERONLY);

    /// <summary>Longest autosave interval a session owner can pick, in seconds.</summary>
    public static readonly CVarDef<int> AutosaveMaxInterval =
        CVarDef.Create("wf.mapping_mode.autosave_max_interval", 900, CVar.SERVERONLY);

    /// <summary>Versions kept per library map.</summary>
    public static readonly CVarDef<int> MaxVersions =
        CVarDef.Create("wf.mapping_mode.max_versions", 25, CVar.SERVERONLY);

    /// <summary>Maps per player library.</summary>
    public static readonly CVarDef<int> MaxMaps =
        CVarDef.Create("wf.mapping_mode.max_maps", 50, CVar.SERVERONLY);

    /// <summary>Bytes per player library.</summary>
    public static readonly CVarDef<long> MaxLibraryBytes =
        CVarDef.Create("wf.mapping_mode.max_library_bytes", 256L * 1024 * 1024, CVar.SERVERONLY);

    /// <summary>Members per session, absent ones included.</summary>
    public static readonly CVarDef<int> MaxMembers =
        CVarDef.Create("wf.mapping_mode.max_members", 8, CVar.SERVERONLY);

    /// <summary>Live sessions on the server.</summary>
    public static readonly CVarDef<int> MaxSessions =
        CVarDef.Create("wf.mapping_mode.max_sessions", 32, CVar.SERVERONLY);

    /// <summary>Z-levels per session.</summary>
    public static readonly CVarDef<int> MaxLevels =
        CVarDef.Create("wf.mapping_mode.max_levels", 3, CVar.SERVERONLY);

    /// <summary>Seconds a session with nobody present survives before it closes.</summary>
    public static readonly CVarDef<int> EmptySessionGrace =
        CVarDef.Create("wf.mapping_mode.empty_session_grace", 300, CVar.SERVERONLY);

    /// <summary>Grids each member adds to their session's grid cap.</summary>
    public static readonly CVarDef<int> GridsPerPlayer =
        CVarDef.Create("wf.mapping_mode.grids_per_player", 6, CVar.SERVERONLY);

    /// <summary>Grids per session, whatever the member count.</summary>
    public static readonly CVarDef<int> MaxGridsPerSession =
        CVarDef.Create("wf.mapping_mode.max_grids_per_session", 8, CVar.SERVERONLY);

    /// <summary>Entities on one tile.</summary>
    public static readonly CVarDef<int> MaxEntitiesPerTile =
        CVarDef.Create("wf.mapping_mode.max_entities_per_tile", 30, CVar.SERVERONLY);

    /// <summary>Decals on one tile.</summary>
    public static readonly CVarDef<int> MaxDecalsPerTile =
        CVarDef.Create("wf.mapping_mode.max_decals_per_tile", 16, CVar.SERVERONLY);

    /// <summary>Entities across all of a session's maps.</summary>
    public static readonly CVarDef<int> MaxEntities =
        CVarDef.Create("wf.mapping_mode.max_entities", 25000, CVar.SERVERONLY);

    /// <summary>Size of a file loaded as a reference grid; the default admits every ship and refuses stations.</summary>
    public static readonly CVarDef<long> MaxReferenceBytes =
        CVarDef.Create("wf.mapping_mode.max_reference_bytes", 1536L * 1024, CVar.SERVERONLY);

    /// <summary>Seconds after a reference load before anyone on the server may load another.</summary>
    public static readonly CVarDef<float> ReferenceCooldown =
        CVarDef.Create("wf.mapping_mode.reference_cooldown", 2f, CVar.SERVERONLY);

    /// <summary>
    /// Size of a map file a player may upload into their library; replicated so the client refuses early. Clamped
    /// to <see cref="WFMappingUploadAssembler.MaxFileBytes"/>.
    /// </summary>
    public static readonly CVarDef<long> MaxUploadBytes =
        CVarDef.Create("wf.mapping_mode.max_upload_bytes", 1536L * 1024, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Seconds after an upload starts before the same player may start another.</summary>
    public static readonly CVarDef<float> UploadCooldown =
        CVarDef.Create("wf.mapping_mode.upload_cooldown", 30f, CVar.SERVERONLY);

    /// <summary>Seconds the server holds an upload with unknown prototypes while the player decides.</summary>
    public static readonly CVarDef<float> UploadConfirmSeconds =
        CVarDef.Create("wf.mapping_mode.upload_confirm_seconds", 120f, CVar.SERVERONLY);

    /// <summary>Placement, tile and decal requests per second per mapper.</summary>
    public static readonly CVarDef<int> MaxEditsPerSecond =
        CVarDef.Create("wf.mapping_mode.max_edits_per_second", 60, CVar.SERVERONLY);

    /// <summary>Live sessions a player may own, empty ones waiting out their grace included.</summary>
    public static readonly CVarDef<int> MaxSessionsPerPlayer =
        CVarDef.Create("wf.mapping_mode.max_sessions_per_player", 1, CVar.SERVERONLY);

    /// <summary>Cells one mapping tools selection or area erase may cover.</summary>
    public static readonly CVarDef<int> MaxSelectionCells =
        CVarDef.Create("wf.mapping_mode.max_selection_cells", 4096, CVar.SERVERONLY);

    /// <summary>Entities, contents included, one copy, paste, delete, erase or undo step may touch.</summary>
    public static readonly CVarDef<int> MaxEditEntities =
        CVarDef.Create("wf.mapping_mode.max_edit_entities", 2000, CVar.SERVERONLY);

    /// <summary>Decals one copy or paste may carry.</summary>
    public static readonly CVarDef<int> MaxEditDecals =
        CVarDef.Create("wf.mapping_mode.max_edit_decals", 4096, CVar.SERVERONLY);

    /// <summary>Undo steps kept per mapper; the oldest go first.</summary>
    public static readonly CVarDef<int> MaxHistory =
        CVarDef.Create("wf.mapping_mode.max_history", 30, CVar.SERVERONLY);

    /// <summary>Estimated serialized bytes of a mapper's undo and redo steps; the oldest go first.</summary>
    public static readonly CVarDef<long> MaxHistoryBytes =
        CVarDef.Create("wf.mapping_mode.max_history_bytes", 16L * 1024 * 1024, CVar.SERVERONLY);

    /// <summary>
    /// Grid file loaded beside the ship on a live test's ground level, 60 tiles off, to fly to and dock at. Empty
    /// means space only.
    /// </summary>
    public static readonly CVarDef<string> TestRange =
        CVarDef.Create("wf.mapping_mode.test_range", "/Maps/_NF/POI/cargodepot.yml", CVar.SERVERONLY);

    /// <summary>Minutes a live test runs before everyone is sent back to mapping.</summary>
    public static readonly CVarDef<int> TestMaxMinutes =
        CVarDef.Create("wf.mapping_mode.test_max_minutes", 30, CVar.SERVERONLY);

    /// <summary>Seconds after a session's live test ends before it may start another.</summary>
    public static readonly CVarDef<float> TestCooldown =
        CVarDef.Create("wf.mapping_mode.test_cooldown", 10f, CVar.SERVERONLY);

    /// <summary>Seconds after any live test starts before the server builds another; a build copies and loads a session.</summary>
    public static readonly CVarDef<float> TestGlobalCooldown =
        CVarDef.Create("wf.mapping_mode.test_global_cooldown", 5f, CVar.SERVERONLY);

    /// <summary>Live tests that may run at once across every session.</summary>
    public static readonly CVarDef<int> TestMaxRunning =
        CVarDef.Create("wf.mapping_mode.test_max_running", 8, CVar.SERVERONLY);
}
