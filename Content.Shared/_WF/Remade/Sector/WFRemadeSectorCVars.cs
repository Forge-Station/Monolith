using Robust.Shared.Configuration;

namespace Content.Shared._WF.Remade.Sector;

/// <summary>Settings for the Remade in the open sector.</summary>
[CVarDefs]
public sealed class WFRemadeSectorCVars
{
    /// <summary>Whether the discovery rule places Scions and pirates go after a Scion's carrier. Admins can always spawn one.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.remade.sector.enabled", true, CVar.SERVERONLY);

    /// <summary>The most Scions the sector holds before no new one is placed.</summary>
    public static readonly CVarDef<int> MaxScions =
        CVarDef.Create("wf.remade.sector.max_scions", 3, CVar.SERVERONLY);

    /// <summary>Seconds between pirate hunts sent after a Scion's carrier. Zero for none.</summary>
    public static readonly CVarDef<float> HuntInterval =
        CVarDef.Create("wf.remade.sector.hunt_interval", 1200f, CVar.SERVERONLY);

    /// <summary>Chance, per hunt interval, that pirates come for a visible Scion's carrier.</summary>
    public static readonly CVarDef<float> HuntChance =
        CVarDef.Create("wf.remade.sector.hunt_chance", 0.5f, CVar.SERVERONLY);
}
