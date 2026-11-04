using System.Numerics;
using Robust.Shared.Player;

namespace Content.Server._WF.Planets.Hop;

/// <summary>Where a hop is: warming up on the old map, or waiting on the private hold map.</summary>
public enum WFPlanetHopPhase : byte
{
    Warm,
    Transit,
}

/// <summary>A docked grid riding the hull's hop, with its pose relative to the hull.</summary>
public readonly record struct WFHopRider(EntityUid Grid, Vector2 Offset, Angle Rotation);

/// <summary>
/// Rides a hull through its hop into or out of orbit, and the grids docked to it once the hull leaves. The hull's
/// copy holds the plan; a rider's only names the hull.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetHopComponent : Component
{
    /// <summary>The hull making the hop; the grid itself on the hull's copy.</summary>
    [ViewVariables]
    public EntityUid Hull;

    [ViewVariables]
    public WFPlanetHopPhase Phase;

    /// <summary>When the current phase ends.</summary>
    [ViewVariables]
    public TimeSpan PhaseEnd;

    [ViewVariables]
    public TimeSpan Started;

    /// <summary>The sector body the hull is approaching or leaving, for the approach overlay.</summary>
    [ViewVariables]
    public EntityUid? Body;

    /// <summary>True on the way into orbit, false on the way out.</summary>
    [ViewVariables]
    public bool Arriving;

    [ViewVariables]
    public EntityUid TargetMap;

    /// <summary>Where the hull wants to arrive; the landing spot walks off this to stay clear.</summary>
    [ViewVariables]
    public Vector2 Target;

    [ViewVariables]
    public Angle Angle;

    /// <summary>Where the hull left, for a hop whose target vanished.</summary>
    [ViewVariables]
    public EntityUid OriginMap;

    [ViewVariables]
    public Vector2 Origin;

    [ViewVariables]
    public EntityUid? HoldMap;

    [ViewVariables]
    public EntityUid? Visualizer;

    [ViewVariables]
    public EntityUid? StartupStream;

    [ViewVariables]
    public EntityUid? TravelStream;

    [ViewVariables]
    public List<WFHopRider> Riders = new();

    /// <summary>Players who were sent the arriving ships early.</summary>
    public readonly HashSet<ICommonSession> Preloaded = new();

    /// <summary>Everything sent to <see cref="Preloaded"/>.</summary>
    public readonly List<EntityUid> PreloadedGrids = new();
}
