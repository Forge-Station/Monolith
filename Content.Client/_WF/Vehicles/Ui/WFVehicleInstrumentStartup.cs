namespace Content.Client._WF.Vehicles.Ui;

/// <summary>Smooth ignition self-test: zero to full scale, back to zero, then into live telemetry.</summary>
public static class WFVehicleInstrumentStartup
{
    public const float Duration = 2.2f;

    public static float Reading(float actual, float maximum, float elapsed)
    {
        static float Ease(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return value * value * (3f - 2f * value);
        }
        if (elapsed < 0.8f)
            return maximum * Ease(elapsed / 0.8f);
        if (elapsed < 1.6f)
            return maximum * (1f - Ease((elapsed - 0.8f) / 0.8f));
        return actual * Ease((elapsed - 1.6f) / 0.6f);
    }
}
