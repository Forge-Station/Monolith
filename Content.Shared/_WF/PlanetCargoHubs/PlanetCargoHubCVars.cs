using Robust.Shared.Configuration;

namespace Content.Shared._WF.PlanetCargoHubs;

/// <summary>Planetside cargo hub settings.</summary>
[CVarDefs]
public sealed class PlanetCargoHubCVars
{
    /// <summary>
    /// The chance, per round, that each of cargo depots A and B is built on a planet instead of in space. At least one
    /// depot goes down whenever a world can take it; 0 turns planetside depots off.
    /// </summary>
    public static readonly CVarDef<float> Chance =
        CVarDef.Create("wf.planet_cargo_hub_chance", 0.35f, CVar.SERVERONLY);
}
