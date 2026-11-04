using System.Numerics;
using System.Threading.Tasks;

namespace Content.Server._WF.Planets.Bounds;

/// <summary>
/// Raised broadcast the tick before a bounded ground starts generating its whole circle, so other modules can place
/// what must go in before the terrain exists, such as cavern mouths. Work that takes more than the tick, such as a
/// dungeon, goes in <see cref="Holds"/>: no chunk loads until every hold has finished.
/// </summary>
[ByRefEvent]
public readonly record struct WFPlanetPreloadStartingEvent(EntityUid Ground, Vector2 Centre, float Radius, List<Task> Holds)
{
    /// <summary>Holds that claim ground, such as cavern mouths: the sites are placed round them once they are done.</summary>
    public List<Task> Claims { get; init; } = new();
}

/// <summary>
/// Raised broadcast once a ground's whole circle is generated and its boundary ring laid. Work that takes more than the
/// tick, such as letting the wildlife loose, goes in <see cref="Work"/>: the ground is marked preloaded once it is done.
/// </summary>
[ByRefEvent]
public readonly record struct WFPlanetPreloadedEvent(EntityUid Ground, List<Task> Work);

/// <summary>On a ground layer whose whole circle is generated and populated: nothing on it streams in or unloads any more.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetPreloadedComponent : Component;
