using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for Wolfgate planets.
/// </summary>
[CVarDefs]
public sealed class PlanetCVars
{
    /// <summary>
    /// Master switch for Wolfgate planet networks: orbit layers, surfaces and the orbit FTL jumps.
    /// </summary>
    public static readonly CVarDef<bool> PlanetNetworks =
        CVarDef.Create("wf.planet_networks", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether planet ground gets an atmosphere, so rooms built on it hold their own air. Bare ground keeps the
    /// planet's air either way. Applies to networks built after it is set.
    /// </summary>
    public static readonly CVarDef<bool> TerrainAtmosphere =
        CVarDef.Create("wf.planet_terrain_atmos", true, CVar.SERVERONLY);

    /// <summary>
    /// Milliseconds a pass, ten a second, may spend loading the far part of planet load areas. The two chunks round
    /// each loader and the ground under hulls load at once regardless, and each layer loads one 16-tile block a pass
    /// whatever it costs. Zero loads everything the pass it comes into range, as upstream does.
    /// </summary>
    public static readonly CVarDef<float> TerrainLoadBudget =
        CVarDef.Create("wf.planet_terrain_load_budget", 4f, CVar.SERVERONLY);

    /// <summary>
    /// Whether planet terrain nobody is near is unloaded. Off, upstream's unloader is all there is, and it lets go of
    /// almost nothing.
    /// </summary>
    public static readonly CVarDef<bool> TerrainUnload =
        CVarDef.Create("wf.planet_terrain_unload", true, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a chunk must sit outside every loaded area, hull and build before it is unloaded. Long enough that the
    /// way back from a short trip is still there: loading it again is the dear part, and is done all at once.
    /// </summary>
    public static readonly CVarDef<float> TerrainUnloadIdle =
        CVarDef.Create("wf.planet_terrain_unload_idle", 180f, CVar.SERVERONLY);

    /// <summary>
    /// Milliseconds one unload pass may take, ten passes a second, one planet layer a pass. A pass always unloads one
    /// chunk, so a dense cavern chunk can run past it.
    /// </summary>
    public static readonly CVarDef<float> TerrainUnloadBudget =
        CVarDef.Create("wf.planet_terrain_unload_budget", 3f, CVar.SERVERONLY);
}
