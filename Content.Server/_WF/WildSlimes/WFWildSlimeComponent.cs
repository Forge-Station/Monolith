using System.Numerics;
using Content.Shared._WF.WildSlimes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.WildSlimes;

/// <summary>
/// A slime still living wild on its world: docile, kept near its patch, never starving or splitting. Removed once it is
/// caught, tamed or taken off its world, and it becomes an ordinary xenobiology slime.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFWildSlimeComponent : Component
{
    /// <summary>The habitat it came from, which says how it behaves once caught.</summary>
    [ViewVariables]
    public ProtoId<WFWildSlimeHabitatPrototype> Habitat;

    /// <summary>The map its patch or grotto is on.</summary>
    [ViewVariables]
    public EntityUid HomeMap;

    /// <summary>The other map of its world it may stray onto and still be wild, such as the cavern under the ground.</summary>
    [ViewVariables]
    public EntityUid? OtherMap;

    /// <summary>The middle of its patch, in map coordinates.</summary>
    [ViewVariables]
    public Vector2 Home;

    /// <summary>How far from <see cref="Home"/> it may wander before it heads back.</summary>
    [ViewVariables]
    public float Leash = 6f;

    [ViewVariables]
    public TimeSpan NextCheck;
}
