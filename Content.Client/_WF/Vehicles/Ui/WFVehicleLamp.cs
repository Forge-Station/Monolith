namespace Content.Client._WF.Vehicles.Ui;

/// <summary>When the dashboard's fuel warning shows.</summary>
public static class WFVehicleLamp
{
    /// <summary>Lit under a fifth of a tank with the power on, flashing under a twentieth.</summary>
    public static bool FuelWarningLit(float percent, bool powered, double seconds)
    {
        return powered && percent < 20 && (percent >= 5 || seconds % 1 < 0.5);
    }
}
