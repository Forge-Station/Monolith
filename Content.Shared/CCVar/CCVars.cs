using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
/// Contains all the CVars used by content.
/// </summary>
/// <remarks>
/// NOTICE FOR FORKS: Put your own CVars in a separate file with a different [CVarDefs] attribute. RT will automatically pick up on it.
/// </remarks>
[CVarDefs]
public sealed partial class CCVars : CVars
{
    // Only debug stuff lives here.

#if DEBUG
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<string> DebugTestCVar =
        CVarDef.Create("debug.test_cvar", "default", CVar.SERVER);

    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> DebugTestCVar2 =
        CVarDef.Create("debug.test_cvar2", 123.42069f, CVar.SERVER);
#endif

    /// <summary>
    /// A simple toggle to test <c>OptionsVisualizerComponent</c>.
    /// </summary>
    public static readonly CVarDef<bool> DebugOptionVisualizerTest =
        CVarDef.Create("debug.option_visualizer_test", false, CVar.CLIENTONLY);

    /// <summary>
    /// Set to true to disable parallel processing in the pow3r solver.
    /// </summary>
    public static readonly CVarDef<bool> DebugPow3rDisableParallel =
        CVarDef.Create("debug.pow3r_disable_parallel", false, CVar.SERVERONLY);

    #region Surgery

    public static readonly CVarDef<bool> CanOperateOnSelf =
        CVarDef.Create("surgery.can_operate_on_self", true, CVar.SERVERONLY);

    #endregion

    /// <summary>
    ///     Should the player automatically get up after being knocked down
    /// </summary>
    public static readonly CVarDef<bool> AutoGetUp =
        CVarDef.Create("white.auto_get_up", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED); // WD EDIT

    public static readonly CVarDef<bool> LogInChat =
        CVarDef.Create("white.log_in_chat", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED); // WD EDIT

    public static readonly CVarDef<bool> CoalesceIdenticalMessages =
        CVarDef.Create("white.coalesce_identical_messages", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED); // WD EDIT

    public static readonly CVarDef<bool> DetailedExamine =
        CVarDef.Create("misc.detailed_examine", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED); // Goobstation Change

    /// <summary>
    ///     Goobstation: The amount of time between NPC Silicons draining their battery in seconds.
    /// </summary>
    public static readonly CVarDef<float> SiliconNpcUpdateTime =
        CVarDef.Create("silicon.npcupdatetime", 1.5f, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum decals stored in one 32×32 world chunk; oldest (lowest id) are removed when exceeded.
    ///     Set to 0 to disable the limit.
    /// </summary>
    public static readonly CVarDef<int> DecalsMaxPerChunk =
        CVarDef.Create("decals.max_per_chunk", 512, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum decals drawn per 1×1 tile cell on the client; extras with lower Z-index are skipped first.
    /// </summary>
    public static readonly CVarDef<int> DecalsMaxPerTile =
        CVarDef.Create("decals.client_max_per_tile", 64, CVar.CLIENTONLY);

    /// <summary>
    ///     Drop redundant draws when several decals share the same position, prototype, rotation, color and Z-index
    ///     (keeps highest id / newest). Helps accidental duplicates; cheap for large crayon fills.
    /// </summary>
    public static readonly CVarDef<bool> DecalsClientDeduplicateIdentical =
        CVarDef.Create("decals.client_deduplicate_identical", true, CVar.CLIENTONLY);
}
