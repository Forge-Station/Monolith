using Robust.Shared.Configuration;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>Remade settings for where nothing may grow.</summary>
[CVarDefs]
public sealed class WFRemadeSanctuaryCVars
{
    /// <summary>
    /// Metres round a main station, edge to edge, in which a Monolith can't be drawn on and corruption won't grow. 0 means
    /// only the station's own grid is clean.
    /// </summary>
    public static readonly CVarDef<float> Radius =
        CVarDef.Create("remade.sanctuary_radius", 500f, CVar.SERVERONLY);

    /// <summary>Comma-separated station ids (a grid's <c>BecomesStation</c> id) that count as sanctuaries. By default only CC.</summary>
    public static readonly CVarDef<string> Stations =
        CVarDef.Create("remade.sanctuary_stations", "Frontier", CVar.SERVERONLY);
}
