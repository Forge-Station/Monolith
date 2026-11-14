using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Gore;

/// <summary>
/// A spray of blood thrown off a body by a hit, travelling along the exact line of the hit. The server
/// decides everything at spawn; the client tints the sprite, picks the frame set, turns it to
/// <see cref="Angle"/> and slides it along that same angle over <see cref="Travel"/> seconds.
/// </summary>
/// <remarks>
/// Networked rather than client-predicted because wound creation is server-only, and every field has to
/// survive a client entering PVS after the spawn, which a one-shot message would not.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WolfmedHitSplatterComponent : Component
{
    /// <summary>
    /// FIX1: which way the spray is going, in world space, radians anticlockwise from +X. An exact angle
    /// rather than a <c>Direction</c>: a hit from the north-east throws blood to the south-west, not west.
    /// The sprite is turned to it, so the state it uses must be one of the RSI's single-direction ones.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Angle;

    /// <summary>How far it travels, in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float Distance = 1f;

    /// <summary>The victim's blood reagent colour. The art is greyscale.</summary>
    [DataField, AutoNetworkedField]
    public Color Color = Color.White;

    /// <summary>Which of the RSI's spray variants this one uses. Single-direction; see <see cref="Angle"/>.</summary>
    [DataField, AutoNetworkedField]
    public string State = "hitsplatter1_free";

    /// <summary>Seconds the travel takes. Matches the animation the RSI state carries.</summary>
    [DataField, AutoNetworkedField]
    public float Travel = 1.1f;

    /// <summary>Client-local start of the slide. Never networked; a late joiner sees the rest of it.</summary>
    public TimeSpan StartedAt;
}

/// <summary>
/// Blood left on a wall or a window. An entity rather than a decal because decals draw under walls; it
/// carries no physics and nothing that ticks except its own despawn clock.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WolfmedBloodSplatComponent : Component
{
    /// <summary>The blood reagent colour of whoever it came out of.</summary>
    [DataField, AutoNetworkedField]
    public Color Color = Color.White;

    /// <summary>Which of the RSI's wall splatters this is.</summary>
    [DataField, AutoNetworkedField]
    public string State = "splatter1";

    /// <summary>
    /// FIX1: which way it faces, in world space, radians anticlockwise from +X: back along the line the
    /// spray came in on. The client snaps it to the nearest direction its own RSI state actually has.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Angle;
}

/// <summary>
/// Marks an entity that space cleaner washes off a tile, the way it purges a cleanable decal. The floor
/// half of a blood splat is a decal and is already covered; this is for the ones that had to be entities.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedCleanableComponent : Component
{
    /// <summary>Reagent units one of these costs to wash away.</summary>
    [DataField]
    public float CleanCost = 0.5f;
}

/// <summary>
/// Sits on a body that is losing blood fast enough to throw it around, and holds the clock for the next
/// spurt. Added and removed by <c>WolfmedBleedSpurtSystem</c> as the condition comes and goes, so the
/// spurt tick only ever walks bodies that are actually bleeding hard.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedBleedSpurtComponent : Component
{
    /// <summary>Earliest time the next spurt may happen.</summary>
    [DataField]
    public TimeSpan NextSpurt;
}
