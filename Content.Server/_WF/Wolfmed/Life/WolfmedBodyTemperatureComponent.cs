namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// M5 (plan §3.10): a wound host's core temperature, its fire grace and the cold and heat inputs consciousness reads.
/// Added by <see cref="WolfmedBodyTemperatureSystem"/> on its first tick.
/// </summary>
[RegisterComponent, Access(typeof(WolfmedBodyTemperatureSystem))]
public sealed partial class WolfmedBodyTemperatureComponent : Component
{
    /// <summary>Core temperature in kelvin. NaN until the first tick takes it from the surface.</summary>
    [ViewVariables]
    public float Core = float.NaN;

    /// <summary>The surface temperature at the last tick.</summary>
    [ViewVariables]
    public float Surface = float.NaN;

    /// <summary>The fire grace has been given this cooling cycle.</summary>
    [ViewVariables]
    public bool GraceGranted;

    /// <summary>Seconds of grace left after the fire that granted it first went out; null while it still burns.</summary>
    [ViewVariables]
    public float? GraceLeft;

    /// <summary>The body is in a heat cause it entered with no grace holding it off. A grace never clears it.</summary>
    [ViewVariables]
    public bool HeatHeld;

    /// <summary>The inputs as consciousness reads them: past 1 is past the line.</summary>
    [ViewVariables]
    public float ColdDown;

    [ViewVariables]
    public float ColdOut;

    [ViewVariables]
    public float HeatDown;

    [ViewVariables]
    public float HeatOut;
}
