using Robust.Shared.Configuration;

namespace Content.Shared._WF.ShipSquatters;

/// <summary>Settings for the squatters who move into unguarded landed ships.</summary>
[CVarDefs]
public sealed class WFShipSquatterCVars
{
    /// <summary>Whether unguarded landed ships ever get a squatter.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.ship_squatters", true, CVar.SERVERONLY);

    /// <summary>Minutes a landed ship must go unguarded before a squatter may move in.</summary>
    public static readonly CVarDef<float> IdleMinutes =
        CVarDef.Create("wf.ship_squatters_idle_minutes", 20f, CVar.SERVERONLY);

    /// <summary>Seconds between rolls for each ship that has been idle long enough.</summary>
    public static readonly CVarDef<float> RollSeconds =
        CVarDef.Create("wf.ship_squatters_roll_seconds", 60f, CVar.SERVERONLY);

    /// <summary>Chance per roll that a squatter moves in. At 2% an hour away is a coin flip, half an hour is one in five.</summary>
    public static readonly CVarDef<float> Chance =
        CVarDef.Create("wf.ship_squatters_chance", 0.02f, CVar.SERVERONLY);

    /// <summary>Most squatters living on ships of one world at a time.</summary>
    public static readonly CVarDef<int> Cap =
        CVarDef.Create("wf.ship_squatters_cap", 3, CVar.SERVERONLY);

    /// <summary>Tiles from a ship a living player or NPC crewman still counts as guarding it.</summary>
    public static readonly CVarDef<float> GuardRange =
        CVarDef.Create("wf.ship_squatters_guard_range", 32f, CVar.SERVERONLY);
}
