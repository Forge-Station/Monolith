using System.Numerics;

namespace Content.Server.Traits.Assorted;

/// <summary>
/// This is used for the Lactozit Intolerance trait.
/// </summary>
[RegisterComponent, Access(typeof(LactozitIntoleranceSystem))]
public sealed partial class LactozitIntoleranceComponent : Component
{
    /// <summary>
    /// The random cooldown between Lactozitium incidents, (min, max).
    /// </summary>
    [DataField("timeBetweenIncidents", required: true)]
    public Vector2 TimeBetweenIncidents { get; private set; }

    public float TimeUntilNextIncident;
}
