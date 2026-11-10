namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Makes the rounds of this gun fly to the aimed point and stop there.</summary>
[RegisterComponent]
public sealed partial class ShootAtFixedPointComponent : Component
{
    /// <summary>The furthest a round of this gun flies, in tiles; the aimed point is used when it is nearer. Null for the module's own cap.</summary>
    [DataField]
    public float? MaxFixedRange;

    /// <summary>A target behind a wall stops the round in front of the wall instead.</summary>
    [DataField]
    public bool AutoAimClosestObstacle;

    /// <summary>The rounds arc over everything except impassable fixtures.</summary>
    [DataField]
    public bool ShootArcProj;
}
