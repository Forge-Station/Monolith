namespace Content.Shared._WF.MappingTools;

/// <summary>
/// The atmos devices Pipe mode's device box places, by their primary-layer prototype. The server places nothing
/// else through a device request; other layers come from each prototype's own layer data.
/// </summary>
public static class MappingToolsPipeDevices
{
    public static readonly string[] Ids =
    {
        "GasVentPump",
        "GasPassiveVent",
        "GasVentScrubber",
        "GasDualPortVentPump",
        "GasPort",
        "GasOutletInjector",
        "GasPressurePump",
        "GasVolumePump",
        "GasPassiveGate",
        "GasPressureRegulator",
        "GasValve",
        "SignalControlledValve",
        "PressureControlledValve",
        "GasFilter",
        "GasFilterFlipped",
        "GasMixer",
        "GasMixerFlipped",
        "GasThermoMachineHeater",
        "GasThermoMachineFreezer",
        "HeatExchanger",
        "HeatExchangerBend",
        "GasPipeSensor",
        "GasPipeHalf",
    };

    /// <summary>Whether a prototype id is one of the devices.</summary>
    public static bool Contains(string? id)
    {
        return id != null && Array.IndexOf(Ids, id) >= 0;
    }
}
