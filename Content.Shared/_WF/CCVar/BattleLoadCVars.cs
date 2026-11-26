using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings that keep big ship fights cheap for the server.</summary>
[CVarDefs]
public sealed class BattleLoadCVars
{
    /// <summary>Whether bullet-sized explosions skip the explosion queue and hit their tiles directly.</summary>
    public static readonly CVarDef<bool> MicroExplosions =
        CVarDef.Create("wf.battle_load.micro_explosions", true, CVar.SERVERONLY);

    /// <summary>The most total intensity an explosion can have to take the direct path.</summary>
    public static readonly CVarDef<float> MicroMaxTotal =
        CVarDef.Create("wf.battle_load.micro_max_total", 10f, CVar.SERVERONLY);

    /// <summary>The most per-tile intensity an explosion can have to take the direct path.</summary>
    public static readonly CVarDef<float> MicroMaxTile =
        CVarDef.Create("wf.battle_load.micro_max_tile", 2f, CVar.SERVERONLY);

    /// <summary>How far apart, in metres, two queued explosions on one grid can be and still merge into one.</summary>
    public static readonly CVarDef<float> MergeDistance =
        CVarDef.Create("wf.battle_load.merge_distance", 3f, CVar.SERVERONLY);

    /// <summary>The most total intensity a merged explosion can reach through the wider merge.</summary>
    public static readonly CVarDef<float> MergeMaxTotal =
        CVarDef.Create("wf.battle_load.merge_max_total", 150f, CVar.SERVERONLY);

    /// <summary>Grids split off with at most this many tiles and nobody aboard are deleted. 0 turns it off.</summary>
    public static readonly CVarDef<int> FragmentMaxTiles =
        CVarDef.Create("wf.battle_load.fragment_max_tiles", 4, CVar.SERVERONLY);

    /// <summary>Explosions within <see cref="HotRadius"/> over <see cref="HotWindow"/> that make a spot a hot battle.</summary>
    public static readonly CVarDef<int> HotExplosions =
        CVarDef.Create("wf.battle_load.hot_explosions", 150, CVar.SERVERONLY);

    /// <summary>The window, in seconds, explosions are counted over.</summary>
    public static readonly CVarDef<float> HotWindow =
        CVarDef.Create("wf.battle_load.hot_window", 60f, CVar.SERVERONLY);

    /// <summary>How near, in metres, a spawn has to be to a hot battle to be held back.</summary>
    public static readonly CVarDef<float> HotRadius =
        CVarDef.Create("wf.battle_load.hot_radius", 2000f, CVar.SERVERONLY);
}
