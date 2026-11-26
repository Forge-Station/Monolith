using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>A machine's positronic core temperature; also the machine marker shared code reads.</summary>
// Ensured on every mechanical wound host by the server's overheat system. Over wolfmed.ipc_core_heat_k the core
// loses health and the chassis is in thermal shutdown, Dying with cause CoreHeat, until the core is back under
// wolfmed.ipc_core_heat_wake_k.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedCoreHeatComponent : Component
{
    /// <summary>Core temperature in kelvin. Dirtied when it moves by a whole kelvin.</summary>
    [AutoNetworkedField]
    public float CoreTemperature = 310.15f;

    /// <summary>Core or chassis over wolfmed.ipc_core_heat_warn_k: "smoking, too hot to touch".</summary>
    [AutoNetworkedField]
    public bool Hot;

    /// <summary>Thermal shutdown: the core got past its line and has not cooled back under the wake line.</summary>
    [AutoNetworkedField]
    public bool ThermalShutdown;

    /// <summary>Server: when this thermal shutdown began.</summary>
    [ViewVariables]
    public TimeSpan ThermalShutdownStart;

    /// <summary>Server: the chassis temperature at the last tick, for the analyzer.</summary>
    [ViewVariables]
    public float ChassisTemperature = 310.15f;
}
