using Content.Shared._WF.Planets;

namespace Content.Server._WF.Planets;

public sealed partial class WFPlanetClimateSystem
{
    /// <summary>
    /// The outdoor air temperature in kelvin that a map's world has, or had, at a time in clear weather: its season and
    /// the hour, without the storms nobody recorded. For catching up ground that was out of sight. Null off a world.
    /// </summary>
    public float? GetClearTemperatureAt(EntityUid map, TimeSpan at)
    {
        return TryGetState(map, out var state, out var profile) ? GetTarget(state, profile, at, null) : null;
    }
}
