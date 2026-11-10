using Robust.Shared.Map;

namespace Content.Server._WF.Remade.Critters;

/// <summary>A Remade that, with nothing to hunt, slips onto a table or other low cover and waits there to spring out.</summary>
[RegisterComponent]
public sealed partial class WFHiderComponent : Component
{
    /// <summary>How far it looks for cover.</summary>
    [DataField]
    public float Range = 14f;

    /// <summary>How near the cover it must stand to climb onto it.</summary>
    [DataField]
    public float Reach = 1.6f;

    /// <summary>How far it sees while hidden, so it lets the prey come close before it springs.</summary>
    [DataField]
    public float HiddenSight = 4f;

    /// <summary>Seconds it won't look for cover after it is hurt or can't reach any.</summary>
    [DataField]
    public float Cooldown = 20f;

    /// <summary>The cover it is under.</summary>
    [ViewVariables]
    public EntityUid? Cover;

    /// <summary>Its sight radius before it hid.</summary>
    [ViewVariables]
    public float? Sight;

    [ViewVariables]
    public TimeSpan NoCoverUntil;
}

/// <summary>A Remade that, with nothing to hunt, stands against a wall, and leaps harder from there.</summary>
[RegisterComponent]
public sealed partial class WFWallHuggerComponent : Component
{
    /// <summary>How far it looks for a wall, in tiles.</summary>
    [DataField]
    public int Range = 12;

    /// <summary>How near the middle of the spot it walks to.</summary>
    [DataField]
    public float Reach = 0.3f;

    /// <summary>The wall spot it is walking to or standing at.</summary>
    [ViewVariables]
    public EntityCoordinates? Spot;

    [ViewVariables]
    public TimeSpan NextSearch;
}
