using System.Numerics;
using Content.Shared.Whitelist;

namespace Content.Server._WF.Remade.Variants;

/// <summary>A Remade that goes for what is in its way when it has made no headway: a railing, a table, a window, a door.</summary>
[RegisterComponent]
public sealed partial class WFStuckBreakerComponent : Component
{
    /// <summary>Seconds without covering <see cref="MoveDistance"/> before it starts breaking things.</summary>
    [DataField]
    public float StuckTime = 3f;

    /// <summary>The least it has to cover in that time to count as moving.</summary>
    [DataField]
    public float MoveDistance = 0.5f;

    /// <summary>How far ahead it looks for what is blocking it.</summary>
    [DataField]
    public float Reach = 1.3f;

    /// <summary>Whether it will break a wall, once it has been stuck <see cref="WallTime"/> more with nothing else in the way.</summary>
    [DataField]
    public bool BreaksWalls;

    /// <summary>Extra seconds stuck before it goes for a wall.</summary>
    [DataField]
    public float WallTime = 3f;

    /// <summary>What it leaves alone, like a Monolith or its own growths.</summary>
    [DataField]
    public EntityWhitelist? Spare;

    /// <summary>Where it was when the stuck clock started.</summary>
    [ViewVariables]
    public Vector2 Anchor;

    [ViewVariables]
    public TimeSpan Since;

    /// <summary>The last time it was checked while steering. A long gap starts the clock again.</summary>
    [ViewVariables]
    public TimeSpan Seen;

    /// <summary>Obstacles it gave up on, until when.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> GaveUp = new();

    /// <summary>The obstacle it is working on, and the damage it had when it last looked.</summary>
    [ViewVariables]
    public EntityUid? Working;

    [ViewVariables]
    public float WorkedDamage;

    /// <summary>Swings in a row that did nothing to it.</summary>
    [ViewVariables]
    public int Wasted;
}
